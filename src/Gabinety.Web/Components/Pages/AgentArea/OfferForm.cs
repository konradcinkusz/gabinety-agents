using Gabinety.Web.Api;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.AgentArea;

/// <summary>
/// The offer form as posted. It validates what is cheap to say at once (so every problem is shown together, G-9) and leaves the rest to the service, which stays the authority
/// (<see cref="FieldErrors"/>). Money: the price is typed in zloty and leaves here as integer grosz (<see cref="PriceInput"/>).
/// </summary>
public sealed record OfferForm(string Title, string Description, string Duration, string Price, string Problem, IReadOnlyList<string> Techs, string Kind, string Units)
{
    public const int MaxActive = 5, MaxTechs = 4;
    public static readonly OfferForm Empty = new("", "", "30", "", "", [], "Tasks", "1");

    public const int TasksMin = 1, TasksMax = 1000, ChatMin = 100, ChatMax = 100000;

    /// <summary>The service's field names in the order of the form.</summary>
    public static readonly string[] FieldOrder = ["title", "description", "durationMinutes", "priceMinor", "problemSlug", "techSlugs", "kind", "units"];

    /// <summary>The id of the control of a service field.</summary>
    public static string IdOf(string field) => field switch { "durationMinutes" => "duration", "priceMinor" => "price", _ => field };

    public static OfferForm From(PackagePrivateDto o) => new(o.Title, o.Description, o.DurationMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture), PriceInput.ToInput(o.Price.AmountMinor), o.ProblemSlug, o.TechSlugs, o.Kind, o.Units.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public static OfferForm FromPosted(FormValues f) => new(f.Text("title"), f.Text("description"), f.Text("duration"), f.Text("price"), f.Text("problemSlug"), f.All("techs", MaxTechs + 8), f.Text("kind"), f.Text("units"));

    public IReadOnlyDictionary<string, string> Validate()
    {
        var errors = new Dictionary<string, string>();
        if (Title.Length is < 5 or > 80) errors["title"] = PlAgent.Errors.ByField["title"];
        if (Description.Length > 600) errors["description"] = PlAgent.Errors.ByField["description"];
        if (Duration is not ("30" or "60")) errors["durationMinutes"] = PlAgent.Errors.ByField["durationMinutes"];
        switch (PriceInput.Parse(Price).Problem)
        {
            case PriceProblem.Format: errors["priceMinor"] = PlAgent.Offers.PriceFormat; break;
            case PriceProblem.Range: errors["priceMinor"] = PlAgent.Offers.PriceRange; break;
        }
        if (Problem.Length == 0) errors["problemSlug"] = PlAgent.Errors.ByField["problemSlug"];
        if (Techs.Count > MaxTechs) errors["techSlugs"] = PlAgent.Errors.ByField["techSlugs"];
        if (Kind is not ("Tasks" or "Chat")) errors["kind"] = PlAgent.Errors.ByField["kind"];
        else if (!int.TryParse(Units, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var units) || units < (Kind == "Chat" ? ChatMin : TasksMin) || units > (Kind == "Chat" ? ChatMax : TasksMax))
            errors["units"] = Kind == "Chat" ? PlAgent.Offers.UnitsRangeChat : PlAgent.Offers.UnitsRangeTasks;
        return errors;
    }

    /// <summary>The body for the service; call only when <see cref="Validate"/> found nothing.</summary>
    public PackageBody ToBody() => new(Title, Description, Duration == "60" ? 60 : 30, PriceInput.Parse(Price).Grosz, Problem, Techs, Kind, int.Parse(Units, System.Globalization.CultureInfo.InvariantCulture));
}
