using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gabinety.Billing;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Common;
using Gabinety.Contracts.Directory;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using AccessEntity = Gabinety.Booking.Features.Bookings.Entities.Access;

namespace Gabinety.Booking.Features.AgentApi;

/// <summary>
/// Tasks of the <c>Tasks</c> packages (docs/agentic/PLAN.md B-05). A task is ordered with <c>POST /v1/tasks</c> (API key) or, from the browser, with
/// <c>POST /api/v1/bookings/{id}/tasks</c>; it takes one unit at once (never past the total) and waits in <c>Queued</c> for <see cref="Jobs.AgentTasks.AgentTaskJob"/>.
/// The owner reads it with <c>GET /v1/tasks/{id}</c>, <c>GET /api/v1/me/tasks/{id}</c> or lists a package's tasks with <c>GET /api/v1/me/bookings/{id}/tasks</c>; another user's task is 404.
/// The text of a task and of its answer is user text: it is stored and returned to the owner, and never logged.
/// </summary>
public sealed class TasksFeature : IServiceFeature
{
    public const int MinInputLength = 20, MaxInputLength = 20000;

    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/v1/tasks").RequireAuthorization(ApiKeyAuthentication.PolicyName);
        api.MapPost("", CreateByKeyAsync);
        api.MapGet("{id:guid}", GetByKeyAsync);

        var me = endpoints.MapGroup("/api/v1").RequireAuthorization("Authenticated");
        me.MapPost("bookings/{id:guid}/tasks", CreateForAccessAsync);
        me.MapGet("me/tasks/{id:guid}", GetOwnAsync);
        me.MapGet("me/bookings/{bookingId:guid}/tasks", ListForAccessAsync);
    }

    sealed record CreateTaskBody([property: JsonPropertyName("model")] string? Model, [property: JsonPropertyName("offering_id")] string? PackageId, [property: JsonPropertyName("input")] string? Input);
    sealed record BrowserTaskBody([property: JsonPropertyName("input")] string? Input);

    static string Sub(ClaimsPrincipal user) => (user.FindFirst("sub") ?? user.FindFirst(ClaimTypes.NameIdentifier))?.Value ?? "";

    /// <summary>The shape of a task in every read: <c>status</c> is lowercase; <c>output</c> is null until the task succeeded.</summary>
    static object View(AgentTask t) => new
    {
        id = t.Id, status = t.Status.ToString().ToLowerInvariant(), output = t.Status == AgentTaskStatus.Succeeded ? t.OutputText : null,
        created_at = Iso(t.CreatedAt), completed_at = t.CompletedAt is { } c ? Iso(c) : null,
    };

    static string Iso(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", System.Globalization.CultureInfo.InvariantCulture);

    static IResult Json(object o, int status = 200) => Results.Content(JsonSerializer.Serialize(o), "application/json", System.Text.Encoding.UTF8, status);

    static bool ValidInput(string? input) => input is not null && input.Length is >= MinInputLength and <= MaxInputLength;

    // ---- POST /v1/tasks (API key)

    static async Task<IResult> CreateByKeyAsync(CreateTaskBody? body, ClaimsPrincipal user, EntitlementStore entitlements, AccessDbContext db, TimeProvider time,
        ILogger<TasksFeature> log, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body?.Model)) return OpenAiError.BadRequest("The model field is required.");
        if (!Guid.TryParse(body.PackageId, out var packageId)) return OpenAiError.BadRequest("The offering_id field must be the id of a package.");
        if (!ValidInput(body.Input)) return OpenAiError.BadRequest($"The input must be {MinInputLength} to {MaxInputLength} characters.");

        var sub = Sub(user);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var package = await entitlements.FindAsync(sub, body.Model, PackageKind.Tasks, packageId, ct);
            if (package is null)
                return await entitlements.HasExhaustedAsync(sub, body.Model, PackageKind.Tasks, packageId, ct) ? OpenAiError.QuotaExhausted() : OpenAiError.ModelNotFound_();
            if (!await entitlements.TryConsumeAsync(package.Id, 1, ct)) continue; // another request took the last unit between the find and the take: look again
            var task = await QueueAsync(package, body.Input!, ApiKeyId(user), entitlements, db, time, log, ct);
            return Json(new { id = task.Id, status = "queued" }, 202);
        }
        return OpenAiError.QuotaExhausted();
    }

    static Guid? ApiKeyId(ClaimsPrincipal user) => Guid.TryParse(user.FindFirst(ApiKeyAuthentication.KeyIdClaim)?.Value, out var id) ? id : null;

    /// <summary>Saves the task of a unit that is already taken; when it cannot be saved the unit goes back.</summary>
    static async Task<AgentTask> QueueAsync(AccessEntity package, string input, Guid? apiKeyId, EntitlementStore entitlements, AccessDbContext db, TimeProvider time,
        ILogger log, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var task = new AgentTask
        {
            Id = Guid.NewGuid(), AccessId = package.Id, ClientUserId = package.ClientUserId, AgentSlug = package.AgentSlug, PackageId = package.PackageId,
            Status = AgentTaskStatus.Queued, InputText = input, CreatedAt = now,
        };
        db.AgentTasks.Add(task);
        db.UsageRecords.Add(new UsageRecord { Id = Guid.NewGuid(), AccessId = package.Id, ApiKeyId = apiKeyId, Kind = PackageKind.Tasks, Units = 1, CreatedAt = now });
        try { await db.SaveChangesAsync(CancellationToken.None); }
        catch
        {
            await entitlements.RefundUnitsAsync(package.Id, 1, CancellationToken.None);
            throw;
        }
        log.LogInformation("Task {TaskId} queued for package {BookingId} agent {AgentSlug}", task.Id, package.Id, package.AgentSlug);
        return task;
    }

    // ---- POST /api/v1/bookings/{id}/tasks (browser)

    static async Task<IResult> CreateForAccessAsync(Guid id, BrowserTaskBody? body, ClaimsPrincipal user, EntitlementStore entitlements, AccessDbContext db, TimeProvider time,
        ILogger<TasksFeature> log, CancellationToken ct)
    {
        if (!ValidInput(body?.Input))
            return PaymentWebhookFeature.Problem(ErrorCodes.ValidationFailed, "Validation failed", 422, null,
                new() { ["input"] = [$"The input must be {MinInputLength} to {MaxInputLength} characters."] });

        var sub = Sub(user);
        var package = await db.Accesses.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id && b.ClientUserId == sub, ct);
        if (package is null) return PaymentWebhookFeature.Problem(ErrorCodes.NotFound, "Not found", 404);
        var now = time.GetUtcNow();
        if (package.Kind != PackageKind.Tasks || package.Status != AccessStatus.Confirmed || package.EndUtc <= now || package.UnitsUsed >= package.UnitsTotal)
            return PaymentWebhookFeature.Problem(ErrorCodes.InvalidTransition, "Invalid transition", 409);
        if (!await entitlements.TryConsumeAsync(package.Id, 1, ct)) return PaymentWebhookFeature.Problem(ErrorCodes.InvalidTransition, "Invalid transition", 409);

        var task = await QueueAsync(package, body!.Input!, null, entitlements, db, time, log, ct);
        return Json(new { id = task.Id, status = "queued" }, 202);
    }

    // ---- reads

    static async Task<IResult> GetByKeyAsync(Guid id, ClaimsPrincipal user, AccessDbContext db, CancellationToken ct)
    {
        var sub = Sub(user);
        var task = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id && t.ClientUserId == sub, ct);
        return task is null ? OpenAiError.Result(404, "The task does not exist.", OpenAiError.InvalidRequest, "task_not_found") : Json(View(task));
    }

    static async Task<IResult> GetOwnAsync(Guid id, ClaimsPrincipal user, AccessDbContext db, CancellationToken ct)
    {
        var sub = Sub(user);
        var task = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id && t.ClientUserId == sub, ct);
        return task is null ? PaymentWebhookFeature.Problem(ErrorCodes.NotFound, "Not found", 404) : Json(View(task));
    }

    static async Task<IResult> ListForAccessAsync([Microsoft.AspNetCore.Mvc.FromRoute(Name = "bookingId")] Guid accessId, ClaimsPrincipal user, AccessDbContext db, CancellationToken ct)
    {
        var sub = Sub(user);
        if (!await db.Accesses.AnyAsync(b => b.Id == accessId && b.ClientUserId == sub, ct)) return PaymentWebhookFeature.Problem(ErrorCodes.NotFound, "Not found", 404);
        var tasks = await db.AgentTasks.AsNoTracking().Where(t => t.AccessId == accessId && t.ClientUserId == sub).OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id).ToListAsync(ct);
        return Json(tasks.Select(View).ToList());
    }
}
