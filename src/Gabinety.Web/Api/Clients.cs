using System.Globalization;
using Gabinety.Web.Auth;

namespace Gabinety.Web.Api;

/// <summary>The filters of <c>GET /api/v1/practitioners</c> (CONTRACTS 3.2).</summary>
public sealed record AgentQuery(string? Problem = null, string? Tech = null, string? Q = null, int? Duration = null, long? MaxPriceMinor = null, string? Cursor = null);

/// <summary>Directory, public reads only (SPEC 5.1: 5 s, once, GET only: see <see cref="ApiResilience"/>).</summary>
public sealed partial class DirectoryClient(HttpClient http, VisitorAddressAccessor visitor) : ApiClient(http, visitor)
{
    public Task<ApiResult<TaxonomyDto>> TaxonomyAsync(CancellationToken ct = default) => GetAsync<TaxonomyDto>("/api/v1/taxonomy", null, ct);

    public Task<ApiResult<SitemapDto>> SitemapAsync(CancellationToken ct = default) => GetAsync<SitemapDto>("/api/v1/sitemap", null, ct);

    public Task<ApiResult<AgentProfileDto>> ProfileAsync(string slug, CancellationToken ct = default) =>
        GetAsync<AgentProfileDto>($"/api/v1/practitioners/{Uri.EscapeDataString(slug)}", null, ct);

    public Task<ApiResult<RankingRulesDto>> RankingRulesAsync(CancellationToken ct = default) => GetAsync<RankingRulesDto>("/api/v1/ranking-rules", null, ct);

    public Task<ApiResult<PageDto<AgentCardDto>>> AgentsAsync(AgentQuery query, CancellationToken ct = default)
    {
        var parts = new List<string>();
        void Add(string name, string? value) { if (!string.IsNullOrEmpty(value)) parts.Add($"{name}={Uri.EscapeDataString(value)}"); }
        Add("problem", query.Problem);
        Add("tech", query.Tech);
        Add("q", query.Q);
        Add("duration", query.Duration?.ToString(CultureInfo.InvariantCulture));
        Add("maxPriceMinor", query.MaxPriceMinor?.ToString(CultureInfo.InvariantCulture));
        Add("cursor", query.Cursor);
        return GetAsync<PageDto<AgentCardDto>>("/api/v1/practitioners" + (parts.Count == 0 ? "" : "?" + string.Join('&', parts)), null, ct);
    }
}

/// <summary>Booking, for the signed-in user: the bearer is read from the caller's scope (<see cref="IAccessTokenSource"/>) and handed to the handler through the request.</summary>
public sealed partial class AccessClient(HttpClient http, VisitorAddressAccessor visitor, IAccessTokenSource tokens) : ApiClient(http, visitor)
{
    public const string ClientRole = "client", AgentRole = "practitioner";

    public async Task<ApiResult<PageDto<AccessViewDto>>> MyAccessesAsync(string role, string? status, string? cursor, CancellationToken ct = default)
    {
        var parts = new List<string> { $"role={(role == AgentRole ? AgentRole : ClientRole)}" };
        if (!string.IsNullOrEmpty(status)) parts.Add($"status={Uri.EscapeDataString(status)}");
        if (!string.IsNullOrEmpty(cursor)) parts.Add($"cursor={Uri.EscapeDataString(cursor)}");
        return await GetAsync<PageDto<AccessViewDto>>("/api/v1/me/bookings?" + string.Join('&', parts), await tokens.AccessTokenAsync(ct), ct);
    }
}

/// <summary>Reputation, public reads.</summary>
public sealed partial class ReputationClient(HttpClient http, VisitorAddressAccessor visitor) : ApiClient(http, visitor)
{
    public Task<ApiResult<PageDto<ReviewDto>>> ReviewsAsync(Guid agentId, string? cursor = null, CancellationToken ct = default) =>
        GetAsync<PageDto<ReviewDto>>($"/api/v1/practitioners/{agentId}/reviews" + (string.IsNullOrEmpty(cursor) ? "" : "?cursor=" + Uri.EscapeDataString(cursor)), null, ct);
}

/// <summary>Assistant. The page keys off the gateway state (<see cref="AssistantStatusDto.State"/>), not off the health verdict: a working login that shows as unavailable is the recorded failure.</summary>
public sealed class AssistantClient(HttpClient http, VisitorAddressAccessor visitor) : ApiClient(http, visitor)
{
    public Task<ApiResult<AssistantStatusDto>> StatusAsync(CancellationToken ct = default) => GetAsync<AssistantStatusDto>("/api/v1/assistant/status", null, ct);
}
