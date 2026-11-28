using Gabinety.Billing;
using Gabinety.Booking.Features.Accesses.Create;
using Gabinety.Booking.Features.Lifecycle.Core;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Booking;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using AccessEntity = Gabinety.Booking.Features.Accesses.Entities.Access;
using AccessStatus = Gabinety.Billing.AccessStatus;

namespace Gabinety.Booking.Features.Payouts;

/// <summary>
/// When a transition leaves a booking money-complete (CONTRACTS 8.1, 9.2) sets <c>fee_minor</c> and <c>payout_minor</c> (CONTRACTS 9.1) and writes the
/// <c>PlatformFee</c> ledger row. Runs inside the transition's transaction. It creates no payout row: the payout job does (T-04.08d).
/// </summary>
public sealed class FeeHook(IConfiguration config, PlatformFeeClaims claims) : IAccessTransitionHook
{
    public const string RateKey = "Billing:FirstConsultationFeeBps";
    public const int DefaultRateBps = 1500;

    /// <summary>The part of the price the platform charges on, or null when this status carries no fee (full refund, not money-complete).</summary>
    public static Money? Kept(AccessStatus status, string? resolution, Money price, long refundedMinor) => status switch
    {
        AccessStatus.Completed or AccessStatus.ClientNoShow or AccessStatus.LateCancelledByClient => price,
        AccessStatus.Resolved when resolution == "release" => price,
        AccessStatus.Resolved when resolution == "refund_partial" => price.Minus(Money.Pln(refundedMinor)),
        _ => null,
    };

    public async Task OnTransitionAsync(AccessTransitionContext context, CancellationToken ct)
    {
        var b = context.Access;
        if (context.From == AccessStatus.Completed && context.To == AccessStatus.Disputed)
        {
            claims.Release(PairKey.For(b.ClientUserId, b.AgentId)); // the dispute reopened the money: its unpaid PlatformFee row was removed (DisputeFeature), so the InMemory stand-in lets go too
            return;
        }
        if (context.From == AccessStatus.Completed && context.To == AccessStatus.Confirmed)
        {
            // a refunded unit reopened the package (PackageReopening): its unpaid PlatformFee row was removed in this transaction. The InMemory stand-in lets the pair go only when this booking held it.
            if (context.Db.ChangeTracker.Entries<LedgerEntry>().Any(e => e.State == EntityState.Deleted && e.Entity.AccessId == b.Id && e.Entity.Kind == LedgerKind.PlatformFee))
                claims.Release(PairKey.For(b.ClientUserId, b.AgentId));
            return;
        }
        if (b.FeeMinor is not null) return; // computed once
        var price = Money.Pln(b.PriceMinor);
        // refund_partial: `kept` reads payment.RefundedMinor, so the refunded amount must be set BEFORE the hooks run (T-04.07c's effects do this for a
        // provider that confirms at once, the Mock). For an asynchronous provider the fee is to be settled when the refund is confirmed (T-04.07c brief).
        var kept = Kept(context.To, b.DisputeResolution, price, context.Payment?.RefundedMinor ?? 0);
        if (kept is null) return;

        var rate = config.GetValue(RateKey, DefaultRateBps);
        var result = Fees.Calculate(kept.Value, rate, pairAlreadyCharged: false);
        // The ledger amount is always positive (CONTRACTS 4.1), so a fee of 0 writes no row and does not use up the pair.
        if (result.Fee.Minor > 0 && !await ClaimPairAsync(context, b, result.Fee.Minor, ct))
            result = Fees.Calculate(kept.Value, rate, pairAlreadyCharged: true);

        b.FeeMinor = result.Fee.Minor;
        b.PayoutMinor = result.Payout.Minor;
    }

    /// <summary>Writes the PlatformFee row for the pair; false when the pair already has one. The unique partial index decides a race.</summary>
    async Task<bool> ClaimPairAsync(AccessTransitionContext context, AccessEntity b, long feeMinor, CancellationToken ct)
    {
        var pairKey = PairKey.For(b.ClientUserId, b.AgentId); // spec format when it fits varchar(80), hashed otherwise
        var id = Guid.CreateVersion7();
        if (context.Db.Database.IsNpgsql())
        {
            // ON CONFLICT DO NOTHING: the loser's insert does not abort the transaction; zero affected rows means the pair is taken.
            var rows = await context.Db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO ledger_entries (id, booking_id, kind, amount_minor, pair_key, created_at)
                VALUES ({id}, {b.Id}, {nameof(LedgerKind.PlatformFee)}, {feeMinor}, {pairKey}, {context.Now})
                ON CONFLICT (pair_key) WHERE kind = 'PlatformFee' DO NOTHING
                """, ct);
            return rows == 1;
        }

        // InMemory has no unique index: emulate it under a process lock (the database is shared by name across hosts in one process).
        if (!await claims.TryClaimAsync(context.Db, pairKey, ct)) return false;
        context.Db.LedgerEntries.Add(new LedgerEntry
        {
            Id = id, AccessId = b.Id, Kind = LedgerKind.PlatformFee, AmountMinor = feeMinor, PairKey = pairKey, CreatedAt = context.Now,
        });
        return true;
    }
}

/// <summary>
/// Takes a package's money decision back, in the open transition's transaction: clears <c>fee_minor</c>, <c>payout_minor</c>, <c>payout_due_at</c> and removes the UNPAID <c>PlatformFee</c> ledger row, which
/// frees the pair for the booking that really earns the fee. Used where a completed package goes back to being open: a dispute (<c>DisputeFeature</c>, the money is decided again at the resolution) and a
/// refunded unit that reopens a package completed by exhaustion (<c>PackageReopening</c>). The one deliberate delete of a ledger row; the caller guarantees nothing was paid out (no payout row, no payout entry).
/// </summary>
public static class MoneyReopening
{
    public static void Reopen(AccessTransitionContext c)
    {
        c.Access.FeeMinor = null;
        c.Access.PayoutMinor = null;
        c.Access.PayoutDueAt = null;
        c.Db.LedgerEntries.RemoveRange(c.Db.LedgerEntries.Where(l => l.AccessId == c.Access.Id && l.Kind == LedgerKind.PlatformFee).ToList());
    }
}

/// <summary>InMemory stand-in for the unique partial index: pairs claimed in this process plus the PlatformFee rows already saved.</summary>
public sealed class PlatformFeeClaims
{
    readonly object gate = new();
    readonly HashSet<string> claimed = [];

    public void Release(string pairKey) { lock (gate) claimed.Remove(pairKey); }

    public async Task<bool> TryClaimAsync(AccessDbContext db, string pairKey, CancellationToken ct)
    {
        var saved = await db.LedgerEntries.AsNoTracking().AnyAsync(l => l.Kind == LedgerKind.PlatformFee && l.PairKey == pairKey, ct);
        lock (gate) return !saved && claimed.Add(pairKey);
    }
}

public sealed class PayoutsFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<PlatformFeeClaims>();
        builder.Services.AddScoped<IAccessTransitionHook, FeeHook>();
    }

    public void Map(IEndpointRouteBuilder endpoints) { }
}
