using Gabinety.Billing;
using Gabinety.Booking.Features.AgentApi;
using Gabinety.Booking.Features.Bookings.Create;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Common;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Access;
using AccessStatus = Gabinety.Billing.AccessStatus;

namespace Gabinety.Booking.Features.Lifecycle.Core;

/// <summary>What a hook or an effect sees while the transition's transaction is open. <see cref="Db"/> is the transaction's own context: add rows to it, never save.</summary>
public sealed record BookingTransitionContext(BookingDbContext Db, BookingEntity Access, Payment? Payment, AccessStatus From, AccessStatus To,
    AccessTrigger Trigger, DateTimeOffset Now);

/// <summary>
/// Extension point: called inside the transition's transaction, after the status changed and before it is saved, for EVERY transition.
/// Nothing is registered today; T-04.09b registers the notification scheduler here. A hook that throws rolls the whole transition back.
/// </summary>
public interface IBookingTransitionHook
{
    Task OnTransitionAsync(BookingTransitionContext context, CancellationToken ct);
}

/// <summary>
/// One transition to apply. <see cref="Now"/> is taken once by the caller, so the decision made before the transaction (a refund amount) and the
/// one made inside it agree. <see cref="Precondition"/> is the time or party rule the state table cannot see (CONTRACTS 8.1): false means 409.
/// <see cref="Effects"/> sets what is specific to the trigger (a reason, a dispute timestamp, a refund record); it runs inside the transaction,
/// possibly more than once if the database retries, so it must only set values and add rows.
/// </summary>
public sealed record TransitionRequest(Guid AccessId, AccessTrigger Trigger, DateTimeOffset Now)
{
    public Func<BookingEntity, bool>? Precondition { get; init; }
    /// <summary>A rule that needs the database (a row of another table), checked inside the transaction under the booking's row lock, after <see cref="Precondition"/>: false means 409. Read only.</summary>
    public Func<BookingDbContext, BookingEntity, CancellationToken, Task<bool>>? DbPrecondition { get; init; }
    public Action<BookingTransitionContext>? Effects { get; init; }
}

public enum TransitionKind { Applied, NotFound, Invalid }

/// <summary>Applied carries the committed booking and its payment; From is the status before (the one that refused, for Invalid).</summary>
public sealed record BookingTransitionResult(TransitionKind Kind, BookingEntity? Access = null, Payment? Payment = null, AccessStatus? From = null)
{
    public bool Applied => Kind == TransitionKind.Applied;

    /// <summary>404 not_found or 409 invalid_transition for a result that did not apply; null when it did.</summary>
    public IResult? ToErrorResult() => Kind switch
    {
        TransitionKind.NotFound => PaymentWebhookFeature.Problem(ErrorCodes.NotFound, "Not found", 404),
        TransitionKind.Invalid => PaymentWebhookFeature.Problem(ErrorCodes.InvalidTransition, "Invalid transition", 409),
        _ => null,
    };
}

/// <summary>
/// The only place a <see cref="AccessTrigger"/> is applied to a booking by the lifecycle (CONTRACTS 8.1). It runs <see cref="AccessStateMachine.Next"/>
/// on the booking row under a lock (SELECT ... FOR UPDATE on PostgreSQL, so a concurrent request waits and then sees the new status),
/// sets the status and the timestamps every cancellation shares, lets the request's effects and the registered hooks write their rows, and commits once.
/// A new trigger needs no change here: add it to the state table and pass its own precondition and effects.
/// Provider calls must happen BEFORE <see cref="ApplyAsync"/>: no lock is held across a network call.
/// </summary>
public sealed class BookingTransitions(BookingDbContext db, IEnumerable<IBookingTransitionHook> hooks, ILogger<BookingTransitions> log)
{
    public async Task<BookingTransitionResult> ApplyAsync(TransitionRequest request, CancellationToken ct)
    {
        async Task<BookingTransitionResult> Body()
        {
            db.ChangeTracker.Clear(); // a retry starts from the database
            var booking = db.Database.IsNpgsql()
                ? await db.Accesses.FromSql($"SELECT * FROM bookings WHERE id = {request.AccessId} FOR UPDATE").SingleOrDefaultAsync(ct)
                : await db.Accesses.SingleOrDefaultAsync(b => b.Id == request.AccessId, ct);
            if (booking is null) return new(TransitionKind.NotFound);

            var from = booking.Status;
            var next = AccessStateMachine.Next(from, request.Trigger, booking.StartUtc, request.Now);
            if (!next.Allowed || request.Precondition?.Invoke(booking) == false) return new(TransitionKind.Invalid, From: from);
            if (request.DbPrecondition is { } dbRule && !await dbRule(db, booking, ct)) return new(TransitionKind.Invalid, From: from);

            var to = next.Next!.Value;
            booking.Status = to;
            StampCancellation(booking, to, request.Now);
            var payment = await db.Payments.SingleOrDefaultAsync(p => p.AccessId == booking.Id, ct);
            var context = new BookingTransitionContext(db, booking, payment, from, to, request.Trigger, request.Now);
            request.Effects?.Invoke(context);
            foreach (var hook in hooks) await hook.OnTransitionAsync(context, ct);
            await db.SaveChangesAsync(ct);
            return new(TransitionKind.Applied, booking, payment, from);
        }

        BookingTransitionResult result;
        if (!db.Database.IsRelational())
        {
            // InMemory has no row lock: one gate for the read, the checks and the save, shared with EntitlementStore's InMemory path, so that a completion and a refund of a unit (ADR-0013) are never interleaved
            // within one process (the fake profile and the tests). On PostgreSQL the FOR UPDATE above does this.
            await EntitlementStore.InMemoryGate.WaitAsync(ct);
            try { result = await Body(); }
            finally { EntitlementStore.InMemoryGate.Release(); }
        }
        else
            result = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var r = await Body();
                if (r.Applied) await tx.CommitAsync(ct);
                return r;
            });

        if (result.Applied) log.LogInformation("Booking {BookingId} moved {From} -> {To} by {Trigger}", request.AccessId, result.From, result.Access!.Status, request.Trigger);
        return result;
    }

    /// <summary>What the three cancellations share (CONTRACTS 8.1): the time, and for a late client cancel the payout date. Other triggers set theirs in their effects.</summary>
    static void StampCancellation(BookingEntity b, AccessStatus to, DateTimeOffset now)
    {
        if (to is not (AccessStatus.CancelledByClient or AccessStatus.LateCancelledByClient or AccessStatus.CancelledByPractitioner)) return;
        b.CancelledAt = now;
        if (to == AccessStatus.LateCancelledByClient) b.PayoutDueAt = b.StartUtc;
    }
}

/// <summary>Registers the lifecycle core. Hooks are resolved as <c>IEnumerable&lt;IBookingTransitionHook&gt;</c>: add one with <c>AddScoped&lt;IBookingTransitionHook, ...&gt;</c>.</summary>
public sealed class LifecycleCoreFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder)
    {
        builder.Services.AddScoped<BookingTransitions>();
        builder.Services.AddScoped<RefundCoordinator>();
    }

    public void Map(IEndpointRouteBuilder endpoints) { }
}
