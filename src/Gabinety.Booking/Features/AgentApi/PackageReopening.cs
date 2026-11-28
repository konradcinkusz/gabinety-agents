using Gabinety.Billing;
using Gabinety.Booking.Features.Lifecycle.Core;
using Gabinety.Booking.Features.Payouts;
using Gabinety.Contracts.Booking;
using Gabinety.Contracts.Directory;
using Microsoft.EntityFrameworkCore;
using AccessEntity = Gabinety.Booking.Features.Accesses.Entities.Access;
using AccessStatus = Gabinety.Billing.AccessStatus;

namespace Gabinety.Booking.Features.AgentApi;

/// <summary>
/// ADR-0013, owner decision 2. <c>AutoCompleteJob</c> completes a <c>Tasks</c> package when its units run out. If the last task then ends <c>Failed</c>, its unit goes back (<c>EntitlementStore.RefundUnitsAsync</c>)
/// to a package that is <c>Completed</c> and so cannot be used (<c>POST /v1/tasks</c> looks for <c>Confirmed</c>). This puts such a package back to <c>Confirmed</c>, in ONE transaction under the booking's row lock
/// (<see cref="AccessTransitions"/>, trigger <see cref="AccessTrigger.UnitRefunded"/>, the lock <c>AutoCompleteJob</c> and the payout job also take), and takes the money decision back
/// (<see cref="MoneyReopening"/>: the fee, the payout amount and date are cleared and the unpaid <c>PlatformFee</c> row is removed, exactly as a dispute does).
/// It touches the package ONLY when everything holds, all re-read under the lock:
/// <list type="bullet">
/// <item>it is <c>Completed</c> (so not <c>Disputed</c> or <c>Resolved</c>: a package in a claim keeps its status) and is a <c>Tasks</c> package with a unit free (<c>UnitsUsed &lt; UnitsTotal</c>);</item>
/// <item>its validity has NOT ended (<c>EndUtc &gt; now</c>): then it was completed by exhaustion, not by time, and the client can still use the unit. After <c>EndUtc</c> nothing is reopened;</item>
/// <item>nothing was paid out: no <c>Payout</c> row (of any status: <c>Pending</c> means the provider may already have the money) and no <c>PractitionerPayout</c> ledger entry.</item>
/// </list>
/// Otherwise it is a no-op (false), never an error. Idempotent: a second call finds the package <c>Confirmed</c>. The refund of the unit and this are two committed steps, in that order, so a
/// concurrent <c>AutoCompleteJob</c> either completes first (and this reopens) or sees the free unit and does not complete (<c>IsDue</c> is re-checked under the lock). A crash between the two leaves the
/// package <c>Completed</c> with a free unit, as before this change: the client's claim route is open.
/// </summary>
public static class PackageReopening
{
    /// <summary>The rules that need only the row.</summary>
    public static bool CanReopen(AccessEntity b, DateTimeOffset now) =>
        b.Status == AccessStatus.Completed && b.Kind == PackageKind.Tasks && b.UnitsTotal > 0 && b.UnitsUsed < b.UnitsTotal && b.EndUtc > now
        && b.DisputeOpenedAt is null && b.ResolvedAt is null;

    /// <summary>True when nothing was paid out for the booking: no payout row and no payout ledger entry.</summary>
    public static async Task<bool> NothingPaidAsync(Persistence.AccessDbContext db, Guid accessId, CancellationToken ct) =>
        !await db.Payouts.AnyAsync(p => p.AccessId == accessId, ct)
        && !await db.LedgerEntries.AnyAsync(l => l.AccessId == accessId && l.Kind == LedgerKind.PractitionerPayout, ct);

    public static async Task<bool> TryAsync(AccessTransitions transitions, Guid accessId, DateTimeOffset now, CancellationToken ct)
    {
        var result = await transitions.ApplyAsync(new TransitionRequest(accessId, AccessTrigger.UnitRefunded, now)
        {
            Precondition = b => CanReopen(b, now),
            DbPrecondition = (db, b, token) => NothingPaidAsync(db, b.Id, token),
            Effects = c =>
            {
                c.Access.EndedAt = null; // it did not end
                MoneyReopening.Reopen(c);
            },
        }, ct);
        return result.Applied;
    }
}
