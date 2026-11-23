using System.Security.Claims;
using System.Text.Json;
using Gabinety.Contracts.Directory;
using Gabinety.ServiceDefaults;

namespace Gabinety.Booking.Features.AgentApi;

/// <summary>
/// <c>GET /v1/models</c> (docs/agentic/PLAN.md B-04): the agents the key's client has a usable <c>Chat</c> package for, in the OpenAI list shape, so a client such as GitHub Copilot
/// BYOK can offer them as models. The <c>id</c> is the agent slug.
/// </summary>
public sealed class ModelsFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/v1/models", ListAsync).RequireAuthorization(ApiKeyAuthentication.PolicyName);

    static async Task<IResult> ListAsync(ClaimsPrincipal user, EntitlementStore entitlements, CancellationToken ct)
    {
        var slugs = await entitlements.UsableSlugsAsync(user.FindFirst("sub")!.Value, OfferingKind.Chat, ct);
        var body = JsonSerializer.Serialize(new
        {
            @object = "list",
            data = slugs.Select(s => new { id = s, @object = "model", owned_by = "gabinety" }),
        });
        return Results.Content(body, "application/json");
    }
}
