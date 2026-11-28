using Gabinety.Web.Api;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.Accesses;

/// <summary>What the user reads when a call of the client area failed: from the status and the code only, never from the server's title, detail or message (they are English and may echo input).</summary>
public static class ActionFailure
{
    public static string Text(ApiFailure f) => f switch
    {
        { Status: 0 } => AccessesText.Errors.Network,
        { Code: "invalid_transition" } => AccessesText.Errors.InvalidTransition,
        { Code: "insufficient_quota" } => AccessesText.Errors.QuotaExhausted,
        { Code: "dependency_unavailable" } or { Status: 503 } => AccessesText.Errors.Unavailable,
        { Code: "not_found" } or { Status: 404 } => AccessesText.NotFound,
        { Code: "validation_failed" or "invalid_input" } or { Status: 400 or 422 } => AccessesText.Errors.Validation,
        { Code: "forbidden" } or { Status: 403 } => AccessesText.Errors.Forbidden,
        { Code: "rate_limited" } or { Status: 429 } => AccessesText.Errors.RateLimited,
        _ => AccessesText.Errors.Generic,
    };

    /// <summary>The state of the booking changed under the user: the way out is to read it again.</summary>
    public static bool OffersRefresh(ApiFailure f) => f.Code == "invalid_transition";
}

/// <summary>Dispute and cancel rules of CONTRACTS 4.2, applied first so every problem is shown at once.</summary>
public static class DisputeRules
{
    public const int TextMin = 10, TextMax = 1000, ReasonMax = 500;
    /// <summary>The kinds a client can pick; <c>practitioner_no_show</c> stays in the contract but the interface no longer offers it (there is no appointment to miss).</summary>
    public static readonly string[] Kinds = ["not_as_described", "other"];

    public static string KindLabel(string kind) => kind switch
    {
        "not_as_described" => AccessesText.Dispute.NotAsDescribed,
        _ => AccessesText.Dispute.Other,
    };

    public static string? KindProblem(string? kind) => kind is not null && Kinds.Contains(kind) ? null : AccessesText.Dispute.KindInvalid;

    public static string? TextProblem(string? text)
    {
        var n = (text ?? "").Trim().Length;
        return n < TextMin ? AccessesText.Dispute.TooShort : n > TextMax ? AccessesText.Dispute.TooLong : null;
    }

    public static string? ReasonProblem(string? reason) => (reason ?? "").Trim().Length > ReasonMax ? AccessesText.Cancel.ReasonTooLong : null;
}
