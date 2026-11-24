namespace Gabinety.Billing;

public enum BookingStatus
{
    PendingPayment, Confirmed, CancelledByClient, LateCancelledByClient, CancelledByPractitioner,
    Expired, Completed, ClientNoShow, Disputed, Resolved,
}

public enum BookingTrigger
{
    PaymentSucceeded, PaymentFailed, HoldExpired, ClientCancels, PractitionerCancels,
    CompletionWindowElapsed, ClientNoShowReported, ClientDisputes, AdminResolves,
    /// <summary>A task failed and its unit went back to a package the job had completed because its units ran out (ADR-0013, owner decision 2): <c>Completed</c> back to <c>Confirmed</c>. The caller's preconditions decide whether it may.</summary>
    UnitRefunded,
}

/// <summary>Next is null when the transition is rejected.</summary>
public readonly record struct TransitionResult(bool Allowed, BookingStatus? Next);

/// <summary>
/// CONTRACTS 8.1 as a transition table. No I/O, no clock. Time conditions the table cannot see
/// (practitioner cancel before end, dispute window, 48 h completion) are the caller's: it only
/// sends the trigger when the condition holds (the client may cancel a package only while no unit was used, also the caller's check). <c>start</c> and <c>now</c> are kept for the signature.
/// </summary>
public static class BookingStateMachine
{
    private static readonly Dictionary<(BookingStatus, BookingTrigger), BookingStatus> Table = new()
    {
        [(BookingStatus.PendingPayment, BookingTrigger.PaymentSucceeded)] = BookingStatus.Confirmed,
        [(BookingStatus.PendingPayment, BookingTrigger.HoldExpired)] = BookingStatus.Expired,
        [(BookingStatus.PendingPayment, BookingTrigger.PaymentFailed)] = BookingStatus.Expired,
        [(BookingStatus.Confirmed, BookingTrigger.PractitionerCancels)] = BookingStatus.CancelledByPractitioner,
        [(BookingStatus.Confirmed, BookingTrigger.CompletionWindowElapsed)] = BookingStatus.Completed,
        [(BookingStatus.Confirmed, BookingTrigger.ClientNoShowReported)] = BookingStatus.ClientNoShow,
        [(BookingStatus.Confirmed, BookingTrigger.ClientDisputes)] = BookingStatus.Disputed,
        [(BookingStatus.Completed, BookingTrigger.ClientDisputes)] = BookingStatus.Disputed, // until start + 72 h (CONTRACTS 4.2), enforced by the caller; ADR proposal 0011
        [(BookingStatus.Completed, BookingTrigger.UnitRefunded)] = BookingStatus.Confirmed, // only for a package completed by running out of units, before its end and before any payout: the caller checks (PackageReopening)
        [(BookingStatus.Disputed, BookingTrigger.AdminResolves)] = BookingStatus.Resolved,
    };

    public static BookingStatus Create() => BookingStatus.PendingPayment;

    public static TransitionResult Next(BookingStatus from, BookingTrigger trigger, DateTimeOffset start, DateTimeOffset now)
    {
        // A package is cancelled in full or not at all (the caller checks that nothing was used): there is no late cancellation any more (A-03).
        if (from == BookingStatus.Confirmed && trigger == BookingTrigger.ClientCancels) return new(true, BookingStatus.CancelledByClient);
        return Table.TryGetValue((from, trigger), out var to) ? new(true, to) : new(false, null);
    }
}
