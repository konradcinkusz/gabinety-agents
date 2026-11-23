using Gabinety.Booking;
using Gabinety.Booking.Features.AgentApi;
using Gabinety.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddFeatures();
builder.Services.AddRateLimiting(builder.Configuration).AddOpenAiRateLimitRefusal();

// Authentication is registered here and only here: Me no longer calls it.
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddAuthentication().AddInternalKeyAuthentication().AddApiKeyAuthentication();
builder.Services.AddInternalKeyValidation(); // F-03: strong, distinct internal keys outside the fake profile
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Authenticated", p => p.RequireAuthenticatedUser())
    .AddPolicy("Admin", p => p.RequireRole("Admin", "SuperAdmin")) // authservice roles; the claim is the WS-Federation role URI
    .AddPolicy(ApiKeyAuthentication.PolicyName, p => p.AddAuthenticationSchemes(ApiKeyAuthentication.SchemeName).RequireAuthenticatedUser()) // the public /v1 API: only a gab_ key, 401 in the OpenAI shape
    .AddPolicy("Internal", p => p.AddAuthenticationSchemes(Security.InternalScheme).RequireAuthenticatedUser())
    .RequireAuthenticatedByDefault(); // deny by default: a public route must say AllowAnonymous() (docs/operations/ROUTE-AUTH.md)
builder.Services.AddOpenApi();

var app = builder.Build();
app.UsePublicEdgeGuard(PublicEdge.Serves); // F-03: Booking is the one public app; the edge sees only what PublicEdge.Serves allows
app.UseOriginGuard();
app.UseAuthentication();
app.UseApiKeyRateLimitIdentity(); // /v1 only: the limiter partitions by user id, so it is shown the id of the key
app.UseRateLimiter(); // after authentication: the limit is per user id when signed in (CONTRACTS 10.2)
app.UseAuthorization();
app.MapDefaultEndpoints();
app.MapFeatures();
if (OpenApiExposure.IsServed(app.Configuration)) app.MapOpenApi("/openapi/v1.json").AllowAnonymous(); // F-07: not served when deployed
app.Run();

public partial class Program;
