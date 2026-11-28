using Gabinety.Web.Api;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.AgentArea;

public enum ChecklistKey { Headline, Bio, Offering, Github, Nip, Portfolio, Email }

public sealed record ChecklistItem(ChecklistKey Key, bool Done)
{
    public string Title => Key switch
    {
        ChecklistKey.Headline => PlAgent.Checklist.Headline, ChecklistKey.Bio => PlAgent.Checklist.Bio, ChecklistKey.Offering => PlAgent.Checklist.Package,
        ChecklistKey.Github => PlAgent.Checklist.Github, ChecklistKey.Nip => PlAgent.Checklist.Nip, ChecklistKey.Portfolio => PlAgent.Checklist.Portfolio,
        _ => PlAgent.Checklist.Email,
    };

    public string Help => Key switch
    {
        ChecklistKey.Headline or ChecklistKey.Bio => PlAgent.Checklist.HelpProfile, ChecklistKey.Offering => PlAgent.Checklist.HelpOffers,
        ChecklistKey.Github => PlAgent.Checklist.HelpGithub, ChecklistKey.Nip => PlAgent.Checklist.HelpNip, ChecklistKey.Portfolio => PlAgent.Checklist.HelpPortfolio,
        _ => PlAgent.Checklist.HelpEmail,
    };

    /// <summary>Where the missing step is done; null when it is done on the dashboard itself (GitHub) or outside the product (the e-mail link).</summary>
    public string? Href => Key switch
    {
        ChecklistKey.Headline or ChecklistKey.Bio or ChecklistKey.Nip or ChecklistKey.Portfolio => AgentPaths.Profile,
        ChecklistKey.Offering => AgentPaths.Offers,
        _ => null,
    };
}

/// <summary>The submit checklist of CONTRACTS 3.5, in its order. It reads only what the API returned (the profile and the offers), never form state, so a step is ticked only when the service agrees.</summary>
public static class OnboardingChecklist
{
    public const int BioMinimum = 200;

    public static IReadOnlyList<ChecklistItem> Build(AgentPrivateDto p, IReadOnlyList<PackagePrivateDto> offers) =>
    [
        new(ChecklistKey.Headline, p.Headline.Trim().Length > 0),
        new(ChecklistKey.Bio, p.Bio.Trim().Length >= BioMinimum),
        new(ChecklistKey.Offering, offers.Any(o => o.IsActive)),
        new(ChecklistKey.Github, p.GithubLinked),
        new(ChecklistKey.Nip, p.NipValid),
        new(ChecklistKey.Portfolio, !string.IsNullOrEmpty(p.PortfolioUrl)),
        new(ChecklistKey.Email, p.EmailVerified),
    ];

    public static int Remaining(IReadOnlyList<ChecklistItem> items) => items.Count(i => !i.Done);

    /// <summary>The items a <c>422 submit_incomplete</c> names (the Directory's own words), in the order of the checklist.</summary>
    public static IReadOnlyList<ChecklistKey> FromServer(IEnumerable<string> unmet)
    {
        static ChecklistKey? Map(string code) => code switch
        {
            "headline" => ChecklistKey.Headline, "bio" => ChecklistKey.Bio, "offering" => ChecklistKey.Offering, "github_linked" => ChecklistKey.Github,
            "nip_valid" => ChecklistKey.Nip, "portfolioUrl" => ChecklistKey.Portfolio, "email_verified" => ChecklistKey.Email, _ => null,
        };
        return [.. unmet.Select(Map).OfType<ChecklistKey>().Distinct().Order()];
    }

    /// <summary>Only a draft or a rejected profile can be sent (CONTRACTS 3.4); everything else has no submit button.</summary>
    public static bool CanBeSubmitted(string status) => status is "Draft" or "Rejected";
}

/// <summary>The routes of the area, in one place.</summary>
public static class AgentPaths
{
    public const string Home = "/panel-agenta", Profile = "/panel-agenta/profil", Offers = "/panel-agenta/oferty", Payments = "/panel-agenta/platnosci";
}
