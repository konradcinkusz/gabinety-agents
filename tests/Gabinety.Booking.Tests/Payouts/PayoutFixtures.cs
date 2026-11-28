using Gabinety.Booking.Features.Bookings.Create;
using Gabinety.Booking.Features.Lifecycle.Core;
using Gabinety.Booking.Features.Payments;
using Gabinety.Contracts.Booking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AccessEntity = Gabinety.Booking.Features.Bookings.Entities.Access;
using AccessStatus = Gabinety.Billing.AccessStatus;
using AccessTrigger = Gabinety.Billing.AccessTrigger;

namespace Gabinety.Booking.Tests;

/// <summary>Helpers for the fee and payout tests: bookings of any pair seeded in any status, and a transition applied through <see cref="AccessTransitions"/>.</summary>
static class Fee
{
    public static string NewClient() => "demo-" + Guid.NewGuid().ToString("N");

    /// <summary>A booking with a succeeded payment (and its charge) for the given pair, starting <paramref name="startIn"/> after the host clock.</summary>
    public static async Task<Guid> Seed(CreateHost h, AccessStatus status, string client, Guid agent, long price, TimeSpan? startIn = null, long refunded = 0, string? resolution = null)
    {
        var id = Guid.NewGuid();
        var start = h.Clock.GetUtcNow() + (startIn ?? TimeSpan.FromDays(2));
        await h.Seed(db =>
        {
            db.Accesses.Add(new AccessEntity
            {
                Id = id, ClientUserId = client, AgentId = agent, AgentUserId = "demo-practitioner-user", PackageId = h.Package,
                PackageTitle = h.Title, DurationMinutes = 60, PriceMinor = price, StartUtc = start, EndUtc = start.AddHours(1), Status = status,
                BriefText = "demo-brief: a first review of my plan", IdempotencyKey = Guid.NewGuid(), CreatedAt = h.Clock.GetUtcNow(),
                DisputeResolution = resolution,
            });
            db.Payments.Add(new Payment
            {
                Id = Guid.NewGuid(), AccessId = id, Provider = "Mock", ProviderPaymentId = "pay_" + id.ToString("N"), AmountMinor = price, RefundedMinor = refunded,
                Status = PaymentStatus.Succeeded, CreatedAt = h.Clock.GetUtcNow(), UpdatedAt = h.Clock.GetUtcNow(),
            });
        });
        return id;
    }

    public static async Task<AccessTransitionResult> Apply(CreateHost h, Guid id, AccessTrigger trigger, Action<AccessTransitionContext>? effects = null)
    {
        using var scope = h.Services.CreateScope();
        var t = scope.ServiceProvider.GetRequiredService<AccessTransitions>();
        return await t.ApplyAsync(new TransitionRequest(id, trigger, h.Clock.GetUtcNow()) { Effects = effects }, default);
    }

    public static async Task<(long? Fee, long? Payout)> Amounts(CreateHost h, Guid id)
    {
        var b = await Life.Row(h, id);
        return (b.FeeMinor, b.PayoutMinor);
    }

    public static Task<List<LedgerEntry>> FeeRows(CreateHost h, string client, Guid agent) =>
        h.Query(db => db.LedgerEntries.AsNoTracking().Where(l => l.Kind == LedgerKind.PlatformFee && l.PairKey == client + ":" + agent).ToListAsync());
}
