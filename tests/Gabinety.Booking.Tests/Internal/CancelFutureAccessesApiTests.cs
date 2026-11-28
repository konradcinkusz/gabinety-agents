using System.Net;
using System.Text.Json;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Notifications;
using Gabinety.Booking.Payments.Abstractions;
using Gabinety.Contracts.Booking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using AccessStatus = Gabinety.Billing.AccessStatus;

namespace Gabinety.Booking.Tests;

/// <summary>
/// POST /internal/v1/practitioners/{id}/cancel-future-bookings: Directory calls it when a practitioner is suspended or erased. Every future Confirmed booking
/// goes through the practitioner-cancel transition (CONTRACTS 8.1, 9.2): full refund, <c>CancelledByPractitioner</c>, reason <c>practitioner_suspended</c>,
/// a <c>Cancelled</c> notification for the client. Holds expire. Idempotent.
/// </summary>
public class CancelFutureAccessesApiTests
{
    const string Key = "k-internal-cancel-future-DO-NOT-LEAK";
    static readonly TimeSpan Day = TimeSpan.FromDays(1);

    sealed class FlakyProvider(RecordingProvider inner) : IPaymentProvider
    {
        public bool Down;
        public string Name => inner.Name;
        public Task<CheckoutResult> CreateCheckoutAsync(CheckoutRequest r, CancellationToken ct) => inner.CreateCheckoutAsync(r, ct);
        public Task<RefundResult> RefundAsync(RefundRequest r, CancellationToken ct) => Down ? throw new HttpRequestException("provider down") : inner.RefundAsync(r, ct);
        public Task<PayoutResult> PayoutAsync(PayoutRequest r, CancellationToken ct) => inner.PayoutAsync(r, ct);
        public Gabinety.Booking.Payments.Abstractions.WebhookEvent? VerifyWebhook(ReadOnlySpan<byte> rawBody, string? sig) => inner.VerifyWebhook(rawBody, sig);
    }

    static CreateHost Host(IPaymentProvider provider)
    {
        var h = Life.Host(new RecordingProvider());
        h.Settings["Internal:Key"] = Key;
        h.Configure += s => { s.RemoveAll<IPaymentProvider>(); s.AddSingleton(provider); };
        return h;
    }

    static Task<HttpResponseMessage> Call(CreateHost h, Guid agent, string? key = Key)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/internal/v1/practitioners/{agent}/cancel-future-bookings");
        if (key is not null) req.Headers.Add("X-Internal-Key", key);
        return h.CreateClient().SendAsync(req);
    }

    static async Task<JsonElement> Json(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
    static Task<List<Notification>> Notes(CreateHost h, Guid id) => h.Query(db => db.Notifications.AsNoTracking().Where(n => n.AccessId == id).ToListAsync());

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task Without_the_internal_key_nothing_happens(string? key)
    {
        await using var h = Host(new RecordingProvider());
        var id = await Life.Seed(h, AccessStatus.Confirmed, 3 * Day);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Call(h, h.Agent, key)).StatusCode);
        Assert.Equal(AccessStatus.Confirmed, (await Life.Row(h, id)).Status);
    }

    [Fact]
    public async Task A_future_confirmed_access_is_cancelled_by_the_agent_with_a_full_refund_and_no_client_penalty()
    {
        var provider = new RecordingProvider();
        await using var h = Host(provider);
        var id = await Life.Seed(h, AccessStatus.Confirmed, 3 * Day);

        var r = await Call(h, h.Agent);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await Json(r);
        Assert.Equal((1, 0, 0), (body.GetProperty("cancelled").GetInt32(), body.GetProperty("holdsExpired").GetInt32(), body.GetProperty("failed").GetInt32()));

        var row = await Life.Row(h, id);
        Assert.Equal(AccessStatus.CancelledByPractitioner, row.Status);
        Assert.Equal("practitioner_suspended", row.StatusReason);
        Assert.Equal(h.Clock.GetUtcNow(), row.CancelledAt);
        Assert.Null(row.PayoutDueAt); // the client pays nothing and the practitioner earns nothing (CONTRACTS 9.2)
        var pay = await Life.PaymentOf(h, id);
        Assert.Equal((PaymentStatus.Refunded, h.PriceMinor), (pay.Status, pay.RefundedMinor));
        Assert.Equal(h.PriceMinor, Assert.Single(await Life.RefundRows(h, id)).AmountMinor);
        Assert.Equal(h.PriceMinor, Assert.Single(provider.Refunds).AmountMinor);
        Assert.DoesNotContain(await Life.Ledger(h, id), l => l.Kind is LedgerKind.PlatformFee or LedgerKind.PractitionerPayout);
    }

    [Fact]
    public async Task Even_a_access_inside_the_24h_window_is_refunded_in_full()
    {
        await using var h = Host(new RecordingProvider());
        var id = await Life.Seed(h, AccessStatus.Confirmed, TimeSpan.FromHours(3)); // a client cancel here would be late and refund nothing
        Assert.Equal(HttpStatusCode.OK, (await Call(h, h.Agent)).StatusCode);
        Assert.Equal((AccessStatus.CancelledByPractitioner, h.PriceMinor), ((await Life.Row(h, id)).Status, (await Life.PaymentOf(h, id)).RefundedMinor));
    }

    [Fact]
    public async Task The_client_is_told_with_the_existing_Cancelled_notification_and_the_unsent_reminders_go()
    {
        await using var h = Host(new RecordingProvider());
        var id = await Life.Seed(h, AccessStatus.Confirmed, 3 * Day);
        await h.Seed(db => db.Notifications.AddRange(
            new Notification { Id = Guid.NewGuid(), AccessId = id, Kind = NotificationKind.Reminder24h, ToUserId = Life.Client, DueAt = h.Clock.GetUtcNow().AddDays(2) },
            new Notification { Id = Guid.NewGuid(), AccessId = id, Kind = NotificationKind.ReviewRequest, ToUserId = Life.Client, DueAt = h.Clock.GetUtcNow().AddDays(4) }));

        await Call(h, h.Agent);
        var notes = await Notes(h, id);
        var cancelled = Assert.Single(notes);
        Assert.Equal((NotificationKind.Cancelled, Life.Client, h.Clock.GetUtcNow()), (cancelled.Kind, cancelled.ToUserId, cancelled.DueAt));
    }

    [Fact]
    public async Task Only_future_confirmed_accesses_of_that_agent_are_touched_and_holds_expire()
    {
        await using var h = Host(new RecordingProvider());
        var future = await Life.Seed(h, AccessStatus.Confirmed, 3 * Day);
        var hold = await Life.Seed(h, AccessStatus.PendingPayment, 5 * Day);
        var started = await Life.Seed(h, AccessStatus.Confirmed, -TimeSpan.FromMinutes(10), TimeSpan.FromHours(1)); // under way: it runs its course
        var finished = await Life.Seed(h, AccessStatus.Completed, -3 * Day);
        var clientCancelled = await Life.Seed(h, AccessStatus.CancelledByClient, 4 * Day);
        var disputed = await Life.Seed(h, AccessStatus.Disputed, -Day);
        var elsewhere = await Life.Seed(h, AccessStatus.Confirmed, 3 * Day);
        await h.Seed(db => db.Accesses.Single(b => b.Id == elsewhere).AgentId = Guid.NewGuid());

        var body = await Json(await Call(h, h.Agent));
        Assert.Equal((1, 1), (body.GetProperty("cancelled").GetInt32(), body.GetProperty("holdsExpired").GetInt32()));

        Assert.Equal(AccessStatus.CancelledByPractitioner, (await Life.Row(h, future)).Status);
        Assert.Equal(AccessStatus.Expired, (await Life.Row(h, hold)).Status);
        Assert.Equal(AccessStatus.Confirmed, (await Life.Row(h, started)).Status);
        Assert.Equal(AccessStatus.Completed, (await Life.Row(h, finished)).Status);
        Assert.Equal(AccessStatus.CancelledByClient, (await Life.Row(h, clientCancelled)).Status);
        Assert.Equal(AccessStatus.Disputed, (await Life.Row(h, disputed)).Status);
        Assert.Equal(AccessStatus.Confirmed, (await Life.Row(h, elsewhere)).Status);
    }

    [Fact]
    public async Task A_repeated_call_changes_nothing_and_refunds_once()
    {
        var provider = new RecordingProvider();
        await using var h = Host(provider);
        var id = await Life.Seed(h, AccessStatus.Confirmed, 3 * Day);
        await Call(h, h.Agent);
        var again = await Call(h, h.Agent);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(0, (await Json(again)).GetProperty("cancelled").GetInt32());
        Assert.Single(provider.Refunds);
        Assert.Single(await Life.RefundRows(h, id));
        Assert.Single(await Notes(h, id));
    }

    [Fact]
    public async Task An_unknown_agent_is_a_no_op_not_an_error()
    {
        await using var h = Host(new RecordingProvider());
        var r = await Call(h, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(0, (await Json(r)).GetProperty("cancelled").GetInt32());
    }

    [Fact]
    public async Task A_provider_that_is_down_leaves_the_access_confirmed_answers_503_and_the_next_call_finishes_it()
    {
        var provider = new FlakyProvider(new RecordingProvider()) { Down = true };
        await using var h = Host(provider);
        var broken = await Life.Seed(h, AccessStatus.Confirmed, 3 * Day);

        var first = await Call(h, h.Agent);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);
        Assert.Equal("dependency_unavailable", (await Json(first)).GetProperty("code").GetString());
        Assert.Equal(AccessStatus.Confirmed, (await Life.Row(h, broken)).Status);
        Assert.Empty(await Life.RefundRows(h, broken));

        provider.Down = false;
        Assert.Equal(HttpStatusCode.OK, (await Call(h, h.Agent)).StatusCode);
        Assert.Equal(AccessStatus.CancelledByPractitioner, (await Life.Row(h, broken)).Status);
        Assert.Equal(h.PriceMinor, (await Life.PaymentOf(h, broken)).RefundedMinor);
    }

    [Fact]
    public async Task One_failing_refund_does_not_stop_the_others()
    {
        var provider = new FlakyProvider(new RecordingProvider());
        await using var h = Host(provider);
        var a = await Life.Seed(h, AccessStatus.Confirmed, 3 * Day);
        var b = await Life.Seed(h, AccessStatus.Confirmed, 4 * Day);
        await h.Seed(db => db.Payments.Single(p => p.AccessId == a).ProviderPaymentId = null); // cannot be refunded: counted as failed, not thrown
        var r = await Call(h, h.Agent);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        Assert.Equal(AccessStatus.Confirmed, (await Life.Row(h, a)).Status);
        Assert.Equal(AccessStatus.CancelledByPractitioner, (await Life.Row(h, b)).Status);
    }

    [Fact]
    public async Task A_hold_is_expired_at_once_so_a_late_payment_meets_the_existing_expired_and_refund_rule()
    {
        await using var h = Host(new RecordingProvider());
        var hold = await Life.Seed(h, AccessStatus.PendingPayment, 5 * Day);
        await Call(h, h.Agent);
        Assert.Equal(AccessStatus.Expired, (await Life.Row(h, hold)).Status); // a later PaymentSucceeded finds Expired: CONTRACTS 8.1 refunds it in full
    }
}
