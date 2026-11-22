using BookingStatus = Gabinety.Billing.BookingStatus;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Booking;
using Gabinety.Contracts.Common;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Booking.Features.Internal.Reviewability;

/// <summary>
/// GET /internal/v1/bookings/{id}/reviewability (CONTRACTS 4.3), asked by Reputation. Reviewable only when something was used from the package
/// (see <see cref="IsReviewable"/>); the window runs from <c>end_utc</c> (A-03). The answer carries no brief and no price.
/// </summary>
public sealed class ReviewabilityFeature : IServiceFeature
{
    public static readonly TimeSpan Window = TimeSpan.FromDays(14);

    /// <summary>
    /// A package may be reviewed once something was used from it (<c>units_used &gt; 0</c>), while it is Confirmed or Completed, or Resolved with <c>release</c>,
    /// up to 14 days after <c>end_utc</c> (inclusive). The one rule of this endpoint and of <c>BookingPermissions.CanReview</c>.
    /// </summary>
    public static bool IsReviewable(BookingStatus status, string? disputeResolution, long unitsUsed, DateTimeOffset endUtc, DateTimeOffset now)
    {
        var eligible = status is BookingStatus.Confirmed or BookingStatus.Completed || (status == BookingStatus.Resolved && disputeResolution == "release");
        return unitsUsed > 0 && eligible && now <= endUtc + Window;
    }

    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/internal/v1/bookings/{id:guid}/reviewability", GetAsync).RequireAuthorization("Internal");

    static async Task<IResult> GetAsync(Guid id, BookingDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var b = await db.Bookings.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { x.Status, x.DisputeResolution, x.UnitsUsed, x.EndedAt, x.EndUtc, x.ClientUserId, x.PractitionerId })
            .SingleOrDefaultAsync(ct);
        if (b is null) return PaymentWebhookFeature.Problem(ErrorCodes.NotFound, "Not found", 404);

        var ended = b.EndedAt ?? b.EndUtc;
        var reviewable = IsReviewable(b.Status, b.DisputeResolution, b.UnitsUsed, b.EndUtc, clock.GetUtcNow());
        return Results.Ok(new ReviewabilityDto(reviewable, b.ClientUserId, b.PractitionerId, ended));
    }
}
