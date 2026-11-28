using System.Security.Claims;
using Gabinety.Contracts.Directory;
using Gabinety.Directory.Persistence;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Directory.Features.Me;

/// <summary>
/// GET /api/v1/me behind JWT validation against authservice's JWKS (CONTRACTS 3.3). Since E-04 it also answers which agents the account owns: <c>agents</c> lists them, oldest
/// first, and <c>practitionerId</c> / <c>practitionerStatus</c> describe the first one (null for an account with none), so a client written for one agent per account is unchanged.
/// </summary>
public sealed class MeFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder) { } // JWT authentication is registered once, in Program.cs

    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v1/me", async (ClaimsPrincipal user, DirectoryDbContext db, CancellationToken ct) =>
        {
            // "sub" stays "sub" or is mapped to NameIdentifier, depending on the handler's inbound mapping.
            var userId = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userId is null) return Results.Unauthorized();
            var rows = await db.Practitioners.AsNoTracking().Where(p => p.UserId == userId).OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).ToListAsync(ct); // order, then project
            var agents = rows.Select(p => new MeAgent(p.Id, p.Slug, p.DisplayName, p.Status)).ToList();
            return Results.Ok(new MeDto(userId, agents.Count > 0 ? agents[0].Id : null, agents.Count > 0 ? agents[0].Status : null,
                user.IsInRole("Admin") || user.IsInRole("SuperAdmin"), false, agents));
        }).RequireAuthorization();
}
