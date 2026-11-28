using Gabinety.Billing;
using Gabinety.Booking.Clients;
using Gabinety.Booking.Features.Bookings.Create;
using Gabinety.Booking.Features.Lifecycle.Core;
using Gabinety.Booking.Payments.Abstractions;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Booking;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using AccessEntity = Gabinety.Booking.Features.Bookings.Entities.Access;
using AccessStatus = Gabinety.Billing.AccessStatus;
using ProviderEvent = Gabinety.Booking.Payments.Abstractions.WebhookEvent;

namespace Gabinety.Booking.Features.Payments;

/// <summary>What the webhook and the simulate endpoint answer: an outcome, or "retry later" (the booking row is not there yet, CONTRACTS 4.3).</summary>
public readonly record struct PaymentEventResult(WebhookOutcome? Outcome)
{
    public static readonly PaymentEventResult RetryLater = new(null);
}

/// <summary>
/// The single handler behind POST /webhooks/payments/{provider} and POST /api/v1/payments/simulate (CONTRACTS 4.3, 8.1).
/// Provider calls (the meeting URL lookup, the refund of a late payment) happen BEFORE the transaction, so no database lock is held across a network call.
/// </summary>
public sealed class PaymentEventHandler(AccessDbContext db, IDirectoryClient directory, IPaymentProvider provider, TimeProvider time,
    IEnumerable<IAccessTransitionHook> hooks, ILogger<PaymentEventHandler> log)
{
    const string UniqueViolation = "23505";
    public const string Currency = "PLN";

    /// <summary>Thrown inside the transaction when the booking moved between the first read and the lock: the whole handling is read again.</summary>
    sealed class StaleReadException : Exception;

    public async Task<PaymentEventResult> HandleAsync(string providerName, ProviderEvent e, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return await HandleOnceAsync(providerName, e, ct); }
            catch (StaleReadException) when (attempt < 2) { db.ChangeTracker.Clear(); }
        }
    }

    async Task<PaymentEventResult> HandleOnceAsync(string providerName, ProviderEvent e, CancellationToken ct)
    {
        if (e.Kind == WebhookEventKind.Unknown)
        {
            log.LogInformation("Payment event of an unknown kind from {Provider} ignored", providerName);
            return new(await RecordOnlyAsync(providerName, e.EventId, WebhookOutcome.Ignored, ct));
        }

        var payment = await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Provider == providerName && p.ProviderPaymentId == e.ProviderPaymentId, ct);
        var access = payment is null ? null : await db.Accesses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == payment.AccessId, ct);
        if (payment is null || access is null) return PaymentEventResult.RetryLater;

        if (await db.WebhookEvents.AnyAsync(x => x.Provider == providerName && x.EventId == e.EventId, ct))
            return new(WebhookOutcome.Duplicate);

        var now = time.GetUtcNow();
        if (e.Kind == WebhookEventKind.PaymentSucceeded && (e.AmountMinor != access.PriceMinor || !string.Equals(e.Currency, Currency, StringComparison.Ordinal)))
        {
            // The ids are not user text; the amounts are the alert's evidence.
            log.LogError("ALERT payment amount or currency mismatch for booking {BookingId}: expected {Expected} {Currency}, got {Got} {GotCurrency}",
                access.Id, access.PriceMinor, Currency, e.AmountMinor, e.Currency);
            return new(await RecordOnlyAsync(providerName, e.EventId, WebhookOutcome.Rejected, ct));
        }

        var late = e.Kind == WebhookEventKind.PaymentSucceeded && IsLate(access, now);
        string? meetingUrl = null;
        if (e.Kind == WebhookEventKind.PaymentSucceeded && !late && access.Status == AccessStatus.PendingPayment)
            meetingUrl = (await directory.GetPackageAsync(access.PackageId, ct))?.MeetingUrl; // DirectoryUnavailableException: the caller answers 503 and the provider retries
        if (late && payment.Status == PaymentStatus.Created)
            await provider.RefundAsync(new RefundRequest(e.ProviderPaymentId!, payment.AmountMinor, $"late:{payment.Id:N}"), ct);

        try
        {
            return new(await ApplyAsync(providerName, e, access.Id, late, meetingUrl, ct));
        }
        catch (DbUpdateException ex) when (IsUnique(ex))
        {
            db.ChangeTracker.Clear();
            return new(WebhookOutcome.Duplicate); // the same event arrived at the same time and won
        }
    }

    static bool IsLate(AccessEntity b, DateTimeOffset now) =>
        b.Status == AccessStatus.Expired || (b.Status == AccessStatus.PendingPayment && b.HoldExpiresAt <= now);

    async Task<WebhookOutcome> ApplyAsync(string providerName, ProviderEvent e, Guid accessId, bool lateAtRead, string? meetingUrl, CancellationToken ct)
    {
        async Task<WebhookOutcome> Body()
        {
            db.ChangeTracker.Clear(); // a retry starts from the database
            var now = time.GetUtcNow();
            var access = await LoadForUpdateAsync(accessId, ct);
            var payment = await db.Payments.SingleAsync(p => p.AccessId == accessId, ct);
            var outcome = WebhookOutcome.Ignored;

            switch (e.Kind)
            {
                case WebhookEventKind.PaymentSucceeded:
                    outcome = ApplySucceeded(access, payment, e, IsLate(access, now), lateAtRead, meetingUrl, now);
                    // PendingPayment -> Confirmed is a transition like any other: the hooks (the notification rows of T-04.09b) run in this same transaction.
                    // Without this no confirmation e-mail was ever scheduled for a paid booking.
                    if (outcome == WebhookOutcome.Applied && access.Status == AccessStatus.Confirmed)
                    {
                        var context = new AccessTransitionContext(db, access, payment, AccessStatus.PendingPayment, AccessStatus.Confirmed, AccessTrigger.PaymentSucceeded, now);
                        foreach (var hook in hooks) await hook.OnTransitionAsync(context, ct);
                    }
                    break;
                case WebhookEventKind.PaymentFailed:
                    if (access.Status == AccessStatus.PendingPayment && payment.Status == PaymentStatus.Created)
                    {
                        access.Status = AccessStateMachine.Next(access.Status, AccessTrigger.PaymentFailed, access.StartUtc, now).Next!.Value;
                        payment.Status = PaymentStatus.Failed;
                        payment.UpdatedAt = now;
                        outcome = WebhookOutcome.Applied;
                    }
                    break;
                case WebhookEventKind.RefundSucceeded:
                    outcome = ApplyRefundSucceeded(access, payment, e, now);
                    break;
            }

            db.WebhookEvents.Add(new WebhookEvent { Provider = providerName, EventId = e.EventId, ReceivedAt = now, Outcome = outcome });
            await db.SaveChangesAsync(ct);
            return outcome;
        }

        if (!db.Database.IsRelational()) return await Body();
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var outcome = await Body();
            await tx.CommitAsync(ct);
            return outcome;
        });
    }

    WebhookOutcome ApplySucceeded(AccessEntity access, Payment payment, ProviderEvent e, bool lateNow, bool lateAtRead, string? meetingUrl, DateTimeOffset now)
    {
        if (payment.Status != PaymentStatus.Created) return WebhookOutcome.Ignored; // a second success for the same payment
        if (lateNow && !lateAtRead) throw new StaleReadException(); // the hold ran out after the first read: it needs the refund call
        if (lateNow)
        {
            // Hold expired, payment succeeded anyway: the money arrived, so it is a charge, and it goes back in full (CONTRACTS 8.1, 9.2).
            if (access.Status == AccessStatus.PendingPayment)
                access.Status = AccessStateMachine.Next(access.Status, AccessTrigger.HoldExpired, access.StartUtc, now).Next!.Value;
            if (access.Status != AccessStatus.Expired) return WebhookOutcome.Ignored;
            AddLedger(access.Id, LedgerKind.ClientCharge, payment.AmountMinor, now);
            AddLedger(access.Id, LedgerKind.Refund, payment.AmountMinor, now);
            payment.Status = PaymentStatus.Refunded;
            payment.RefundedMinor = payment.AmountMinor;
            payment.UpdatedAt = now;
            return WebhookOutcome.Applied;
        }

        var next = AccessStateMachine.Next(access.Status, AccessTrigger.PaymentSucceeded, access.StartUtc, now);
        if (!next.Allowed) return WebhookOutcome.Ignored;
        access.Status = next.Next!.Value;
        access.ConfirmedAt = now;
        access.MeetingUrlSnapshot = meetingUrl is { Length: > 300 } ? meetingUrl[..300] : meetingUrl;
        access.HoldExpiresAt = null;
        payment.Status = PaymentStatus.Succeeded;
        payment.UpdatedAt = now;
        AddLedger(access.Id, LedgerKind.ClientCharge, payment.AmountMinor, now);
        return WebhookOutcome.Applied;
    }

    /// <summary>The provider confirms a refund it was asked for (CONTRACTS 9.2: the ledger row is written then). Already recorded in full is a no-op.</summary>
    WebhookOutcome ApplyRefundSucceeded(AccessEntity access, Payment payment, ProviderEvent e, DateTimeOffset now) =>
        RefundLedger.Record(db, access.Id, payment, e.AmountMinor, now); // shared with the cancel flow (T-04.07a)

    void AddLedger(Guid accessId, LedgerKind kind, long amount, DateTimeOffset now) =>
        db.LedgerEntries.Add(new LedgerEntry { Id = Guid.CreateVersion7(), AccessId = accessId, Kind = kind, AmountMinor = amount, CreatedAt = now });

    /// <summary>The booking row, locked on PostgreSQL so two events for one booking apply one after the other.</summary>
    async Task<AccessEntity> LoadForUpdateAsync(Guid accessId, CancellationToken ct) =>
        db.Database.IsNpgsql()
            ? await db.Accesses.FromSql($"SELECT * FROM bookings WHERE id = {accessId} FOR UPDATE").SingleAsync(ct)
            : await db.Accesses.SingleAsync(b => b.Id == accessId, ct);

    async Task<WebhookOutcome> RecordOnlyAsync(string providerName, string eventId, WebhookOutcome outcome, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(eventId)) return outcome;
        try
        {
            if (await db.WebhookEvents.AnyAsync(x => x.Provider == providerName && x.EventId == eventId, ct)) return WebhookOutcome.Duplicate;
            db.WebhookEvents.Add(new WebhookEvent { Provider = providerName, EventId = eventId, ReceivedAt = time.GetUtcNow(), Outcome = outcome });
            await db.SaveChangesAsync(ct);
            return outcome;
        }
        catch (DbUpdateException ex) when (IsUnique(ex)) { db.ChangeTracker.Clear(); return WebhookOutcome.Duplicate; }
    }

    static bool IsUnique(Exception e)
    {
        for (var x = e; x is not null; x = x.InnerException) if (x is PostgresException { SqlState: UniqueViolation }) return true;
        return false;
    }
}
