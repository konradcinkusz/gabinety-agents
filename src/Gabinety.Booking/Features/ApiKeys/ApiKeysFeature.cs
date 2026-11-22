using System.Security.Claims;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Booking;
using Gabinety.Contracts.Common;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Booking.Features.ApiKeys;

/// <summary>
/// Own API keys of the signed-in client (docs/agentic/PLAN.md B-01). <c>POST</c> returns the plain key once; the list never does. A key that is not the
/// caller's is 404 on <c>DELETE</c>. At most <see cref="MaxActive"/> keys are active at a time. The name is user free text: stored, never logged.
/// </summary>
public sealed class ApiKeysFeature : IServiceFeature
{
    public const int MaxActive = 5;
    public const int MaxNameLength = 60;

    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints)
    {
        var g = endpoints.MapGroup("/api/v1/me/api-keys").RequireAuthorization("Authenticated");
        g.MapPost("", CreateAsync);
        g.MapGet("", ListAsync);
        g.MapDelete("{id:guid}", RevokeAsync);
    }

    static string? Sub(ClaimsPrincipal user) => user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    static async Task<IResult> CreateAsync(CreateApiKeyRequest? body, ClaimsPrincipal user, BookingDbContext db, TimeProvider time, CancellationToken ct)
    {
        var sub = Sub(user);
        if (sub is null) return Results.Unauthorized();

        var name = body?.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
            return PaymentWebhookFeature.Problem(ErrorCodes.ValidationFailed, "Validation failed", 422, null,
                new() { ["name"] = [$"The name is required and at most {MaxNameLength} characters."] });

        var active = await db.ApiKeys.CountAsync(k => k.ClientUserId == sub && k.RevokedAt == null, ct);
        if (active >= MaxActive)
            return PaymentWebhookFeature.Problem(ErrorCodes.ValidationFailed, "Validation failed", 422, null,
                new() { ["name"] = [$"At most {MaxActive} active keys; revoke one first."] });

        var key = ApiKeyHasher.Generate();
        var row = new ApiKey
        {
            Id = Guid.NewGuid(), ClientUserId = sub, Name = name, Prefix = ApiKeyHasher.PrefixOf(key), Hash = ApiKeyHasher.Hash(key), CreatedAt = time.GetUtcNow(),
        };
        db.ApiKeys.Add(row);
        await db.SaveChangesAsync(ct);
        return Results.Json(new ApiKeyCreatedDto(row.Id, row.Name, row.Prefix, key, row.CreatedAt), statusCode: 201);
    }

    static async Task<IResult> ListAsync(ClaimsPrincipal user, BookingDbContext db, CancellationToken ct)
    {
        var sub = Sub(user);
        if (sub is null) return Results.Unauthorized();
        var rows = await db.ApiKeys.AsNoTracking().Where(k => k.ClientUserId == sub && k.RevokedAt == null).OrderBy(k => k.CreatedAt).ToListAsync(ct);
        return Results.Ok(rows.Select(k => new ApiKeyDto(k.Id, k.Name, k.Prefix, k.CreatedAt, k.LastUsedAt)).ToList());
    }

    static async Task<IResult> RevokeAsync(Guid id, ClaimsPrincipal user, BookingDbContext db, TimeProvider time, CancellationToken ct)
    {
        var sub = Sub(user);
        if (sub is null) return Results.Unauthorized();
        var row = await db.ApiKeys.SingleOrDefaultAsync(k => k.Id == id && k.ClientUserId == sub, ct);
        if (row is null) return PaymentWebhookFeature.Problem(ErrorCodes.NotFound, "Not found", 404);
        if (row.RevokedAt is null)
        {
            row.RevokedAt = time.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }
        return Results.NoContent();
    }
}
