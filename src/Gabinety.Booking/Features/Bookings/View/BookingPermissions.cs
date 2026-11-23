using Gabinety.Billing;
using Gabinety.Booking.Features.Internal.Reviewability;
using Gabinety.Booking.Features.Lifecycle.Cancel;
using Gabinety.Booking.Features.Lifecycle.Dispute;
using Gabinety.Booking.Features.Lifecycle.NoShow;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Booking;

namespace Gabinety.Booking.Features.Bookings.View;

/// <summary>What the caller may do with a booking right now (CONTRACTS 4.4: the four booleans of <c>BookingView</c>).</summary>
public readonly record struct BookingFlags(bool CanCancel, bool CanDispute, bool CanReportNoShow, bool CanReview)
{
    public static readonly BookingFlags None = default;
}

/// <summary>
/// The four <c>BookingView</c> flags for one caller at <c>time.GetUtcNow()</c>. Pure: no I/O, no clock of its own. Each flag asks the same rule the endpoint
/// enforces, so the two cannot drift: the state table (<see cref="BookingStateMachine"/>), <see cref="DisputeFeature.InWindow"/>,
/// <see cref="ReportClientNoShowFeature.InWindow"/> (no longer offered, always false) and <see cref="ReviewabilityFeature"/>. The cancel rules, and the outcome of a cancel, are in <see cref="CancelPlanner"/>.
/// <para><c>CanReview</c> is an upper bound: Reputation owns "already reviewed", which Booking cannot see.</para>
/// An admin who is not a party gets no flag; a stranger neither.
/// </summary>
public static class BookingPermissions
{
    public static BookingFlags For(BookingEntity b, string callerId, TimeProvider time)
    {
        var isClient = b.ClientUserId == callerId;
        var isPractitioner = b.PractitionerUserId == callerId;
        if (!isClient && !isPractitioner) return BookingFlags.None;
        var now = time.GetUtcNow();
        var s = b.Status;

        var canCancel = isClient && Allowed(s, BookingTrigger.ClientCancels, b, now) && CancelPlanner.TimeRuleAllows(b, byClient: true, now)
            || isPractitioner && Allowed(s, BookingTrigger.PractitionerCancels, b, now) && CancelPlanner.TimeRuleAllows(b, byClient: false, now);
        // "Can open a dispute of some kind": the practitioner_no_show kind has the widest window, the other kinds' window lies inside it or ends with it.
        var canDispute = isClient && Allowed(s, BookingTrigger.ClientDisputes, b, now) && DisputeFeature.InWindow(b, "practitioner_no_show", now);
        const bool canReportNoShow = false; // an agent has no appointment to miss (A-03); the endpoint stays, the flag does not offer it
        var canReview = isClient && CanReview(b, now);
        return new(canCancel, canDispute, canReportNoShow, canReview);
    }

    static bool Allowed(BookingStatus from, BookingTrigger trigger, BookingEntity b, DateTimeOffset now) =>
        BookingStateMachine.Next(from, trigger, b.StartUtc, now).Allowed;

    /// <summary>The same test as the reviewability endpoint (<see cref="ReviewabilityFeature.IsReviewable"/>).</summary>
    static bool CanReview(BookingEntity b, DateTimeOffset now) => ReviewabilityFeature.IsReviewable(b.Status, b.DisputeResolution, b.UnitsUsed, b.EndUtc, now);
}
