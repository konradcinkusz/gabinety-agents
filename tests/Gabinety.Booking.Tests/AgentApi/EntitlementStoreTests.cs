using Gabinety.Billing;
using Gabinety.Booking.Features.AgentApi;
using Gabinety.Contracts.Directory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gabinety.Booking.Tests;

public class EntitlementStoreTests
{
    readonly string Me = AgentApiFx.NewUser(), Other = AgentApiFx.NewUser(), Slug = "demo-agent-" + Guid.NewGuid().ToString("N")[..8];

    static async Task<T> InStore<T>(CreateHost h, Func<EntitlementStore, Task<T>> act)
    {
        using var scope = h.Services.CreateScope();
        return await act(scope.ServiceProvider.GetRequiredService<EntitlementStore>());
    }

    [Fact]
    public async Task Find_returns_the_oldest_usable_package()
    {
        await using var h = new CreateHost();
        var newer = await AgentApiFx.Package(h, Me, Slug, created: TimeSpan.FromHours(2));
        var oldest = await AgentApiFx.Package(h, Me, Slug, created: TimeSpan.FromHours(1));
        var found = await InStore(h, s => s.FindAsync(Me, Slug, OfferingKind.Chat, null, default));
        Assert.Equal(oldest, found!.Id);
        Assert.NotEqual(newer, found.Id);
    }

    [Fact]
    public async Task Find_skips_what_is_not_usable_and_returns_null_when_nothing_is()
    {
        await using var h = new CreateHost();
        await AgentApiFx.Package(h, Me, Slug, status: BookingStatus.PendingPayment);
        await AgentApiFx.Package(h, Me, Slug, status: BookingStatus.CancelledByClient);
        await AgentApiFx.Package(h, Me, Slug, status: BookingStatus.Completed);
        await AgentApiFx.Package(h, Me, Slug, validFor: TimeSpan.Zero);            // EndUtc == now: over
        await AgentApiFx.Package(h, Me, Slug, total: 10, used: 10);                // used up
        await AgentApiFx.Package(h, Me, Slug, kind: OfferingKind.Tasks, total: 5); // other kind
        await AgentApiFx.Package(h, Me, Slug + "x");                               // other agent
        await AgentApiFx.Package(h, Other, Slug);                                  // other client
        Assert.Null(await InStore(h, s => s.FindAsync(Me, Slug, OfferingKind.Chat, null, default)));
    }

    [Fact]
    public async Task Find_can_ask_for_one_offering()
    {
        await using var h = new CreateHost();
        var wanted = Guid.NewGuid();
        await AgentApiFx.Package(h, Me, Slug, OfferingKind.Tasks, total: 3, offering: Guid.NewGuid(), created: TimeSpan.FromMinutes(1));
        var id = await AgentApiFx.Package(h, Me, Slug, OfferingKind.Tasks, total: 3, offering: wanted, created: TimeSpan.FromMinutes(2));
        Assert.Equal(id, (await InStore(h, s => s.FindAsync(Me, Slug, OfferingKind.Tasks, wanted, default)))!.Id);
        Assert.Null(await InStore(h, s => s.FindAsync(Me, Slug, OfferingKind.Tasks, Guid.NewGuid(), default)));
    }

    [Fact]
    public async Task A_package_that_ran_out_is_exhausted_until_it_expires_whether_the_job_completed_it_or_not()
    {
        await using var h = new CreateHost();
        Assert.False(await InStore(h, s => s.HasExhaustedAsync(Me, Slug, OfferingKind.Chat, null, default)));
        await AgentApiFx.Package(h, Me, Slug, total: 10, used: 12);
        Assert.True(await InStore(h, s => s.HasExhaustedAsync(Me, Slug, OfferingKind.Chat, null, default)));
        Assert.False(await InStore(h, s => s.HasExhaustedAsync(Other, Slug, OfferingKind.Chat, null, default)));
        Assert.False(await InStore(h, s => s.HasExhaustedAsync(Me, Slug, OfferingKind.Tasks, null, default)));

        var completed = AgentApiFx.NewUser();
        await AgentApiFx.Package(h, completed, Slug, total: 10, used: 10, status: BookingStatus.Completed);
        Assert.True(await InStore(h, s => s.HasExhaustedAsync(completed, Slug, OfferingKind.Chat, null, default)));

        var expired = AgentApiFx.NewUser();
        await AgentApiFx.Package(h, expired, Slug, total: 10, used: 10, validFor: TimeSpan.Zero);
        Assert.False(await InStore(h, s => s.HasExhaustedAsync(expired, Slug, OfferingKind.Chat, null, default)));
    }

    [Fact]
    public async Task UsableSlugs_lists_the_distinct_agents_of_the_kind_sorted()
    {
        await using var h = new CreateHost();
        await AgentApiFx.Package(h, Me, "demo-b-" + Slug);
        await AgentApiFx.Package(h, Me, "demo-b-" + Slug);
        await AgentApiFx.Package(h, Me, "demo-a-" + Slug);
        await AgentApiFx.Package(h, Me, "demo-tasks-" + Slug, OfferingKind.Tasks, total: 5);
        await AgentApiFx.Package(h, Me, "demo-used-" + Slug, total: 5, used: 5);
        await AgentApiFx.Package(h, Other, "demo-theirs-" + Slug);
        Assert.Equal(["demo-a-" + Slug, "demo-b-" + Slug], await InStore(h, s => s.UsableSlugsAsync(Me, OfferingKind.Chat, default)));
    }

    [Fact]
    public async Task Twenty_parallel_ConsumeAsync_of_one_unit_add_up_to_twenty()
    {
        await using var h = new CreateHost();
        var id = await AgentApiFx.Package(h, Me, Slug, total: 1000);
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => InStore(h, async s => { await s.ConsumeAsync(id, 1, default); return 0; }))));
        Assert.Equal(20, (await h.Query(db => db.Bookings.AsNoTracking().SingleAsync(b => b.Id == id))).UnitsUsed);
    }

    [Fact]
    public async Task Consume_may_go_past_the_total_and_Refund_never_goes_below_zero()
    {
        await using var h = new CreateHost();
        var id = await AgentApiFx.Package(h, Me, Slug, total: 10, used: 8);
        await InStore(h, async s => { await s.ConsumeAsync(id, 5, default); return 0; });
        Assert.Equal(13, (await h.Query(db => db.Bookings.AsNoTracking().SingleAsync(b => b.Id == id))).UnitsUsed);
        await InStore(h, async s => { await s.RefundUnitsAsync(id, 3, default); return 0; });
        Assert.Equal(10, (await h.Query(db => db.Bookings.AsNoTracking().SingleAsync(b => b.Id == id))).UnitsUsed);
        await InStore(h, async s => { await s.RefundUnitsAsync(id, 50, default); return 0; });
        Assert.Equal(0, (await h.Query(db => db.Bookings.AsNoTracking().SingleAsync(b => b.Id == id))).UnitsUsed);
    }

    [Fact]
    public async Task Consuming_an_unknown_package_changes_nothing_and_does_not_throw()
    {
        await using var h = new CreateHost();
        await InStore(h, async s => { await s.ConsumeAsync(Guid.NewGuid(), 1, default); await s.RefundUnitsAsync(Guid.NewGuid(), 1, default); return 0; });
    }
}
