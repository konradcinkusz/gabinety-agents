using System.Net;
using System.Net.Http.Json;
using Gabinety.Billing;
using Gabinety.Booking.Agents.Abstractions;
using Gabinety.Booking.Features.AgentApi;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Features.Payouts;
using Gabinety.Contracts.Booking;
using Gabinety.Contracts.Directory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Access;
using AccessStatus = Gabinety.Billing.AccessStatus;

namespace Gabinety.Booking.Tests;

/// <summary>Helpers shared by the InMemory and the PostgreSQL tests of ADR-0013, owner decision 2: a package completed because its units ran out, whose last task then fails.</summary>
static class Reopen
{
    public static readonly string Input = TaskFx.Input;

    public static CreateHost Host(params (string Key, string Value)[] settings)
    {
        var h = new CreateHost();
        h.Settings["Jobs:Enabled"] = "false";
        foreach (var (k, v) in settings) h.Settings[k] = v;
        h.Configure = s => { s.RemoveAll<IAgentUpstream>(); s.AddSingleton<IAgentUpstream>(new SpyUpstream { Throw = new UpstreamException(503) }); }; // the platform never answers
        return h;
    }

    /// <summary>A confirmed <c>Tasks</c> package of <paramref name="total"/> units with every unit ordered through the real endpoint (so each has a queued task and a used unit).</summary>
    public static async Task<(Guid Package, List<Guid> Tasks)> Ordered(CreateHost h, string client, string slug, Guid offer, long total = 1, TimeSpan? validFor = null)
    {
        var package = await AgentApiFx.Package(h, client, slug, PackageKind.Tasks, total, offering: offer, validFor: validFor);
        var tasks = new List<Guid>();
        for (var i = 0; i < total; i++)
            tasks.Add((await (await h.ClientFor(client).PostAsJsonAsync($"/api/v1/bookings/{package}/tasks", new { input = Input })).Body()).GetProperty("id").GetGuid());
        return (package, tasks);
    }

    /// <summary>Three starts that fail: the task ends <c>Failed</c> and its unit goes back.</summary>
    public static async Task FailAll(CreateHost h, string client)
    {
        for (var i = 0; i < 3; i++) await TaskFx.Job(h, client).RunOnceAsync(h.Clock.GetUtcNow(), default);
    }

    public static Task<int> Complete(CreateHost h) => Auto.Job(h).CompleteDueAsync(h.Clock.GetUtcNow(), default);

    public static async Task<List<LedgerEntry>> Ledger(CreateHost h, Guid package) => await Life.Ledger(h, package);
}

public class PackageReopeningTests
{
    readonly string Me = AgentApiFx.NewUser(), Slug = "demo-agent-" + Guid.NewGuid().ToString("N")[..8];
    readonly Guid Offer = Guid.NewGuid();

    // ---- the rule on the row (pure)

    static BookingEntity Row(Action<BookingEntity>? tweak = null)
    {
        var now = new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero);
        var b = new BookingEntity { Status = AccessStatus.Completed, Kind = PackageKind.Tasks, UnitsTotal = 3, UnitsUsed = 2, StartUtc = now.AddDays(-1), EndUtc = now.AddDays(29) };
        tweak?.Invoke(b);
        return b;
    }

    static readonly DateTimeOffset Now = new(2026, 12, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact] public void A_completed_Tasks_package_with_a_free_unit_before_its_end_may_reopen() => Assert.True(PackageReopening.CanReopen(Row(), Now));

    [Theory]
    [InlineData("status-confirmed")]
    [InlineData("status-disputed")]
    [InlineData("status-resolved")]
    [InlineData("chat")]
    [InlineData("no-free-unit")]
    [InlineData("legacy-no-units")]
    [InlineData("ended")]
    [InlineData("dispute-opened")]
    public void Anything_else_does_not(string what)
    {
        var b = Row(x =>
        {
            switch (what)
            {
                case "status-confirmed": x.Status = AccessStatus.Confirmed; break;
                case "status-disputed": x.Status = AccessStatus.Disputed; break;
                case "status-resolved": x.Status = AccessStatus.Resolved; break;
                case "chat": x.Kind = PackageKind.Chat; break;
                case "no-free-unit": x.UnitsUsed = 3; break;
                case "legacy-no-units": x.UnitsTotal = 0; x.UnitsUsed = 0; break;
                case "ended": x.EndUtc = Now; break; // the end itself is already too late
                case "dispute-opened": x.DisputeOpenedAt = Now; break;
            }
        });
        Assert.False(PackageReopening.CanReopen(b, Now));
    }

    // ---- the whole path on InMemory

    [Fact]
    public async Task A_failed_last_task_reopens_the_package_completed_by_exhaustion_and_takes_the_money_decision_back()
    {
        await using var h = Reopen.Host();
        var (package, tasks) = await Reopen.Ordered(h, Me, Slug, Offer);

        Assert.Equal(1, await Reopen.Complete(h)); // the unit is used up: the job completes the package
        var done = await Life.Row(h, package);
        Assert.Equal(AccessStatus.Completed, done.Status);
        Assert.NotNull(done.FeeMinor); Assert.NotNull(done.PayoutMinor); Assert.NotNull(done.PayoutDueAt); Assert.NotNull(done.EndedAt);
        Assert.Single(await Reopen.Ledger(h, package), l => l.Kind == LedgerKind.PlatformFee);

        await Reopen.FailAll(h, Me);
        Assert.Equal(AgentTaskStatus.Failed, (await TaskFx.Task(h, tasks[0])).Status);

        var b = await Life.Row(h, package);
        Assert.Equal((AccessStatus.Confirmed, 0L), (b.Status, b.UnitsUsed)); // the client has the unit back and may use it
        Assert.Equal((null, null, null, null), (b.FeeMinor, b.PayoutMinor, b.PayoutDueAt, b.EndedAt));
        Assert.DoesNotContain(await Reopen.Ledger(h, package), l => l.Kind == LedgerKind.PlatformFee);
        Assert.Equal(0, await Reopen.Complete(h)); // not due any more: the unit is free
    }

    [Fact]
    public async Task The_reopened_package_takes_a_new_task_and_when_that_one_uses_the_unit_it_completes_again_with_the_fee_claimed_again()
    {
        await using var h = Reopen.Host();
        var (package, _) = await Reopen.Ordered(h, Me, Slug, Offer);
        await Reopen.Complete(h);
        await Reopen.FailAll(h, Me);

        var again = await h.ClientFor(Me).PostAsJsonAsync($"/api/v1/bookings/{package}/tasks", new { input = Reopen.Input });
        Assert.Equal(HttpStatusCode.Accepted, again.StatusCode);
        Assert.Equal(1, await Reopen.Complete(h));
        var b = await Life.Row(h, package);
        Assert.Equal(AccessStatus.Completed, b.Status);
        Assert.NotNull(b.FeeMinor);
        Assert.Single(await Reopen.Ledger(h, package), l => l.Kind == LedgerKind.PlatformFee); // the pair was free again: exactly one fee row, not zero and not two
    }

    [Fact]
    public async Task With_two_units_one_failed_task_gives_a_free_unit_and_reopens_while_the_other_task_is_untouched()
    {
        await using var h = Reopen.Host();
        var (package, tasks) = await Reopen.Ordered(h, Me, Slug, Offer, total: 2);
        Assert.Equal(1, await Reopen.Complete(h)); // both units are used: completed
        await h.Query(async db => // the first task has already succeeded; only the second can fail
        {
            var first = await db.AgentTasks.SingleAsync(t => t.Id == tasks[0]);
            first.Status = AgentTaskStatus.Succeeded; first.OutputText = "demo-answer"; first.CompletedAt = h.Clock.GetUtcNow();
            await db.SaveChangesAsync();
            return 0;
        });
        await Reopen.FailAll(h, Me);

        var b = await Life.Row(h, package);
        Assert.Equal((AccessStatus.Confirmed, 1L), (b.Status, b.UnitsUsed)); // one unit is free again
        Assert.Equal(AgentTaskStatus.Failed, (await TaskFx.Task(h, tasks[1])).Status);
        Assert.Equal((AgentTaskStatus.Succeeded, "demo-answer"), ((await TaskFx.Task(h, tasks[0])).Status, (await TaskFx.Task(h, tasks[0])).OutputText));
    }

    [Fact]
    public async Task After_the_end_of_the_validity_nothing_is_reopened_but_the_unit_still_goes_back()
    {
        await using var h = Reopen.Host();
        var (package, _) = await Reopen.Ordered(h, Me, Slug, Offer, validFor: TimeSpan.FromDays(2));
        await Reopen.Complete(h);
        var before = await Life.Row(h, package);
        h.Clock.Advance(TimeSpan.FromDays(3)); // past EndUtc
        await Reopen.FailAll(h, Me);

        var b = await Life.Row(h, package);
        Assert.Equal((AccessStatus.Completed, 0L), (b.Status, b.UnitsUsed));
        Assert.Equal((before.FeeMinor, before.PayoutMinor, before.PayoutDueAt, before.EndedAt), (b.FeeMinor, b.PayoutMinor, b.PayoutDueAt, b.EndedAt));
        Assert.Single(await Reopen.Ledger(h, package), l => l.Kind == LedgerKind.PlatformFee);
    }

    [Theory]
    [InlineData(PayoutStatus.Sent, true)]
    [InlineData(PayoutStatus.Pending, false)] // the provider may already hold the request: not paid yet, but not ours to take back
    [InlineData(PayoutStatus.Failed, false)]
    public async Task A_package_that_was_paid_out_or_is_being_paid_is_left_alone(PayoutStatus status, bool withLedgerEntry)
    {
        await using var h = Reopen.Host();
        var (package, _) = await Reopen.Ordered(h, Me, Slug, Offer);
        await Reopen.Complete(h);
        var before = await Life.Row(h, package);
        await h.Seed(db =>
        {
            db.Payouts.Add(new Payout { Id = Guid.NewGuid(), AccessId = package, AgentId = h.Agent, AmountMinor = before.PayoutMinor!.Value, Status = status, ProviderRef = "demo-ref", CreatedAt = h.Clock.GetUtcNow(), SentAt = h.Clock.GetUtcNow() });
            if (withLedgerEntry) db.LedgerEntries.Add(new LedgerEntry { Id = Guid.NewGuid(), AccessId = package, Kind = LedgerKind.PractitionerPayout, AmountMinor = before.PayoutMinor!.Value, CreatedAt = h.Clock.GetUtcNow() });
        });
        await Reopen.FailAll(h, Me);

        var b = await Life.Row(h, package);
        Assert.Equal((AccessStatus.Completed, 0L), (b.Status, b.UnitsUsed));
        Assert.Equal((before.FeeMinor, before.PayoutMinor, before.PayoutDueAt), (b.FeeMinor, b.PayoutMinor, b.PayoutDueAt));
        Assert.Single(await Reopen.Ledger(h, package), l => l.Kind == LedgerKind.PlatformFee);
        Assert.Equal(withLedgerEntry ? 1 : 0, (await Reopen.Ledger(h, package)).Count(l => l.Kind == LedgerKind.PractitionerPayout));
    }

    [Theory]
    [InlineData(AccessStatus.Disputed)]
    [InlineData(AccessStatus.Resolved)]
    [InlineData(AccessStatus.CancelledByPractitioner)]
    public async Task A_package_in_a_claim_or_cancelled_keeps_its_status_and_gets_its_unit_back(AccessStatus status)
    {
        await using var h = Reopen.Host();
        var package = await AgentApiFx.Package(h, Me, Slug, PackageKind.Tasks, total: 1, used: 1, status: status, offering: Offer);
        var task = Guid.NewGuid();
        await h.Seed(db => db.AgentTasks.Add(new AgentTask { Id = task, AccessId = package, ClientUserId = Me, PractitionerSlug = Slug, PackageId = Offer, Status = AgentTaskStatus.Queued, InputText = Reopen.Input, CreatedAt = h.Clock.GetUtcNow() }));
        await Reopen.FailAll(h, Me);
        var b = await Life.Row(h, package);
        Assert.Equal((status, 0L), (b.Status, b.UnitsUsed));
    }

    [Fact]
    public async Task A_dispute_opened_after_the_completion_keeps_the_package_disputed_when_the_task_fails_later()
    {
        await using var h = Reopen.Host();
        var (package, _) = await Reopen.Ordered(h, Me, Slug, Offer);
        await Reopen.Complete(h);
        var dispute = await DisputeApiTests.Dispute(h, package, Me, new { kind = "not_as_described", text = "demo-dispute-text: the agent did not do what the offer said" });
        Assert.Equal(HttpStatusCode.OK, dispute.StatusCode);
        await Reopen.FailAll(h, Me);
        var b = await Life.Row(h, package);
        Assert.Equal((AccessStatus.Disputed, 0L, (long?)null), (b.Status, b.UnitsUsed, b.FeeMinor));
    }

    // ---- the race with AutoCompleteJob (InMemory: both go through the shared gate; PostgreSQL: PackageReopeningIntegrationTests)

    [Fact]
    public async Task The_completion_job_and_the_failure_of_the_last_task_at_once_always_end_with_an_open_package_and_the_unit_back()
    {
        await using var h = Reopen.Host();
        for (var round = 0; round < 40; round++)
        {
            var client = AgentApiFx.NewUser();
            var package = await AgentApiFx.Package(h, client, Slug, PackageKind.Tasks, total: 1, used: 1, offering: Offer);
            var now = h.Clock.GetUtcNow();
            // a task on its last start that has already timed out: the next run counts it as failed (Attempts = 3, Running for more than the timeout)
            await h.Seed(db => db.AgentTasks.Add(new AgentTask
            {
                Id = Guid.NewGuid(), AccessId = package, ClientUserId = client, PractitionerSlug = Slug, PackageId = Offer, Status = AgentTaskStatus.Running, InputText = Reopen.Input,
                Attempts = 3, CreatedAt = now.AddHours(-2), StartedAt = now.AddHours(-1),
            }));

            await Task.WhenAll(Task.Run(() => Reopen.Complete(h)), Task.Run(() => TaskFx.Job(h, client).RunOnceAsync(now, default)));

            var b = await Life.Row(h, package);
            Assert.True((b.Status, b.UnitsUsed) == (AccessStatus.Confirmed, 0L), $"round {round}: {b.Status}, used {b.UnitsUsed}");
            Assert.Null(b.FeeMinor);
            Assert.DoesNotContain(await Reopen.Ledger(h, package), l => l.Kind == LedgerKind.PlatformFee);
        }
    }
}
