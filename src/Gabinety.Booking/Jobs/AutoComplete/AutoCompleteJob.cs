using Gabinety.Billing;
using Gabinety.Booking.Features.Lifecycle.Core;
using Gabinety.Booking.Jobs.Base;
using Gabinety.Booking.Persistence;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using AccessEntity = Gabinety.Booking.Features.Bookings.Entities.Access;
using AccessStatus = Gabinety.Billing.AccessStatus;

namespace Gabinety.Booking.Jobs.AutoComplete;

/// <summary>
/// Completes the packages nobody disputed (A-03: <c>Confirmed</c> to <c>Completed</c> once <c>end_utc</c> has passed or the units are used up, see <see cref="IsDue"/>).
/// The effects: <c>ended_at</c> (the earlier of <c>end_utc</c> and now) and <c>payout_due_at = max(end_utc + 48 h, start_utc + 72 h)</c>; the fee hook (T-04.08c) computes
/// <c>fee_minor</c>/<c>payout_minor</c> in the same transaction. Each booking is its own transaction through <see cref="AccessTransitions"/> (trigger
/// <see cref="AccessTrigger.CompletionWindowElapsed"/>), which re-checks status and time under the row lock: a booking disputed, completed or cancelled meanwhile
/// (by a client, another run or another instance) is no longer <c>Confirmed</c>, so it is a no-op, not an error. A disputed booking therefore can never be completed.
/// Each cycle takes the due bookings oldest first in batches (<c>Jobs:AutoComplete:BatchSize</c>, 50) until none are left or <c>Jobs:AutoComplete:MaxPerRun</c> (500) were handled.
/// Idempotent, and safe in two processes at once.
/// </summary>
public class AutoCompleteJob(IServiceScopeFactory scopes, TimeProvider time, IConfiguration config, JobMetrics metrics, ILogger<AutoCompleteJob> log)
    : PeriodicJob(time, config, metrics, log)
{
    public const string JobName = "auto_complete";
    public const string WindowKey = "Booking:CompleteAfterHours";
    public const int DefaultWindowHours = 48, DefaultBatch = 50, DefaultMaxPerRun = 500;

    /// <summary>A package pays out at <c>end_utc + 72 h</c>: the claim window of a package closes then (the dispute window of CONTRACTS 4.2, measured from the end), and money never moves before it has closed (owner decision, A-03).</summary>
    public static readonly TimeSpan PayoutDelay = TimeSpan.FromHours(72);

    /// <summary><c>payout_due_at</c> of a completed package: <c>end + 72 h</c>. <paramref name="startUtc"/> stays in the signature for the callers; it no longer matters.</summary>
    public static DateTimeOffset PayoutDueAt(DateTimeOffset startUtc, DateTimeOffset endUtc) => endUtc + PayoutDelay;

    public override string Name => JobName;

    public override async Task RunOnceAsync(DateTimeOffset now, CancellationToken ct) => await CompleteDueAsync(now, ct);

    /// <summary>
    /// A package is done when its validity ended or its units are used up (A-03). <c>UnitsTotal &gt; 0</c> guards the rows that predate the units (they hold 0 of 0) and must not complete at once.
    /// The 48 h window <see cref="WindowKey"/> no longer applies to this condition.
    /// </summary>
    public static bool IsDue(AccessEntity b, DateTimeOffset now) => b.EndUtc <= now || (b.UnitsTotal > 0 && b.UnitsUsed >= b.UnitsTotal);

    /// <summary>Completes every due booking up to the per-run cap and returns how many this call completed. Failures are counted, every other booking is still handled, then <see cref="JobFailedException"/> is thrown. A booking that fails is not tried again within the run.</summary>
    public async Task<int> CompleteDueAsync(DateTimeOffset now, CancellationToken ct)
    {
        var batch = ReadPositiveInt(Configuration, "Jobs:AutoComplete:BatchSize", DefaultBatch);
        var cap = ReadPositiveInt(Configuration, "Jobs:AutoComplete:MaxPerRun", DefaultMaxPerRun);
        List<Guid> seen = [];
        int applied = 0, failed = 0;
        while (seen.Count < cap)
        {
            var ids = await SelectDueAsync(now, seen, Math.Min(batch, cap - seen.Count), ct);
            if (ids.Count == 0) break;
            seen.AddRange(ids);
            var (a, f) = await ProcessAsync(ids, now, ct);
            applied += a;
            failed += f;
        }
        if (applied > 0) log.LogInformation("AutoComplete completed {Count} booking(s)", applied);
        if (failed > 0) throw new JobFailedException(failed);
        return applied;
    }

    /// <summary>Completes the given bookings (a list selected earlier may be stale) and returns how many were completed; the others are left as they are.</summary>
    public async Task<int> CompleteAsync(IReadOnlyCollection<Guid> ids, DateTimeOffset now, CancellationToken ct)
    {
        var (applied, failed) = await ProcessAsync(ids, now, ct);
        if (failed > 0) throw new JobFailedException(failed);
        return applied;
    }

    /// <summary>Narrows what the job looks at. The default is everything; a test sharing a database with other tests restricts it to its own rows.</summary>
    protected virtual IQueryable<AccessEntity> Scope(IQueryable<AccessEntity> accesses) => accesses;

    async Task<List<Guid>> SelectDueAsync(DateTimeOffset now, List<Guid> exclude, int take, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AccessDbContext>();
        var due = Scope(db.Accesses.AsNoTracking()).Where(b => b.Status == AccessStatus.Confirmed && (b.EndUtc <= now || (b.UnitsTotal > 0 && b.UnitsUsed >= b.UnitsTotal)));
        if (exclude.Count > 0) due = due.Where(b => !exclude.Contains(b.Id));
        return await due.OrderBy(b => b.EndUtc).ThenBy(b => b.Id).Select(b => b.Id).Take(take).ToListAsync(ct);
    }

    async Task<(int Applied, int Failed)> ProcessAsync(IReadOnlyCollection<Guid> ids, DateTimeOffset now, CancellationToken ct)
    {
        int applied = 0, failed = 0;
        foreach (var id in ids)
        {
            try
            {
                using var scope = scopes.CreateScope(); // one context, one transaction per booking
                var transitions = scope.ServiceProvider.GetRequiredService<AccessTransitions>();
                var result = await transitions.ApplyAsync(new TransitionRequest(id, AccessTrigger.CompletionWindowElapsed, now)
                {
                    Precondition = b => IsDue(b, now),
                    Effects = c =>
                    {
                        c.Access.EndedAt = c.Access.EndUtc < now ? c.Access.EndUtc : now; // when it really ended: the validity, or the moment the last unit went
                        c.Access.PayoutDueAt = PayoutDueAt(c.Access.StartUtc, c.Access.EndUtc);
                    },
                }, ct);
                if (result.Applied) applied++; // Invalid (disputed, cancelled or completed meanwhile; not due) and NotFound are no-ops
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                failed++;
                log.LogError("AutoComplete could not complete booking {BookingId}: {ExceptionType}", id, ex.GetType().Name);
            }
        }
        return (applied, failed);
    }
}

/// <summary>Registers the auto-complete job as a singleton (a test resolves it) and as the hosted service that runs it. <c>Jobs:Enabled=false</c> stops the loop from starting.</summary>
public sealed class AutoCompleteFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<AutoCompleteJob>();
        builder.Services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<AutoCompleteJob>());
    }

    public void Map(IEndpointRouteBuilder endpoints) { }
}
