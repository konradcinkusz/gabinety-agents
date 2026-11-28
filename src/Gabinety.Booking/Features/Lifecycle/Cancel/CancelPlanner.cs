using Gabinety.Billing;
using Gabinety.Booking.Features.Lifecycle.Core;
using Gabinety.Contracts.Booking;
using Gabinety.Contracts.Common;
using AccessEntity = Gabinety.Booking.Features.Bookings.Entities.Access;
using AccessStatus = Gabinety.Billing.AccessStatus;

namespace Gabinety.Booking.Features.Lifecycle.Cancel;

/// <summary>What a cancel will do: the refund in grosz (0 for none), the provider's reason and the outcome the UI tells the user.</summary>
public readonly record struct CancelPlan(long Refund, RefundReason Reason, CancelOutcomeKind Outcome)
{
    public CancelOutcomeDto ToDto() => new(Outcome, new MoneyDto(Refund));
}

/// <summary>
/// The one place that decides the result of a cancel (CONTRACTS 8.1, 9.2). <see cref="CancelFeature"/> performs it and <c>BookingViewFeature.ToView</c> shows it
/// before the user confirms (<c>cancelOutcome</c>), so the dialog and the money cannot disagree. Pure: no I/O, no clock of its own.
/// </summary>
public static class CancelPlanner
{
    /// <summary>The practitioner (the admin's withdrawal of an agent) may cancel any time before the end; the client only while no unit of the package was used (A-03). The state table cannot see either.</summary>
    public static bool TimeRuleAllows(AccessEntity b, bool byClient, DateTimeOffset now) => byClient ? b.UnitsUsed == 0 : now < b.EndUtc;

    public static AccessTrigger TriggerFor(bool byClient) => byClient ? AccessTrigger.ClientCancels : AccessTrigger.PractitionerCancels;

    /// <summary>Null when the state table or the time rule refuses the cancel, else its plan.</summary>
    public static CancelPlan? Plan(AccessEntity b, bool byClient, DateTimeOffset now)
    {
        if (!AccessStateMachine.Next(b.Status, TriggerFor(byClient), b.StartUtc, now).Allowed || !TimeRuleAllows(b, byClient, now)) return null;
        var money = byClient ? MoneyEvent.ClientCancelledEarly : MoneyEvent.PractitionerCancelled; // the client's cancel is always a full refund (A-03)
        var refund = RefundPolicy.Settle(money, Money.Pln(b.PriceMinor), rateBps: 0, pairAlreadyCharged: true).Refund.Minor;
        var kind = refund <= 0 ? CancelOutcomeKind.NoRefund : refund >= b.PriceMinor ? CancelOutcomeKind.FullRefund : CancelOutcomeKind.Partial;
        return new(refund, byClient ? RefundReason.ClientCancelledEarly : RefundReason.PractitionerCancelled, kind);
    }
}
