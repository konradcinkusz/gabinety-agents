using Gabinety.Booking.Features.Bookings.View;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Booking;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests;

/// <summary>BookingPermissions (T-04.07d): the four flags for the caller at <c>now</c>, same windows as the endpoints, at their exact edges.</summary>
public class BookingPermissionsTests
{
    const string Client = "demo-client", Practitioner = "demo-practitioner", Stranger = "demo-stranger";
    static readonly DateTimeOffset Start = new(2026, 12, 1, 10, 0, 0, TimeSpan.Zero);
    static readonly TimeSpan S = TimeSpan.FromSeconds(1), H = TimeSpan.FromHours(1);

    sealed class Clock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    static BookingEntity Row(BookingStatus status, TimeSpan? duration = null, string? resolution = null, DateTimeOffset? endedAt = null, long unitsUsed = 0) => new()
    {
        Id = Guid.NewGuid(), ClientUserId = Client, PractitionerUserId = Practitioner, StartUtc = Start, EndUtc = Start + (duration ?? H),
        Status = status, DisputeResolution = resolution, EndedAt = endedAt, UnitsTotal = 10, UnitsUsed = unitsUsed,
    };

    static BookingFlags Flags(BookingEntity b, string caller, DateTimeOffset now) => BookingPermissions.For(b, caller, new Clock(now));

    static readonly BookingStatus[] All = Enum.GetValues<BookingStatus>();

    [Fact]
    public void Cancel_client_any_time_while_Confirmed_and_unused_practitioner_only_before_the_end()
    {
        var b = Row(BookingStatus.Confirmed);
        foreach (var now in new[] { Start - 30 * H, Start - S, Start + S, b.EndUtc - S, b.EndUtc, b.EndUtc + 100 * H })
            Assert.True(Flags(b, Client, now).CanCancel);
        Assert.True(Flags(b, Practitioner, Start - 30 * H).CanCancel);
        Assert.True(Flags(b, Practitioner, b.EndUtc - S).CanCancel);   // 1 s inside
        Assert.False(Flags(b, Practitioner, b.EndUtc).CanCancel);      // end itself is outside (the endpoint says now < end)
        Assert.False(Flags(b, Practitioner, b.EndUtc + S).CanCancel);
        Assert.False(Flags(b, Stranger, Start - H).CanCancel);

        var used = Row(BookingStatus.Confirmed, unitsUsed: 1);
        Assert.False(Flags(used, Client, Start + S).CanCancel);        // A-03: not after a unit was used
        Assert.True(Flags(used, Practitioner, Start + S).CanCancel);   // the admin's withdrawal is not blocked by use
    }

    [Fact]
    public void Cancel_is_false_from_every_status_but_Confirmed()
    {
        foreach (var s in All.Where(s => s != BookingStatus.Confirmed))
            foreach (var who in new[] { Client, Practitioner, Stranger })
                Assert.False(Flags(Row(s), who, Start - 48 * H).CanCancel, $"{s}/{who}");
    }

    // A-03: a Confirmed package may be disputed from the purchase until end + 72 h, whatever the kind and whatever its length.
    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(3600, true)]
    [InlineData(73 * 3600 - 1, true)]  // the end (start + 1 h) + 72 h, 1 s inside
    [InlineData(73 * 3600, false)]
    [InlineData(73 * 3600 + 1, false)]
    public void Dispute_runs_until_end_plus_72h(int offsetSeconds, bool expected)
    {
        var b = Row(BookingStatus.Confirmed);
        var now = Start + TimeSpan.FromSeconds(offsetSeconds);
        Assert.Equal(expected, Flags(b, Client, now).CanDispute);
        Assert.False(Flags(b, Practitioner, now).CanDispute);
        Assert.False(Flags(b, Stranger, now).CanDispute);
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    public void Dispute_for_a_month_long_package_is_bound_by_its_end_plus_72h(int offsetFromWindowEndSeconds, bool expected)
    {
        var b = Row(BookingStatus.Confirmed, duration: TimeSpan.FromDays(30));
        var now = b.EndUtc + 72 * H + TimeSpan.FromSeconds(offsetFromWindowEndSeconds);
        Assert.Equal(expected, Flags(b, Client, now).CanDispute);
    }

    [Fact]
    public void Dispute_is_false_from_every_status_but_Confirmed_and_Completed_and_only_the_client_may_from_Completed()
    {
        Assert.True(Flags(Row(BookingStatus.Completed), Client, Start + 2 * H).CanDispute);
        Assert.True(Flags(Row(BookingStatus.Completed), Client, Start + 72 * H).CanDispute);  // the old start + 72 h no longer ends it
        Assert.False(Flags(Row(BookingStatus.Completed), Client, Start + 73 * H).CanDispute); // end + 72 h: the right is over
        Assert.False(Flags(Row(BookingStatus.Completed), Practitioner, Start + 2 * H).CanDispute);
        foreach (var s in All.Where(s => s is not (BookingStatus.Confirmed or BookingStatus.Completed)))
            foreach (var who in new[] { Client, Practitioner, Stranger })
                Assert.False(Flags(Row(s), who, Start + 2 * H).CanDispute, $"{s}/{who}");
    }

    [Fact]
    public void ReportNoShow_is_never_offered_any_more()
    {
        foreach (var s in All)
            foreach (var who in new[] { Client, Practitioner, Stranger })
                foreach (var offset in new[] { 0, 15 * 60, 3600, 72 * 3600 - 1 })
                    Assert.False(Flags(Row(s), who, Start + TimeSpan.FromSeconds(offset)).CanReportNoShow, $"{s}/{who}/{offset}");
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(14 * 24 * 3600 - 1, true)]
    [InlineData(14 * 24 * 3600, true)]   // inclusive, as the reviewability endpoint
    [InlineData(14 * 24 * 3600 + 1, false)]
    public void Review_is_for_the_client_of_a_used_package_within_14_days_of_the_end(int afterEndSeconds, bool expected)
    {
        var b = Row(BookingStatus.Completed, unitsUsed: 1);
        var now = b.EndUtc + TimeSpan.FromSeconds(afterEndSeconds);
        Assert.Equal(expected, Flags(b, Client, now).CanReview);
        Assert.False(Flags(b, Practitioner, now).CanReview);
        Assert.False(Flags(b, Stranger, now).CanReview);
    }

    [Fact]
    public void Review_window_runs_from_end_utc_not_from_ended_at()
    {
        var b = Row(BookingStatus.Completed, endedAt: Start + 5 * H, unitsUsed: 1);
        Assert.True(Flags(b, Client, b.EndUtc + TimeSpan.FromDays(14)).CanReview);
        Assert.False(Flags(b, Client, b.EndUtc + TimeSpan.FromDays(14) + S).CanReview);
    }

    [Fact]
    public void Review_needs_a_used_unit_and_is_open_while_Confirmed_too()
    {
        foreach (var s in new[] { BookingStatus.Confirmed, BookingStatus.Completed })
        {
            Assert.False(Flags(Row(s, unitsUsed: 0), Client, Start + 2 * H).CanReview, $"{s} unused");
            Assert.True(Flags(Row(s, unitsUsed: 1), Client, Start + 2 * H).CanReview, $"{s} used");
            Assert.False(Flags(Row(s, unitsUsed: 1), Practitioner, Start + 2 * H).CanReview, $"{s} practitioner");
        }
    }

    [Theory]
    [InlineData("release", true)]
    [InlineData("refund_full", false)]
    [InlineData("refund_partial", false)]
    [InlineData(null, false)]
    public void Review_after_a_dispute_only_when_released(string? resolution, bool expected)
    {
        var b = Row(BookingStatus.Resolved, resolution: resolution, unitsUsed: 1);
        Assert.Equal(expected, Flags(b, Client, b.EndUtc + 2 * H).CanReview);
        Assert.False(Flags(b, Practitioner, b.EndUtc + 2 * H).CanReview);
    }

    [Fact]
    public void Review_is_false_from_every_other_status()
    {
        foreach (var s in All.Where(s => s is not (BookingStatus.Confirmed or BookingStatus.Completed or BookingStatus.Resolved)))
            Assert.False(Flags(Row(s, resolution: "release", unitsUsed: 1), Client, Start + 2 * H).CanReview, s.ToString());
    }

    [Fact]
    public void The_practitioner_sees_brief_and_client_name_from_Confirmed_onward_and_the_client_always()
    {
        var names = new Gabinety.Booking.Features.Payments.PartyNames(ClientDisplayName: "demo-client-name");
        foreach (var s in All)
        {
            var b = Row(s);
            b.BriefText = "demo-brief";
            var seen = s is not (BookingStatus.PendingPayment or BookingStatus.Expired);
            var p = Gabinety.Booking.Features.Payments.BookingViewFeature.ToView(b, null, Practitioner, admin: false, new Clock(Start), names);
            Assert.Equal(seen, p.Brief is not null);
            Assert.Equal(seen, p.Client is not null);
            var c = Gabinety.Booking.Features.Payments.BookingViewFeature.ToView(b, null, Client, admin: false, new Clock(Start), names);
            Assert.NotNull(c.Brief);
            Assert.Null(c.Client);
            // the meeting URL rule is unchanged: Confirmed only
            b.MeetingUrlSnapshot = "https://meet.demo.invalid/x";
            Assert.Equal(s == BookingStatus.Confirmed, Gabinety.Booking.Features.Payments.BookingViewFeature.ToView(b, null, Practitioner, false, new Clock(Start)).MeetingUrl is not null);
            Assert.Null(Gabinety.Booking.Features.Payments.BookingViewFeature.ToView(b, null, Stranger, false, new Clock(Start), names).Brief);
        }
    }

    [Fact]
    public void The_resolution_note_of_a_Resolved_booking_is_for_the_admin_only_and_a_cancel_reason_stays_for_the_parties()
    {
        static Gabinety.Contracts.Booking.BookingView V(BookingEntity b, string who, bool admin) =>
            Gabinety.Booking.Features.Payments.BookingViewFeature.ToView(b, null, who, admin);

        var resolved = Row(BookingStatus.Resolved);
        resolved.StatusReason = "demo-admin-note: the practitioner was late";
        Assert.Null(V(resolved, Client, false).StatusReason);
        Assert.Null(V(resolved, Practitioner, false).StatusReason);
        Assert.Equal("demo-admin-note: the practitioner was late", V(resolved, "demo-admin", true).StatusReason);

        var cancelled = Row(BookingStatus.CancelledByClient);
        cancelled.StatusReason = "demo-cancel-reason";
        Assert.Equal("demo-cancel-reason", V(cancelled, Client, false).StatusReason);
        Assert.Equal("demo-cancel-reason", V(cancelled, Practitioner, false).StatusReason);
    }
}
