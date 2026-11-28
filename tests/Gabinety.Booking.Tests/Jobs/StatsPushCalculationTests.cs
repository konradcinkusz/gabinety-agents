using System.Net.Http.Json;
using System.Text.Json;
using Gabinety.Booking.Jobs.StatsPush;
using AccessStatus = Gabinety.Billing.AccessStatus;

namespace Gabinety.Booking.Tests;

public class StatsPushCalculationTests : IDisposable
{
    readonly CreateHost h = Life.Host(new RecordingProvider());
    public void Dispose() => h.Dispose();

    // Rule written down (CONTRACTS 8.1 says "stats completed_count + 1" only for Completed, and "as Completed" for Resolved/release):
    // completed = Completed + Resolved/release. ClientNoShow and LateCancelledByClient count as completed "for money" only; refund_partial has no stats line.
    [Theory]
    [InlineData(AccessStatus.PendingPayment, 0, 0)]
    [InlineData(AccessStatus.Confirmed, 0, 0)]
    [InlineData(AccessStatus.CancelledByClient, 0, 0)]
    [InlineData(AccessStatus.LateCancelledByClient, 0, 0)]
    [InlineData(AccessStatus.CancelledByPractitioner, 0, 1)]
    [InlineData(AccessStatus.Expired, 0, 0)]
    [InlineData(AccessStatus.Completed, 1, 0)]
    [InlineData(AccessStatus.ClientNoShow, 0, 0)]
    [InlineData(AccessStatus.Disputed, 0, 0)]
    public async Task One_booking_in_each_status_moves_the_counts_as_the_rule_says(AccessStatus status, int completed, int cancelled)
    {
        await StatsFx.Seed(h, status, kind: status == AccessStatus.Disputed ? "practitioner_no_show" : null);
        var s = await StatsFx.StatsOf(h);
        Assert.Equal(completed, s.CompletedCount);
        Assert.Equal(cancelled, s.AgentCancelCount);
        Assert.Equal(0, s.NoShowUpheldCount);
    }

    [Theory]
    [InlineData("release", "not_as_described", 1, 0)]
    [InlineData("release", "practitioner_no_show", 1, 0)]
    [InlineData("refund_full", "not_as_described", 0, 0)]
    [InlineData("refund_full", "practitioner_no_show", 0, 1)]
    [InlineData("refund_partial", "practitioner_no_show", 0, 0)]
    [InlineData("refund_partial", "other", 0, 0)]
    public async Task Resolved_counts_by_resolution_and_kind(string resolution, string kind, int completed, int upheld)
    {
        await StatsFx.Seed(h, AccessStatus.Resolved, resolution, kind);
        var s = await StatsFx.StatsOf(h);
        Assert.Equal(completed, s.CompletedCount);
        Assert.Equal(upheld, s.NoShowUpheldCount);
        Assert.Equal(0, s.AgentCancelCount);
    }

    [Fact]
    public async Task A_cancellation_caused_by_suspension_does_not_count_but_a_voluntary_one_does()
    {
        // ADR-0012: an administrator's action must not lower the reliability of a practitioner who is later reinstated (a deviation from the CONTRACTS 8.1 text)
        var cascade = await StatsFx.Seed(h, AccessStatus.CancelledByPractitioner);
        await h.Seed(db => db.Accesses.Single(b => b.Id == cascade).StatusReason = Gabinety.Booking.Features.Internal.PractitionerCancellation.FutureAccessCanceller.Reason);
        Assert.Equal(0, (await StatsFx.StatsOf(h)).AgentCancelCount);

        var voluntary = await StatsFx.Seed(h, AccessStatus.CancelledByPractitioner);
        await h.Seed(db => db.Accesses.Single(b => b.Id == voluntary).StatusReason = "Choroba, przepraszam");
        await StatsFx.Seed(h, AccessStatus.CancelledByPractitioner); // no reason at all
        Assert.Equal(2, (await StatsFx.StatsOf(h)).AgentCancelCount);
    }

    [Fact]
    public async Task Counts_are_per_practitioner_and_add_up()
    {
        await StatsFx.Seed(h, AccessStatus.Completed);
        await StatsFx.Seed(h, AccessStatus.Completed);
        await StatsFx.Seed(h, AccessStatus.CancelledByPractitioner);
        await StatsFx.Seed(h, AccessStatus.Resolved, "refund_full", "practitioner_no_show");
        using var other = Life.Host(new RecordingProvider());
        await StatsFx.Seed(other, AccessStatus.Completed);
        var s = await StatsFx.StatsOf(h);
        Assert.Equal((2, 1, 1), (s.CompletedCount, s.AgentCancelCount, s.NoShowUpheldCount));
    }

    [Fact]
    public async Task The_set_is_practitioners_with_bookings_only()
    {
        using var withBooking = Life.Host(new RecordingProvider());
        await StatsFx.Seed(withBooking, AccessStatus.Completed);
        using var scope = h.Services.CreateScope();
        var all = await scope.ServiceProvider.GetRequiredService<StatsCalculator>().ComputeAsync(default);
        Assert.DoesNotContain(h.Agent, all.Keys);
        Assert.Contains(withBooking.Agent, all.Keys);
        Assert.DoesNotContain(Guid.NewGuid(), all.Keys);
    }

    // A-03: an agent is always available, so the reported open slots are a constant (Access:ReportedOpenSlots, default 10 = the ranking's "full availability").
    [Fact]
    public async Task Open_slots_are_the_constant_whatever_the_bookings()
    {
        await StatsFx.Seed(h, AccessStatus.Completed);                       
        Assert.Equal(StatsCalculator.DefaultReportedOpenSlots, (await StatsFx.StatsOf(h)).OpenSlots14d);
        Assert.Equal(10, StatsCalculator.DefaultReportedOpenSlots);

        await StatsFx.Seed(h, AccessStatus.Confirmed, startIn: TimeSpan.FromDays(2));
        Assert.Equal(10, (await StatsFx.StatsOf(h)).OpenSlots14d);
    }

    [Fact]
    public async Task The_constant_follows_Access_ReportedOpenSlots()
    {
        using var configured = Life.Host(new RecordingProvider(), x => x.Settings["Access:ReportedOpenSlots"] = "4");
        await StatsFx.Seed(configured, AccessStatus.Completed);
        Assert.Equal(4, (await StatsFx.StatsOf(configured)).OpenSlots14d);
    }
}
