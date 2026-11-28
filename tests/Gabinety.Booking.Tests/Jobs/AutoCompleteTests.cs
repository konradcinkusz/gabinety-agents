using Gabinety.Booking.Features.Bookings.Create;
using Gabinety.Contracts.Booking;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Jobs.AutoComplete;
using Gabinety.Booking.Jobs.Base;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using AccessStatus = Gabinety.Billing.AccessStatus;

namespace Gabinety.Booking.Tests;

public class AutoCompleteTests : IDisposable
{
    readonly CreateHost h = Auto.Host();
    public void Dispose() => h.Dispose();

    [Fact]
    public async Task A_confirmed_package_completes_exactly_at_its_end_and_not_one_second_before()
    {
        var id = await Auto.Seed(h, -TimeSpan.FromSeconds(1)); // ends in one second
        var job = Auto.Job(h);

        Assert.Equal(0, await job.CompleteDueAsync(h.Clock.GetUtcNow(), default));
        Assert.Equal(AccessStatus.Confirmed, await Hold.StatusOf(h, id));

        h.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, await job.CompleteDueAsync(h.Clock.GetUtcNow(), default));
        Assert.Equal(AccessStatus.Completed, await Hold.StatusOf(h, id));
    }

    [Fact]
    public async Task Completion_sets_ended_at_and_payout_due_at_and_the_fee_hook_computes_the_amounts_in_the_same_transaction()
    {
        var id = await Auto.Seed(h, TimeSpan.Zero); // ends exactly now

        Assert.Equal(1, await Auto.Job(h).CompleteDueAsync(h.Clock.GetUtcNow(), default));

        var b = await Life.Row(h, id);
        Assert.Equal(AccessStatus.Completed, b.Status);
        Assert.Equal(b.EndUtc, b.EndedAt);
        Assert.Equal(b.EndUtc + TimeSpan.FromHours(72), b.PayoutDueAt); // end + 72 h: the dispute window of a package has closed (owner decision, A-03)
        Assert.Equal(1500, b.FeeMinor);    // 15 % of 10000, first consultation of the pair
        Assert.Equal(8500, b.PayoutMinor);
        var fee = Assert.Single(await Life.Ledger(h, id), l => l.Kind == LedgerKind.PlatformFee);
        Assert.Equal(1500, fee.AmountMinor);
    }

    [Fact]
    public async Task The_old_48_hour_window_and_its_setting_no_longer_apply()
    {
        using var h48 = Auto.Host(("Booking:CompleteAfterHours", "48"));
        var id = await Auto.Seed(h48, TimeSpan.FromHours(1)); // ended an hour ago: used to wait for end + 48 h
        Assert.Equal(1, await Auto.Job(h48).CompleteDueAsync(h48.Clock.GetUtcNow(), default));
        Assert.Equal(AccessStatus.Completed, await Hold.StatusOf(h48, id));
    }

    [Fact]
    public async Task A_package_whose_units_are_used_up_completes_before_its_end()
    {
        var used = await Auto.Seed(h, -TimeSpan.FromDays(20), unitsUsed: 5, unitsTotal: 5);
        var partly = await Auto.Seed(h, -TimeSpan.FromDays(20), unitsUsed: 4, unitsTotal: 5);
        var noUnits = await Auto.Seed(h, -TimeSpan.FromDays(20), unitsUsed: 0, unitsTotal: 0); // a row that predates the units: 0 of 0 is not "used up"

        Assert.Equal(1, await Auto.Job(h).CompleteDueAsync(h.Clock.GetUtcNow(), default));

        Assert.Equal(AccessStatus.Completed, await Hold.StatusOf(h, used));
        Assert.Equal(AccessStatus.Confirmed, await Hold.StatusOf(h, partly));
        Assert.Equal(AccessStatus.Confirmed, await Hold.StatusOf(h, noUnits));
        var row = await Life.Row(h, used);
        Assert.Equal(h.Clock.GetUtcNow(), row.EndedAt);                       // it ended now, not at the end of its validity
        Assert.Equal(row.EndUtc + TimeSpan.FromHours(72), row.PayoutDueAt); // end + 72 h of the validity, also when the units ran out earlier
    }

    [Fact]
    public async Task A_disputed_access_is_never_completed_even_when_its_id_comes_from_a_stale_selection()
    {
        var disputed = await Auto.Seed(h, Auto.Window + TimeSpan.FromHours(5), AccessStatus.Disputed);
        var job = Auto.Job(h);

        Assert.Equal(0, await job.CompleteDueAsync(h.Clock.GetUtcNow(), default));
        Assert.Equal(0, await job.CompleteAsync([disputed], h.Clock.GetUtcNow(), default)); // selected while Confirmed, disputed meanwhile
        Assert.Equal(AccessStatus.Disputed, await Hold.StatusOf(h, disputed));
        Assert.Null((await Life.Row(h, disputed)).FeeMinor);
    }

    [Fact]
    public async Task A_stale_id_that_is_not_due_and_an_unknown_id_are_no_ops()
    {
        var early = await Auto.Seed(h, -TimeSpan.FromHours(10)); // not due: it ends in ten hours
        Assert.Equal(0, await Auto.Job(h).CompleteAsync([early, Guid.NewGuid()], h.Clock.GetUtcNow(), default));
        Assert.Equal(AccessStatus.Confirmed, await Hold.StatusOf(h, early));
    }

    [Fact]
    public async Task Other_statuses_are_left_alone()
    {
        var ids = new List<Guid>();
        foreach (var s in new[] { AccessStatus.CancelledByClient, AccessStatus.ClientNoShow, AccessStatus.Completed, AccessStatus.Resolved })
            ids.Add(await Auto.Seed(h, Auto.Window + TimeSpan.FromDays(1), s));
        var before = await Hold.Snapshot(h);
        Assert.Equal(0, await Auto.Job(h).CompleteDueAsync(h.Clock.GetUtcNow(), default));
        Assert.Equal(before, await Hold.Snapshot(h));
    }

    [Fact]
    public async Task Running_twice_changes_nothing()
    {
        await Auto.Seed(h, Auto.Window + TimeSpan.FromHours(1));
        await Auto.Seed(h, -TimeSpan.FromHours(3)); // not due
        var job = Auto.Job(h);

        Assert.Equal(1, await job.CompleteDueAsync(h.Clock.GetUtcNow(), default));
        var afterFirst = await Hold.Snapshot(h);
        Assert.Equal(0, await job.CompleteDueAsync(h.Clock.GetUtcNow(), default));
        Assert.Equal(afterFirst, await Hold.Snapshot(h));
    }

    [Fact]
    public async Task A_per_run_cap_leaves_the_rest_for_the_next_run()
    {
        using var capped = Auto.Host(("Jobs:AutoComplete:BatchSize", "2"), ("Jobs:AutoComplete:MaxPerRun", "3"));
        for (var i = 0; i < 5; i++) await Auto.Seed(capped, Auto.Window + TimeSpan.FromHours(i));
        var job = Auto.Job(capped);
        Assert.Equal(3, await job.CompleteDueAsync(capped.Clock.GetUtcNow(), default));
        Assert.Equal(2, await job.CompleteDueAsync(capped.Clock.GetUtcNow(), default));
    }

    [Fact]
    public async Task A_failing_row_does_not_block_the_others_and_the_run_reports_it()
    {
        var failing = new HashSet<Guid>();
        using var fh = Hold.Host(failing);
        var a = await Auto.Seed(fh, Auto.Window + TimeSpan.FromHours(3));
        var bad = await Auto.Seed(fh, Auto.Window + TimeSpan.FromHours(2));
        var c = await Auto.Seed(fh, Auto.Window + TimeSpan.FromHours(1));
        failing.Add(bad);

        await Assert.ThrowsAsync<JobFailedException>(() => Auto.Job(fh).RunOnceAsync(fh.Clock.GetUtcNow(), default));

        Assert.Equal(AccessStatus.Completed, await Hold.StatusOf(fh, a));
        Assert.Equal(AccessStatus.Confirmed, await Hold.StatusOf(fh, bad));
        Assert.Equal(AccessStatus.Completed, await Hold.StatusOf(fh, c));
        var all = string.Join("\n", fh.Logs.Lines.Where(l => !l.StartsWith("Microsoft")));
        Assert.Contains(bad.ToString(), all);
        foreach (var forbidden in new[] { "demo-brief", "boom", Life.Client, fh.OwnerUserId, fh.Agent.ToString() }) Assert.DoesNotContain(forbidden, all);
    }

    [Fact]
    public async Task The_cycle_records_the_job_run_with_the_auto_complete_label()
    {
        using var capture = new MetricCapture(h.Services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        await Auto.Seed(h, Auto.Window);
        Assert.True(await Auto.Job(h).RunCycleAsync(h.Clock.GetUtcNow(), default));
        Assert.Equal(1, capture.Runs("auto_complete", "ok"));
    }

    [Fact]
    public void The_job_is_registered_once_as_a_singleton_and_as_a_hosted_service_by_its_own_feature()
    {
        var job = h.Services.GetRequiredService<AutoCompleteJob>();
        Assert.Same(job, h.Services.GetRequiredService<AutoCompleteJob>());
        Assert.Single(h.Services.GetServices<IHostedService>(), s => ReferenceEquals(s, job));
        Assert.Equal("auto_complete", job.Name);
    }
}
