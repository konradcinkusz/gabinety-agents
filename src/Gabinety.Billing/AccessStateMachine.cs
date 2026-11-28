namespace Gabinety.Billing;

public enum AccessStatus
{
    PendingPayment, Confirmed, CancelledByClient, LateCancelledByClient, CancelledByPractitioner,
    Expired, Completed, ClientNoShow, Disputed, Resolved,
}

public enum AccessTrigger
{
    PaymentSucceeded, PaymentFailed, HoldExpired, ClientCancels, PractitionerCancels,
    CompletionWindowElapsed, ClientNoShowReported, ClientDisputes, AdminResolves,
    /// <summary>A task failed and its unit went back to a package the job had completed because its units ran out (ADR-0013, owner decision 2): <c>Completed</c> back to <c>Confirmed</c>. The caller's preconditions decide whether it may.</summary>
    UnitRefunded,
}

/// <summary>Next is null when the transition is rejected.</summary>
public readonly record struct TransitionResult(bool Allowed, AccessStatus? Next);

/// <summary>
/// CONTRACTS 8.1 as a transition table. No I/O, no clock. Time conditions the table cannot see
/// (practitioner cancel before end, dispute window, 48 h completion) are the caller's: it only
/// sends the trigger when the condition holds (the client may cancel a package only while no unit was used, also the caller's check). <c>start</c> and <c>now</c> are kept for the signature.
/// </summary>
public static class AccessStateMachine
{
    private static readonly Dictionary<(AccessStatus, AccessTrigger), AccessStatus> Table = new()
    {
        [(AccessStatus.PendingPayment, AccessTrigger.PaymentSucceeded)] = AccessStatus.Confirmed,
        [(AccessStatus.PendingPayment, AccessTrigger.HoldExpired)] = AccessStatus.Expired,
        [(AccessStatus.PendingPayment, AccessTrigger.PaymentFailed)] = AccessStatus.Expired,
        [(AccessStatus.Confirmed, AccessTrigger.PractitionerCancels)] = AccessStatus.CancelledByPractitioner,
        [(AccessStatus.Confirmed, AccessTrigger.CompletionWindowElapsed)] = AccessStatus.Completed,
        [(AccessStatus.Confirmed, AccessTrigger.ClientNoShowReported)] = AccessStatus.ClientNoShow,
        [(AccessStatus.Confirmed, AccessTrigger.ClientDisputes)] = AccessStatus.Disputed,
        [(AccessStatus.Completed, AccessTrigger.ClientDisputes)] = AccessStatus.Disputed, // until start + 72 h (CONTRACTS 4.2), enforced by the caller; ADR proposal 0011
        [(AccessStatus.Completed, AccessTrigger.UnitRefunded)] = AccessStatus.Confirmed, // only for a package completed by running out of units, before its end and before any payout: the caller checks (PackageReopening)
        [(AccessStatus.Disputed, AccessTrigger.AdminResolves)] = AccessStatus.Resolved,
    };

    public static AccessStatus Create() => AccessStatus.PendingPayment;

    public static TransitionResult Next(AccessStatus from, AccessTrigger trigger, DateTimeOffset start, DateTimeOffset now)
    {
        // A package is cancelled in full or not at all (the caller checks that nothing was used): there is no late cancellation any more (A-03).
        if (from == AccessStatus.Confirmed && trigger == AccessTrigger.ClientCancels) return new(true, AccessStatus.CancelledByClient);
        return Table.TryGetValue((from, trigger), out var to) ? new(true, to) : new(false, null);
    }
}
