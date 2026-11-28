using System.Text.Json.Serialization;
using Gabinety.Booking.Features.Internal.PractitionerCancellation;
using Gabinety.Booking.Persistence;
using Microsoft.EntityFrameworkCore;
using AccessStatus = Gabinety.Billing.AccessStatus;

namespace Gabinety.Booking.Jobs.StatsPush;

/// <summary>The four values Directory keeps per practitioner (CONTRACTS 3.6).</summary>
public sealed record AccessStats(int CompletedCount, [property: JsonPropertyName("practitionerCancelCount")] int AgentCancelCount, int NoShowUpheldCount, int OpenSlots14d);

/// <summary>
/// Computes <see cref="AccessStats"/> for every practitioner that has bookings.
/// <para>Counting rule (CONTRACTS 8.1, the "stats" column is the authority):</para>
/// <list type="bullet">
/// <item><c>completed_count</c> = rows in <c>Completed</c> + rows in <c>Resolved</c> with <c>dispute_resolution = 'release'</c> ("as Completed").
/// <c>ClientNoShow</c> and <c>LateCancelledByClient</c> count as completed "for money" only, and <c>Resolved/refund_partial</c> has no stats line: none of them is counted.</item>
/// <item><c>practitioner_cancel_count</c> = rows in <c>CancelledByPractitioner</c>, except those cancelled by the suspension or erasure cascade (<c>status_reason = practitioner_suspended</c>):
/// an administrator's action must not lower the reliability of a practitioner who is reinstated (ADR-0012, a deviation from the CONTRACTS 8.1 text).</item>
/// <item><c>no_show_upheld_count</c> = <c>Resolved</c> with <c>refund_full</c> and <c>dispute_kind = 'practitioner_no_show'</c>.</item>
/// <item><c>open_slots_14d</c> = the constant <c>Access:ReportedOpenSlots</c> (default 10, the ranking's "full availability"): an agent is always available (A-03).
/// Booking does not reference the ranking rules, hence the setting.</item>
/// </list>
/// </summary>
public sealed class StatsCalculator(AccessDbContext db, IConfiguration config)
{
    public const int DefaultReportedOpenSlots = 10;

    public async Task<Dictionary<Guid, AccessStats>> ComputeAsync(CancellationToken ct)
    {
        var groups = await db.Accesses.AsNoTracking()
            .GroupBy(b => new { b.AgentId, b.Status, b.DisputeResolution, b.DisputeKind, Cascade = b.StatusReason == FutureAccessCanceller.Reason })
            .Select(g => new { g.Key.AgentId, g.Key.Status, g.Key.DisputeResolution, g.Key.DisputeKind, g.Key.Cascade, N = g.Count(), Shortest = g.Min(b => (int)b.DurationMinutes) })
            .ToListAsync(ct);

        var openSlots = config.GetValue("Access:ReportedOpenSlots", DefaultReportedOpenSlots);
        var result = new Dictionary<Guid, AccessStats>();
        foreach (var id in groups.Select(g => g.AgentId).Distinct())
        {
            ct.ThrowIfCancellationRequested();
            var mine = groups.Where(g => g.AgentId == id).ToList();
            var completed = mine.Where(g => g.Status == AccessStatus.Completed || (g.Status == AccessStatus.Resolved && g.DisputeResolution == "release")).Sum(g => g.N);
            var cancelled = mine.Where(g => g.Status == AccessStatus.CancelledByPractitioner && !g.Cascade).Sum(g => g.N);
            var upheld = mine.Where(g => g.Status == AccessStatus.Resolved && g.DisputeResolution == "refund_full" && g.DisputeKind == "practitioner_no_show").Sum(g => g.N);

            result[id] = new AccessStats(completed, cancelled, upheld, openSlots);
        }
        return result;
    }
}
