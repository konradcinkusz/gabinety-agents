using Gabinety.Contracts.Common;
using Gabinety.Directory.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Directory.Features.Profiles;

/// <summary>
/// Which of the caller's agents a <c>/api/v1/me/practitioner...</c> call is about (E-04). One provider account runs several agents, so a call names its agent with the
/// <c>agent</c> query parameter (the slug). An account with exactly one agent may leave it out (the behaviour from before E-04); with more than one the parameter is required
/// (422 <c>validation_failed</c> on <c>agent</c>), because silently picking one could edit the wrong agent. Another account's slug is 404, like an unknown one.
/// </summary>
public static class OwnAgents
{
    public const string Query = "agent";
    public const int DefaultMax = 10;

    /// <summary>Upper bound of agents per provider account: <c>Directory:MaxAgentsPerProvider</c>, default 10; a value below 1 falls back to the default.</summary>
    public static int Max(IConfiguration config) => config.GetValue<int?>("Directory:MaxAgentsPerProvider") is > 0 and var n ? n : DefaultMax;

    public static IResult? Reject(int count, string? slug) => count switch
    {
        0 => Problem(404, ErrorCodes.NotFound, "Not found", null),
        > 1 when string.IsNullOrWhiteSpace(slug) => Problem(422, ErrorCodes.ValidationFailed, "Validation failed",
            new() { [Query] = ["required when the account has more than one agent"] }),
        _ => null,
    };

    /// <summary>The caller's agent for this call tracked for update, or the answer to send.</summary>
    public static async Task<(Agent? Agent, IResult? Rejected)> FindAsync(DirectoryDbContext db, string userId, string? slug, CancellationToken ct)
    {
        slug = string.IsNullOrWhiteSpace(slug) ? null : slug.Trim();
        var q = db.Agents.Where(x => x.UserId == userId);
        if (slug is not null) q = q.Where(x => x.Slug == slug);
        var rows = await q.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(2).ToListAsync(ct);
        var rejected = Reject(rows.Count, slug);
        return rejected is null ? (rows[0], null) : (null, rejected);
    }

    public static IResult Problem(int status, string code, string title, Dictionary<string, string[]>? errors) =>
        Results.Json(ApiProblem.Of(code, title, status, null, errors), statusCode: status, contentType: "application/problem+json");
}
