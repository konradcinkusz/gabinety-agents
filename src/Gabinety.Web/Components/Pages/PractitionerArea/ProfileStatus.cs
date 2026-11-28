using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.PractitionerArea;

/// <summary>A practitioner status as the badge shows it: the Polish label (the strings the journeys read) and the badge kind. An unknown value is shown as unknown, never as the raw wire text.</summary>
public static class ProfileStatus
{
    public static string Label(string status) => status switch
    {
        "Draft" => PlAgent.Status.Draft, "Submitted" => PlAgent.Status.Submitted, "Approved" => PlAgent.Status.Approved,
        "Rejected" => PlAgent.Status.Rejected, "Suspended" => PlAgent.Status.Suspended, _ => PlAgent.Status.Unknown,
    };

    /// <summary>The <c>badge--*</c> modifier of the design contract (5.7): neutral, warning, success, danger.</summary>
    public static string Kind(string status) => status switch { "Submitted" => "warning", "Approved" => "success", "Rejected" or "Suspended" => "danger", _ => "neutral" };

    public static string? Help(string status) => status switch
    {
        "Rejected" => PlAgent.Status.RejectedHelp, "Submitted" => PlAgent.Status.SubmittedHelp, "Approved" => PlAgent.Status.ApprovedHelp,
        "Suspended" => PlAgent.Status.SuspendedHelp, _ => null,
    };
}
