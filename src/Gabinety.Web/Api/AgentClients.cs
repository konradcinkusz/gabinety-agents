using Gabinety.Web.Auth;

namespace Gabinety.Web.Api;

/// <summary>
/// Directory, the practitioner's own profile and offers (CONTRACTS 3.3, 3.5). Every call carries the user's bearer; the service decides who may do what. One provider account may own several
/// agents (E-04): <see cref="Agent"/> is the slug of the agent the page works on, sent as <c>?agent=</c> on every call except the creation of a new agent; null means "the account's only agent".
/// </summary>
public sealed class AgentClient(HttpClient http, VisitorAddressAccessor visitor, IAccessTokenSource tokens) : ApiClient(http, visitor)
{
    const string Me = "/api/v1/me/practitioner";

    /// <summary>The slug of the agent the calls are about (E-04); null for an account with one agent.</summary>
    public string? Agent { get; set; }

    string Scoped(string path) => string.IsNullOrEmpty(Agent) ? path : $"{path}{(path.Contains('?') ? '&' : '?')}agent={Uri.EscapeDataString(Agent)}";

    async Task<ApiResult<T>> Get<T>(string path, CancellationToken ct) => await GetAsync<T>(Scoped(path), await tokens.AccessTokenAsync(ct), ct);

    async Task<ApiResult<T>> Write<T>(HttpMethod method, string path, object? body, CancellationToken ct) => await WriteAsync<T>(method, Scoped(path), await tokens.AccessTokenAsync(ct), body, ct);

    /// <summary>404 means the account has no practitioner profile yet.</summary>
    public Task<ApiResult<AgentPrivateDto>> ProfileAsync(CancellationToken ct = default) => Get<AgentPrivateDto>(Me, ct);

    /// <summary>Creates a NEW agent of the account (E-04: not scoped to <see cref="Agent"/>); 422 <c>too_many_agents</c> at the account's limit.</summary>
    public async Task<ApiResult<AgentPrivateDto>> CreateProfileAsync(string displayName, CancellationToken ct = default) =>
        await WriteAsync<AgentPrivateDto>(HttpMethod.Post, Me, await tokens.AccessTokenAsync(ct), new CreateAgentBody(displayName), ct);

    /// <summary>The account's agents, oldest first (E-04); an empty list when it has none.</summary>
    public async Task<ApiResult<IReadOnlyList<AgentSummaryDto>>> AgentsAsync(CancellationToken ct = default)
    {
        var r = await GetAsync<AgentSummariesDto>("/api/v1/me/practitioners", await tokens.AccessTokenAsync(ct), ct);
        return r.IsOk ? ApiResult<IReadOnlyList<AgentSummaryDto>>.Ok(r.Value!.Items) : ApiResult<IReadOnlyList<AgentSummaryDto>>.Fail(r.Failure!);
    }

    public Task<ApiResult<AgentPrivateDto>> SaveProfileAsync(UpdateAgentBody body, CancellationToken ct = default) => Write<AgentPrivateDto>(HttpMethod.Put, Me, body, ct);

    public Task<ApiResult<Unit>> SubmitAsync(CancellationToken ct = default) => Write<Unit>(HttpMethod.Post, Me + "/submit", null, ct);

    public Task<ApiResult<GithubChallengeDto>> RequestGithubLinkAsync(string githubLogin, CancellationToken ct = default) =>
        Write<GithubChallengeDto>(HttpMethod.Post, Me + "/github-link", new GithubLinkBody(githubLogin), ct);

    public Task<ApiResult<Unit>> VerifyGithubLinkAsync(CancellationToken ct = default) => Write<Unit>(HttpMethod.Post, Me + "/github-link/verify", null, ct);

    public async Task<ApiResult<IReadOnlyList<PackagePrivateDto>>> PackagesAsync(CancellationToken ct = default)
    {
        var r = await Get<PackagesDto>(Me + "/offerings", ct);
        return r.IsOk ? ApiResult<IReadOnlyList<PackagePrivateDto>>.Ok(r.Value!.Items) : ApiResult<IReadOnlyList<PackagePrivateDto>>.Fail(r.Failure!);
    }

    /// <summary>Creates the offer (<paramref name="id"/> null) or replaces it.</summary>
    public Task<ApiResult<PackagePrivateDto>> SavePackageAsync(Guid? id, PackageBody body, CancellationToken ct = default) =>
        Write<PackagePrivateDto>(id is null ? HttpMethod.Post : HttpMethod.Put, Me + "/offerings" + (id is null ? "" : "/" + id), body, ct);

    public Task<ApiResult<Unit>> DeactivatePackageAsync(Guid id, CancellationToken ct = default) => Write<Unit>(HttpMethod.Post, $"{Me}/offerings/{id}/deactivate", null, ct);

    public Task<ApiResult<Unit>> ActivatePackageAsync(Guid id, CancellationToken ct = default) => Write<Unit>(HttpMethod.Post, $"{Me}/offerings/{id}/activate", null, ct);
}

/// <summary>Booking, the provider's payments account (PAYMENTS-GO-LIVE).</summary>
public sealed class AgentSchedulingClient(HttpClient http, VisitorAddressAccessor visitor, IAccessTokenSource tokens) : ApiClient(http, visitor)
{
    /// <summary>The slug of the agent whose payment account the calls are about (E-04: the account is per agent); null for a provider with one agent.</summary>
    public string? Agent { get; set; }

    string Scoped(string path) => string.IsNullOrEmpty(Agent) ? path : $"{path}?agent={Uri.EscapeDataString(Agent)}";

    async Task<ApiResult<T>> Get<T>(string path, CancellationToken ct) => await GetAsync<T>(Scoped(path), await tokens.AccessTokenAsync(ct), ct);

    /// <summary>404 means the payment provider has no connected accounts (the mock of the fake profile): nothing to set up.</summary>
    public Task<ApiResult<PaymentAccountDto>> PaymentAccountAsync(CancellationToken ct = default) => Get<PaymentAccountDto>("/api/v1/me/payments/account", ct);

    public async Task<ApiResult<PaymentOnboardingDto>> StartPaymentOnboardingAsync(CancellationToken ct = default) =>
        await WriteAsync<PaymentOnboardingDto>(HttpMethod.Post, Scoped("/api/v1/me/payments/account/onboarding"), await tokens.AccessTokenAsync(ct), null, ct);
}
