using Gabinety.Billing;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Directory;
using Microsoft.EntityFrameworkCore;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Booking;

namespace Gabinety.Booking.Features.AgentApi;

/// <summary>
/// What a client may still use (docs/agentic/PLAN.md B-02). A package is usable when it is <c>Confirmed</c>, not past <c>EndUtc</c> and not used up.
/// Consumption is one SQL statement (<c>UnitsUsed = UnitsUsed + n</c>) so parallel requests never overwrite each other; the in-memory provider (tests) has no such
/// statement, so it changes the entity under one semaphore. Numbers only: nothing a user wrote goes through here.
/// </summary>
public class EntitlementStore(BookingDbContext db, TimeProvider time)
{
    static readonly SemaphoreSlim InMemoryGate = new(1, 1);

    /// <summary>The oldest usable package of <paramref name="clientUserId"/> for the agent, of the kind, optionally of one offering; null when there is none.</summary>
    public virtual Task<BookingEntity?> FindAsync(string clientUserId, string practitionerSlug, OfferingKind kind, Guid? offeringId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        return Candidates(clientUserId, practitionerSlug, kind, offeringId)
            .Where(b => b.Status == BookingStatus.Confirmed && b.EndUtc > now && b.UnitsUsed < b.UnitsTotal)
            .OrderBy(b => b.CreatedAt).ThenBy(b => b.Id)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// True when the client has a package for the agent that has not expired but is used up (still <c>Confirmed</c>, or already <c>Completed</c> by the job because it ran out).
    /// It tells "no package" (404) from "package used up" (429).
    /// </summary>
    public virtual async Task<bool> HasExhaustedAsync(string clientUserId, string practitionerSlug, OfferingKind kind, Guid? offeringId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        return await Candidates(clientUserId, practitionerSlug, kind, offeringId)
            .AnyAsync(b => (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Completed) && b.EndUtc > now && b.UnitsUsed >= b.UnitsTotal, ct);
    }

    /// <summary>The agents (slugs) the client has a usable package of this kind for, sorted.</summary>
    public virtual async Task<List<string>> UsableSlugsAsync(string clientUserId, OfferingKind kind, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        return await db.Bookings.AsNoTracking()
            .Where(b => b.ClientUserId == clientUserId && b.Kind == kind && b.Status == BookingStatus.Confirmed && b.EndUtc > now && b.UnitsUsed < b.UnitsTotal && b.PractitionerSlug != "")
            .Select(b => b.PractitionerSlug).Distinct().OrderBy(s => s).ToListAsync(ct);
    }

    /// <summary>Adds <paramref name="units"/> to <c>UnitsUsed</c> atomically. May go past <c>UnitsTotal</c>: chat is settled after the fact.</summary>
    public virtual async Task ConsumeAsync(Guid bookingId, long units, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(units);
        if (db.Database.IsRelational())
        {
            await db.Bookings.Where(b => b.Id == bookingId).ExecuteUpdateAsync(s => s.SetProperty(b => b.UnitsUsed, b => b.UnitsUsed + units), ct);
            return;
        }
        await ChangeInMemoryAsync(bookingId, used => used + units, ct);
    }

    /// <summary>Takes <paramref name="units"/> back (a task that failed). <c>UnitsUsed</c> never goes below 0.</summary>
    public virtual async Task RefundUnitsAsync(Guid bookingId, long units, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(units);
        if (db.Database.IsRelational())
        {
            await db.Bookings.Where(b => b.Id == bookingId)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.UnitsUsed, b => b.UnitsUsed >= units ? b.UnitsUsed - units : 0L), ct);
            return;
        }
        await ChangeInMemoryAsync(bookingId, used => Math.Max(0, used - units), ct);
    }

    IQueryable<BookingEntity> Candidates(string clientUserId, string practitionerSlug, OfferingKind kind, Guid? offeringId)
    {
        var q = db.Bookings.AsNoTracking().Where(b => b.ClientUserId == clientUserId && b.PractitionerSlug == practitionerSlug && b.Kind == kind);
        return offeringId is { } o ? q.Where(b => b.OfferingId == o) : q;
    }

    async Task ChangeInMemoryAsync(Guid bookingId, Func<long, long> change, CancellationToken ct)
    {
        await InMemoryGate.WaitAsync(ct);
        try
        {
            var row = await db.Bookings.SingleOrDefaultAsync(b => b.Id == bookingId, ct);
            if (row is null) return;
            row.UnitsUsed = change(row.UnitsUsed);
            await db.SaveChangesAsync(ct);
        }
        finally { InMemoryGate.Release(); }
    }
}

/// <summary>Registers the pieces of the public agent API that are not an endpoint (docs/agentic/PLAN.md B-02).</summary>
public sealed class AgentApiFeature : Gabinety.ServiceDefaults.IServiceFeature
{
    public void Add(IHostApplicationBuilder builder) => builder.Services.AddScoped<EntitlementStore>();
    public void Map(IEndpointRouteBuilder endpoints) { }
}
