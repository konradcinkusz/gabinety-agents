using Gabinety.Web.Api;
using Gabinety.Web.Components.Shared;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.Accesses;

/// <summary>
/// What cancelling THIS booking would mean for the money (F-11). The page never works it out from the clock (the 24 hour rule of CONTRACTS 9.2 is the service's, on its clock, and a test clock moves it):
/// the Booking service sends it as <c>cancelOutcome</c> on the booking read, only while <c>canCancel</c>. Without the field the dialog says the general rule; with it, ONLY the outcome (the amount returned): the general rule is not repeated.
/// </summary>
public sealed record CancelOutcome(long RefundMinor, long PaidMinor)
{
    public bool IsFull => RefundMinor >= PaidMinor && PaidMinor > 0;
    public bool IsNone => RefundMinor <= 0;

    /// <summary>The amount is what counts; a refund outside 0 to the price is not believed (the general rule stays).</summary>
    public static CancelOutcome? From(CancelOutcomeDto? dto, MoneyDto price) =>
        dto is { Refund: { } refund } && refund.AmountMinor >= 0 && refund.AmountMinor <= price.AmountMinor ? new CancelOutcome(refund.AmountMinor, price.AmountMinor) : null;

    /// <summary>A client who would get nothing back is warned (danger); every other sentence only informs.</summary>
    public static bool Warns(CancelOutcome? outcome, string role) => role != AccessClient.AgentRole && outcome is { IsNone: true };

    /// <summary>The sentence of the cancel dialog: the outcome when the booking carries one, else the general rule of the role.</summary>
    public static string Sentence(CancelOutcome? outcome, string role)
    {
        if (role == AccessClient.AgentRole)
            return outcome is null ? AccessesText.Cancel.AgentRule : string.Format(AccessesText.Cancel.AgentOutcome, Money.Pln(outcome.RefundMinor));
        return outcome switch
        {
            null => AccessesText.Cancel.ClientRule,
            { IsNone: true } => AccessesText.Cancel.OutcomeNone,
            { IsFull: true } => string.Format(AccessesText.Cancel.OutcomeFull, Money.Pln(outcome.RefundMinor)),
            _ => string.Format(AccessesText.Cancel.OutcomePartial, Money.Pln(outcome.RefundMinor), Money.Pln(outcome.PaidMinor)),
        };
    }
}
