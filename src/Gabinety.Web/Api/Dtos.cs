using System.Text.Json.Serialization;
namespace Gabinety.Web.Api;

// The web host's own copies of the contracts it reads (BLAZOR-WEB-APP section 4 and 5, P3): the contract is HTTP, so this project does not reference Gabinety.Contracts (SPEC 3.2:
// the web references the kernel and nothing else). Only the fields the pages use are copied; an extra field in an answer is ignored. A test in tests/Gabinety.Web.Tests serializes the
// real Contracts types with the services' own JSON options and reads them into these records (and back), so a renamed field is a failing test and not a blank column.
// Strings stand for enums (badges, statuses): a value this copy does not know still renders, as "unknown", instead of failing the whole page.

public sealed record MoneyDto(long AmountMinor, string Currency);

public sealed record CategoryDto(string Slug, string NamePl);

public sealed record TaxonomyDto(string Version, IReadOnlyList<CategoryDto> Problems, IReadOnlyList<CategoryDto> Techs);

public sealed record RatingDto(double Average, int Count);

public sealed record AgentCardDto(Guid Id, string Slug, string DisplayName, string Headline, string? GithubLogin, IReadOnlyList<string> Badges,
    RatingDto? Rating, bool IsNew, MoneyDto FromPrice, IReadOnlyList<string> ProblemSlugs, IReadOnlyList<int> Durations);

/// <summary>A cursor page (CONTRACTS 0); <c>NextCursor</c> is absent on the last page.</summary>
public sealed record PageDto<T>(IReadOnlyList<T> Items, string? NextCursor);

public sealed record SitemapCategoryDto(string Path, [property: JsonPropertyName("practitionerCount")] int AgentCount, bool Indexable);

public sealed record SitemapAgentDto(string Slug, DateTimeOffset LastModified);

/// <summary><c>Practitioners</c> is optional so a caller that only needs the categories (the list pages) is unaffected by it.</summary>
public sealed record SitemapDto(IReadOnlyList<SitemapCategoryDto> Categories, [property: JsonPropertyName("practitioners")] IReadOnlyList<SitemapAgentDto>? Agents = null);

/// <summary><c>Kind</c> is <c>Tasks</c> or <c>Chat</c> (a string stands for the enum); <c>Units</c> is the number of tasks, or thousands of tokens for <c>Chat</c> (docs/agentic/PLAN.md 2.1).</summary>
public sealed record PackageDto(Guid Id, string Title, string Description, int DurationMinutes, MoneyDto Price, string ProblemSlug, IReadOnlyList<string> TechSlugs, string Kind = "Tasks", int Units = 1);

/// <summary>The public profile (CONTRACTS 3.2): the card's fields and the extras side by side (flat on the wire).</summary>
public sealed record AgentProfileDto(Guid Id, string Slug, string DisplayName, string Headline, string? GithubLogin, IReadOnlyList<string> Badges, RatingDto? Rating, bool IsNew,
    MoneyDto FromPrice, string Bio, string? PortfolioUrl, [property: JsonPropertyName("offerings")] IReadOnlyList<PackageDto> Packages, int CompletedCount, DateTimeOffset MemberSince);

public sealed record RankingWeightsDto(double Rating, double Reliability, double Availability, double Completeness);

public sealed record RankingConstantsDto(int M, double Mu, int MinReviewsToShowRating, int SlotsForFullAvailability);

public sealed record RankingRulesDto(string Version, DateTimeOffset EffectiveFrom, RankingWeightsDto Weights, RankingConstantsDto Constants);

public sealed record AgentRefDto(Guid Id, string DisplayName, string Slug);

public sealed record ClientRefDto(string DisplayName);

/// <summary>The part of <c>BookingView</c> (CONTRACTS 4.4) the list shows. <c>Status</c> is the wire name (<c>PendingPayment</c>, <c>Confirmed</c>, ...).</summary>
public sealed record AccessViewDto(Guid Id, string Status, DateTimeOffset StartUtc, DateTimeOffset EndUtc, int DurationMinutes, [property: JsonPropertyName("offeringTitle")] string PackageTitle,
    MoneyDto Price, [property: JsonPropertyName("practitioner")] AgentRefDto Agent, ClientRefDto? Client, string Kind = "Tasks", long UnitsTotal = 0, long UnitsUsed = 0);

public sealed record ReplyDto(string Text, DateTimeOffset RepliedAt);

public sealed record ReviewDto(Guid Id, int Rating, string Text, DateTimeOffset CreatedAt, ReplyDto? Reply);

/// <summary>The gateway state of the Assistant (CONTRACTS 6): <c>Ready | Fake | NotAuthenticated | Unavailable | Disabled</c>. A page keys off this, not off the health verdict.</summary>
public sealed record AssistantStatusDto(string State, IReadOnlyList<string> Features);

// ---- the booking flow and the client area (CONTRACTS 3.2, 4.2, 4.4, 5, 6)

public sealed record BriefDto(string Text, string? ProblemSlug, bool AiAssisted);

/// <summary>A package has no time slot (docs/agentic/PLAN.md A-02): the request carries no <c>startUtc</c>.</summary>
public sealed record CreateAccessRequestDto([property: JsonPropertyName("offeringId")] Guid PackageId, BriefDto Brief);

/// <summary><c>CheckoutUrl</c> is where the browser goes next: the mock checkout of this site, or the payment provider's page (Stripe Checkout) which returns to the site afterwards.</summary>
public sealed record CreateAccessResponseDto([property: JsonPropertyName("bookingId")] Guid AccessId, string Status, DateTimeOffset HoldExpiresAt, string CheckoutUrl);

public sealed record PaymentViewDto(string Status, MoneyDto Refunded);

/// <summary>
/// <c>BookingView</c> (CONTRACTS 4.4) as the detail, the checkout and the confirmation read it.
/// </summary>
public sealed record AccessDetailDto(Guid Id, string Status, string? StatusReason, DateTimeOffset StartUtc, DateTimeOffset EndUtc, int DurationMinutes, [property: JsonPropertyName("offeringTitle")] string PackageTitle, MoneyDto Price,
    [property: JsonPropertyName("practitioner")] AgentRefDto Agent, ClientRefDto? Client, BriefDto? Brief, string? MeetingUrl, DateTimeOffset? HoldExpiresAt, PaymentViewDto Payment, MoneyDto? Fee, MoneyDto? Payout,
    bool CanCancel, bool CanDispute, bool CanReportNoShow, bool CanReview, CancelOutcomeDto? CancelOutcome = null, string Kind = "Tasks", long UnitsTotal = 0, long UnitsUsed = 0);

/// <summary><c>cancelOutcome</c> (CONTRACTS 4.4, 9.2): what cancelling would do for the viewer right now, computed by the service on its clock. Present only when <c>canCancel</c>. <c>Outcome</c> is <c>full_refund</c>, <c>partial</c> or <c>no_refund</c>; <c>Refund</c> is what the client gets back.</summary>
public sealed record CancelOutcomeDto(string Outcome, MoneyDto Refund);

/// <summary><c>POST /api/v1/assistant/brief</c> (CONTRACTS 6). Only a suggestion: it is shown, never applied by itself.</summary>
public sealed record BriefSuggestionDto(string ProblemSlug, IReadOnlyList<string> TechSlugs, string Summary, IReadOnlyList<string> ClarifyingQuestions, int SuggestedDurationMinutes, string Confidence);

// ---- API keys and tasks (docs/agentic/PLAN.md B-01, B-05). The plain key is in <c>ApiKeyCreatedDto.Key</c> once, at creation; the list never carries it.

public sealed record ApiKeyDto(Guid Id, string Name, string Prefix, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt);

public sealed record ApiKeyCreatedDto(Guid Id, string Name, string Prefix, string Key, DateTimeOffset CreatedAt);

/// <summary>A task of a package (B-05). <c>Status</c> is <c>queued | running | succeeded | failed</c>; <c>Output</c> is text of the agent, shown as plain text and never as markup.</summary>
/// <remarks>Booking writes a task in snake case (<c>created_at</c>, <c>completed_at</c>, the shape of the OpenAI-style API of B-05), unlike the camel case of its other views.</remarks>
public sealed record AgentTaskDto(Guid Id, string Status, string? Output, [property: System.Text.Json.Serialization.JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: System.Text.Json.Serialization.JsonPropertyName("completed_at")] DateTimeOffset? CompletedAt);

public sealed record TaskCreatedDto(Guid Id, string Status);

public sealed record CreateReviewResponseDto(Guid ReviewId, string Status);

/// <summary>One agent of the signed-in provider account (E-04).</summary>
public sealed record MeAgentDto(Guid Id, string Slug, string DisplayName, string Status);

/// <summary><c>PractitionerId</c> and <c>PractitionerStatus</c> describe the account's first agent (the shell asks only whether there is one); <c>Agents</c> lists them all (E-04).</summary>
public sealed record MeDto(string UserId, [property: JsonPropertyName("practitionerId")] Guid? AgentId, [property: JsonPropertyName("practitionerStatus")] string? AgentStatus, bool IsAdmin, bool IsModerator, IReadOnlyList<MeAgentDto>? Agents = null);

// ---- the practitioner's own area (CONTRACTS 3.3, 3.5, 4.2; docs/operations/PAYMENTS-GO-LIVE.md)

/// <summary><c>GET /api/v1/me/practitioner</c>; <c>Status</c> is <c>Draft | Submitted | Approved | Rejected | Suspended</c>.</summary>
public sealed record AgentPrivateDto(Guid Id, string Slug, string Status, string? StatusReason, string DisplayName, string Headline, string Bio, string? PortfolioUrl,
    string? MeetingUrlDefault, string? Nip, bool EmailVerified, bool GithubLinked, bool NipValid, string? GithubLogin);

/// <summary>One row of <c>GET /api/v1/me/practitioners</c>: the provider's own agents, oldest first (E-04).</summary>
public sealed record AgentSummaryDto(Guid Id, string Slug, string DisplayName, string Headline, string Status, string? StatusReason);

public sealed record AgentSummariesDto(IReadOnlyList<AgentSummaryDto> Items);

public sealed record CreateAgentBody(string DisplayName);

/// <summary>An empty optional field is omitted, not sent as null (CONTRACTS 0): the attribute is on the body, not on the shared options.</summary>
public sealed record UpdateAgentBody(string DisplayName, string Headline, string Bio,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? PortfolioUrl,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? Nip);

public sealed record PackagePrivateDto(Guid Id, string Title, string Description, int DurationMinutes, MoneyDto Price, string ProblemSlug, IReadOnlyList<string> TechSlugs, bool IsActive, string? MeetingUrl, string Kind = "Tasks", int Units = 1);

public sealed record PackagesDto(IReadOnlyList<PackagePrivateDto> Items);

/// <summary><c>Kind</c> is <c>Tasks</c> or <c>Chat</c>; <c>Units</c> counts tasks, or thousands of tokens for <c>Chat</c>. The meeting link of the old model is not sent (docs/agentic/PLAN.md C-04).</summary>
public sealed record PackageBody(string Title, string Description, int DurationMinutes, long PriceMinor, string ProblemSlug, IReadOnlyList<string> TechSlugs, string Kind = "Tasks", int Units = 1);

public sealed record GithubLinkBody(string GithubLogin);

public sealed record GithubChallengeDto(string Code);

/// <summary><c>GET /api/v1/me/payments/account</c>: <c>State</c> is <c>none | pending | ready</c>. A provider with no connected accounts (the mock) answers 404.</summary>
public sealed record PaymentAccountDto(string Provider, string State);

/// <summary><c>POST /api/v1/me/payments/account/onboarding</c>: <c>OnboardingUrl</c> is single use and short lived; it is never stored or logged.</summary>
public sealed record PaymentOnboardingDto(string Provider, string State, string? OnboardingUrl);
