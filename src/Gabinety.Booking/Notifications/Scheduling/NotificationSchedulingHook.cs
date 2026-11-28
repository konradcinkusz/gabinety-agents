using Gabinety.Billing;
using Gabinety.Booking.Features.Lifecycle.Core;
using Gabinety.ServiceDefaults;

namespace Gabinety.Booking.Notifications.Scheduling;

/// <summary>
/// Writes the notification rows of CONTRACTS 8.1 inside the transition's own transaction (T-04.09b); it never saves, so a failure anywhere in the
/// transition rolls the rows back with it.
/// <list type="bullet">
/// <item><c>PendingPayment</c> to <c>Confirmed</c>: <c>BookingConfirmed</c> to the client and the practitioner (due now), <c>ReviewRequest</c> to the client (due 2 h after the end).
/// No reminders before the start (A-03): the start of a package is the purchase.</item>
/// <item>A cancellation (any of the three) or a reported client no-show: the unsent reminders and the review request are removed (no review after a
/// cancellation or a no-show, CONTRACTS 8.1); sent rows and the confirmations stay. A cancellation by the practitioner also schedules <c>Cancelled</c> to the client, due now.</item>
/// </list>
/// Rows go through <see cref="INotificationWriter.ScheduleAsync"/>, which checks saved rows too, so a replay never duplicates (unique per booking, kind, user).
/// </summary>
public sealed class NotificationSchedulingHook(INotificationWriter writer) : IBookingTransitionHook
{
    public static readonly TimeSpan ReviewDelay = TimeSpan.FromHours(2);
    static readonly NotificationKind[] RemovedWhenNotHeld = [NotificationKind.Reminder24h, NotificationKind.Reminder1h, NotificationKind.ReviewRequest];

    public async Task OnTransitionAsync(BookingTransitionContext context, CancellationToken ct)
    {
        switch (context.To)
        {
            case AccessStatus.Confirmed when context.From == AccessStatus.PendingPayment:
                await ScheduleConfirmedAsync(context, ct);
                break;
            case AccessStatus.CancelledByClient or AccessStatus.LateCancelledByClient or AccessStatus.CancelledByPractitioner or AccessStatus.ClientNoShow:
                await writer.CancelUnsentAsync(context.Access.Id, RemovedWhenNotHeld, ct);
                // the practitioner's cancel (their own, or the cascade of a suspension) is news to the client: the Cancelled template of T-04.09a; the client's own cancel is not
                if (context.To == AccessStatus.CancelledByPractitioner)
                    await writer.ScheduleAsync(context.Access, NotificationKind.Cancelled, context.Access.ClientUserId, context.Now, ct);
                break;
        }
    }

    async Task ScheduleConfirmedAsync(BookingTransitionContext c, CancellationToken ct)
    {
        var b = c.Access;
        await writer.ScheduleAsync(b, NotificationKind.BookingConfirmed, b.ClientUserId, c.Now, ct);
        await writer.ScheduleAsync(b, NotificationKind.BookingConfirmed, b.PractitionerUserId, c.Now, ct);

        // No reminders (A-03): a package starts at the purchase, so there is no date to remind of. The kinds stay in the enum for old rows.

        await writer.ScheduleAsync(b, NotificationKind.ReviewRequest, b.ClientUserId, b.EndUtc + ReviewDelay, ct);
    }
}

/// <summary>Registers the scheduling hook; <see cref="BookingTransitions"/> runs it inside every transition.</summary>
public sealed class NotificationSchedulingFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder) => builder.Services.AddScoped<IBookingTransitionHook, NotificationSchedulingHook>();

    public void Map(IEndpointRouteBuilder endpoints) { }
}
