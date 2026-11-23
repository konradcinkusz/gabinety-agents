using System.Security.Claims;
using System.Text.Encodings.Web;
using Gabinety.Booking.Features.ApiKeys;
using Gabinety.Booking.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Gabinety.Booking.Features.AgentApi;

/// <summary>
/// The <c>ApiKey</c> scheme (docs/agentic/PLAN.md B-02): <c>Authorization: Bearer gab_...</c> is hashed (SHA-256) and looked up among the keys that are not revoked.
/// A hit signs the caller in with the claims <c>sub</c> (the key's client) and <c>api_key_id</c>. Anything else is "no result", and the challenge answers 401
/// <c>invalid_api_key</c> in the OpenAI error shape. The key is never logged and never put in a message. <c>LastUsedAt</c> moves at most once a minute.
/// </summary>
public static class ApiKeyAuthentication
{
    public const string SchemeName = "ApiKey", PolicyName = "ApiKey", KeyIdClaim = "api_key_id";
    public static readonly TimeSpan LastUsedGranularity = TimeSpan.FromMinutes(1);

    public static AuthenticationBuilder AddApiKeyAuthentication(this AuthenticationBuilder auth) =>
        auth.AddScheme<AuthenticationSchemeOptions, ApiKeyHandler>(SchemeName, null);

    /// <summary>True for the paths of the public agent API.</summary>
    public static bool IsAgentApi(PathString path) => path.StartsWithSegments("/v1", StringComparison.OrdinalIgnoreCase);

    /// <summary>The user id the limiter sees for a key: the limit of the public API is per API key, not per client (docs/agentic/PLAN.md B-02).</summary>
    public const string LimiterIdPrefix = "apikey:";

    /// <summary>
    /// For <c>/v1</c> requests, before the rate limiter: authenticates the <c>ApiKey</c> scheme and, on a hit, shows the limiter a principal whose <c>sub</c> is the id of the key, so
    /// the kernel limiter (which partitions by <c>sub</c>) holds each key to its own window. Authorization later replaces this principal with the real one (<c>sub</c> = the client,
    /// <c>api_key_id</c>); the handler caches its answer for the request, so the key is looked up once. Nothing of the kernel changes.
    /// </summary>
    public static IApplicationBuilder UseApiKeyRateLimitIdentity(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        if (IsAgentApi(ctx.Request.Path))
        {
            var result = await ctx.AuthenticateAsync(SchemeName);
            if (result.Succeeded)
                ctx.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", LimiterIdPrefix + result.Principal!.FindFirst(KeyIdClaim)!.Value)], SchemeName));
        }
        await next();
    });

    /// <summary>The refusal of the limiter on <c>/v1</c> is 429 <c>rate_limit_exceeded</c> in the OpenAI error shape, with <c>Retry-After</c>; every other path keeps the kernel's answer.</summary>
    public static IServiceCollection AddOpenAiRateLimitRefusal(this IServiceCollection services) =>
        services.AddOptions<RateLimiterOptions>().PostConfigure(o =>
        {
            var kernel = o.OnRejected;
            o.OnRejected = async (context, ct) =>
            {
                if (!IsAgentApi(context.HttpContext.Request.Path))
                {
                    if (kernel is not null) await kernel(context, ct);
                    return;
                }
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
                    context.HttpContext.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                await OpenAiError.WriteAsync(context.HttpContext.Response, 429, "Too many requests.", "rate_limit_error", OpenAiError.RateLimitExceeded, ct);
            };
        }).Services;

    sealed class ApiKeyHandler(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e, BookingDbContext db, TimeProvider time)
        : AuthenticationHandler<AuthenticationSchemeOptions>(o, l, e)
    {
        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            const string bearer = "Bearer ";
            if (!header.StartsWith(bearer, StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();
            var key = header[bearer.Length..].Trim();
            if (!key.StartsWith(ApiKeyHasher.KeyPrefix, StringComparison.Ordinal) || key.Length != ApiKeyHasher.KeyPrefix.Length + ApiKeyHasher.RandomLength)
                return AuthenticateResult.NoResult();

            var hash = ApiKeyHasher.Hash(key);
            var row = await db.ApiKeys.SingleOrDefaultAsync(k => k.Hash == hash && k.RevokedAt == null, Context.RequestAborted);
            if (row is null) return AuthenticateResult.NoResult();

            var now = time.GetUtcNow();
            if (row.LastUsedAt is null || now - row.LastUsedAt >= LastUsedGranularity)
            {
                row.LastUsedAt = now;
                await db.SaveChangesAsync(Context.RequestAborted);
            }

            var id = new ClaimsIdentity(SchemeName);
            id.AddClaim(new Claim("sub", row.ClientUserId));
            id.AddClaim(new Claim(KeyIdClaim, row.Id.ToString()));
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(id), SchemeName));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.Headers.WWWAuthenticate = "Bearer";
            return OpenAiError.WriteAsync(Response, 401, "Incorrect API key provided.", OpenAiError.InvalidRequest, OpenAiError.InvalidApiKey, Context.RequestAborted);
        }

        protected override Task HandleForbiddenAsync(AuthenticationProperties properties) => HandleChallengeAsync(properties);
    }
}
