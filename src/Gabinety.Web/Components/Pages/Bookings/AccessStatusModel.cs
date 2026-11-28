using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.Bookings;

public enum BadgeKind { Neutral, Info, Success, Warning, Danger }

/// <summary>A state shown as a badge: the Polish label (always shown, colour never carries the meaning alone) and the kind of the badge (DESIGN-CONTRACT 5.7, G-8). The wire names are those of CONTRACTS 8.1.</summary>
public sealed record Badge(string Label, BadgeKind Kind)
{
    public string CssClass => $"badge badge--{Kind.ToString().ToLowerInvariant()}";

    public static Badge ForAccess(string status) => status switch
    {
        "PendingPayment" => new(Pl.Accesses.Statuses[status], BadgeKind.Warning),
        "Confirmed" => new(Pl.Accesses.Statuses[status], BadgeKind.Info),
        "Completed" => new(Pl.Accesses.Statuses[status], BadgeKind.Success),
        "CancelledByClient" or "Expired" or "Resolved" => new(Pl.Accesses.Statuses[status], BadgeKind.Neutral),
        "LateCancelledByClient" or "Disputed" => new(Pl.Accesses.Statuses[status], BadgeKind.Warning),
        "CancelledByPractitioner" or "ClientNoShow" => new(Pl.Accesses.Statuses[status], BadgeKind.Danger),
        _ => new(Pl.Accesses.UnknownStatus, BadgeKind.Neutral),
    };

    public static Badge ForPayment(string status) => status switch
    {
        "Succeeded" => new(AccessesText.Payment.Succeeded, BadgeKind.Success),
        "Refunded" => new(AccessesText.Payment.Refunded, BadgeKind.Info),
        "PartiallyRefunded" => new(AccessesText.Payment.PartiallyRefunded, BadgeKind.Info),
        "Created" => new(AccessesText.Payment.Created, BadgeKind.Warning),
        "Failed" => new(AccessesText.Payment.Failed, BadgeKind.Danger),
        _ => new(AccessesText.Payment.Unknown, BadgeKind.Neutral),
    };
}
