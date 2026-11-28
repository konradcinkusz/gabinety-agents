using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gabinety.Billing;
using Gabinety.Booking.Clients;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Notifications;
using Gabinety.Booking.Payments.Abstractions;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Booking;
using Gabinety.Contracts.Common;
using Gabinety.Contracts.Directory;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using AccessEntity = Gabinety.Booking.Features.Bookings.Entities.Access;
using ContractsStatus = Gabinety.Contracts.Booking.AccessStatus;

namespace Gabinety.Booking.Features.Bookings.Create;

/// <summary>
/// POST /api/v1/bookings (CONTRACTS 4.2, idempotency CONTRACTS 0): buys a package (A-02), no slot. The brief is user free text: it is stored in the booking row only and
/// never reaches a log, an exception message, a metric, the idempotency record or any response.
/// Not part of this task: the per-user rate limit of CONTRACTS 10.2 (429 rate_limited).
/// </summary>
public sealed class CreateAccessFeature : IServiceFeature
{
    public const string IdempotencyHeader = "Idempotency-Key";
    public static readonly TimeSpan IdempotencyWindow = TimeSpan.FromHours(24);
    const string UniqueViolation = "23505";
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/api/v1/bookings", HandleAsync).RequireAuthorization("Authenticated");

    static async Task<IResult> HandleAsync(HttpContext http, ClaimsPrincipal user, AccessDbContext db, IDirectoryClient directory,
        IPaymentProvider payments, TimeProvider time, IConfiguration config, ILogger<CreateAccessFeature> log, CancellationToken ct)
    {
        var sub = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (sub is null) return Results.Unauthorized();
        var clientName = PartyNamesLookup.ClientSnapshot(user.FindFirst(ClaimTypes.Name)?.Value ?? user.FindFirst("name")?.Value);

        // 1. header and body
        if (!Guid.TryParse(http.Request.Headers[IdempotencyHeader].ToString(), out var key))
            return Problem(ErrorCodes.ValidationFailed, "Validation failed", 400, new() { [IdempotencyHeader] = ["Idempotency-Key is required and must be a uuid"] });
        var (req, errors) = await ReadBodyAsync(http.Request, ct);
        if (req is null) return Problem(ErrorCodes.ValidationFailed, "Validation failed", 400, errors);
        if (errors.Count > 0) return Problem(ErrorCodes.ValidationFailed, "Validation failed", 422, errors);
        var text = req.Brief.Text.Trim();
        var hash = Hash(req, text);

        // 2. idempotency
        var now = time.GetUtcNow();
        if (await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(x => x.ClientUserId == sub && x.IdempotencyKey == key, ct) is { } seen)
            return Replay(seen, hash, now);

        // 3. the offering
        DirectoryPackage? package;
        try { package = await directory.GetPackageAsync(req.PackageId, ct); }
        catch (DirectoryUnavailableException)
        {
            return Problem(ErrorCodes.DependencyUnavailable, "Dependency unavailable", 503, null, "A required service is temporarily unavailable.");
        }
        if (package is null) return Problem(ErrorCodes.NotFound, "Not found", 404);
        if (package.AgentUserId == sub) return Problem(ErrorCodes.OwnPackage, "Own offering", 422);
        if (package.AgentStatus != AgentStatus.Approved) return Problem(ErrorCodes.AgentNotBookable, "Practitioner not bookable", 422);
        if (!package.IsActive) return Problem(ErrorCodes.PackageInactive, "Offering inactive", 422);
        // A real provider pays the practitioner into a connected account: without a ready one the booking would be charged and could never be paid out (T-09.05). Mock needs none.
        if (payments.RequiresAgentAccount
            && !await db.PaymentAccounts.AsNoTracking().AnyAsync(a => a.AgentId == package.AgentId && a.Provider == payments.Name && a.Ready, ct))
            return Problem(ErrorCodes.AgentPaymentsNotReady, "Practitioner payments not ready", 422);

        // The provider call (a network call with a real vendor, SPEC 5.1: 10 s) happens BEFORE the transaction, with a
        // pre-allocated booking id, so no database lock is held across it. A request that then fails (a replay won the race) leaves an unused
        // checkout at the provider: a real adapter must make checkouts expire by themselves (the hold is 10 minutes).
        var accessId = Guid.CreateVersion7();
        var checkout = await payments.CreateCheckoutAsync(new CheckoutRequest(accessId, package.Price.AmountMinor, package.Price.Currency,
            package.Title, $"{SiteUrl.From(config)}/bookings/{accessId}"), ct);

        var holdUntil = now.AddMinutes(config.GetValue("Booking:HoldMinutes", 10));
        // A package is valid from the purchase for Access:ValidityDays (CONTRACTS section 9 constant, the owner's). Chat counts tokens: the offering holds thousands.
        var validUntil = now.AddDays(config.GetValue("Access:ValidityDays", 30));
        var unitsTotal = package.Kind == PackageKind.Chat ? package.Units * 1000L : package.Units;

        try
        {
            return await InTransaction(db, key, async () =>
            {
                // A concurrent request with the same key may have committed while this one waited for the lock: answer with ITS stored response.
                if (await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(x => x.ClientUserId == sub && x.IdempotencyKey == key, ct) is { } won)
                    return Replay(won, hash, now);

                var response = new CreateAccessResponse(accessId, Enum.Parse<ContractsStatus>(AccessStateMachine.Create().ToString()), holdUntil, checkout.CheckoutUrl);
                var body = JsonSerializer.Serialize(response, Json);
                db.Accesses.Add(new AccessEntity
                {
                    Id = accessId, ClientUserId = sub, ClientEmail = UserEmail.From(user), ClientDisplayName = clientName,
                    AgentId = package.AgentId, AgentUserId = package.AgentUserId,
                    PackageId = package.PackageId, PackageTitle = package.Title, DurationMinutes = (short)package.DurationMinutes,
                    PriceMinor = package.Price.AmountMinor, StartUtc = now, EndUtc = validUntil, Kind = package.Kind, UnitsTotal = unitsTotal, UnitsUsed = 0,
                    AgentSlug = package.AgentSlug, Status = AccessStateMachine.Create(),
                    BriefText = text, BriefProblemSlug = req.Brief.ProblemSlug, BriefAiAssisted = req.Brief.AiAssisted,
                    HoldExpiresAt = holdUntil, IdempotencyKey = key, CreatedAt = now,
                });
                db.Payments.Add(new Payment
                {
                    Id = Guid.CreateVersion7(), AccessId = accessId, Provider = payments.Name, ProviderPaymentId = checkout.ProviderPaymentId,
                    AmountMinor = package.Price.AmountMinor, Status = PaymentStatus.Created, RefundedMinor = 0, CreatedAt = now, UpdatedAt = now,
                });
                db.IdempotencyRecords.Add(new IdempotencyRecord
                    { ClientUserId = sub, IdempotencyKey = key, RequestHash = hash, ResponseStatus = 201, ResponseBody = body, CreatedAt = now });
                await db.SaveChangesAsync(ct);

                log.LogInformation("Booking {BookingId} created for practitioner {PractitionerId}", accessId, package.AgentId);
                http.Response.Headers.Location = $"/api/v1/bookings/{accessId}";
                return Results.Text(body, "application/json", Encoding.UTF8, 201);
            }, ct);
        }
        catch (DbUpdateException e) when (IsUnique(e))
        {
            // The same key committed by a concurrent request, or a record older than 24 h: the unique key decides.
            db.ChangeTracker.Clear();
            var stored = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(x => x.ClientUserId == sub && x.IdempotencyKey == key, ct);
            return stored is null ? Problem(ErrorCodes.IdempotencyConflict, "Idempotency conflict", 409) : Replay(stored, hash, now);
        }
    }

    /// <summary>One transaction inside the retrying execution strategy. InMemory has no transactions or unique indexes, so a process lock per idempotency key gives the same one-after-the-other effect there; PostgreSQL relies on the unique key.</summary>
    static async Task<IResult> InTransaction(AccessDbContext db, Guid idempotencyKey, Func<Task<IResult>> body, CancellationToken ct)
    {
        if (!db.Database.IsRelational())
        {
            using var held = await KeyLocks.AcquireAsync(idempotencyKey, ct);
            return await body();
        }
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear(); // a retry starts from the database, not from a half-applied attempt
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var result = await body();
            await tx.CommitAsync(ct);
            return result;
        });
    }

    static IResult Replay(IdempotencyRecord record, string hash, DateTimeOffset now)
    {
        if (now - record.CreatedAt >= IdempotencyWindow) // older than 24 h counts as absent; the bookings unique index would refuse a new booking
            return Problem(ErrorCodes.IdempotencyConflict, "Idempotency conflict", 409);
        if (record.RequestHash != hash) return Problem(ErrorCodes.IdempotencyConflict, "Idempotency conflict", 409,
            detail: "The Idempotency-Key was used with a different request.");
        return Results.Text(record.ResponseBody, "application/json", Encoding.UTF8, record.ResponseStatus);
    }

    static bool IsUnique(Exception e)
    {
        for (var x = e; x is not null; x = x.InnerException) if (x is PostgresException { SqlState: UniqueViolation }) return true;
        return false;
    }

    /// <summary>startUtc, when present, is ignored (A-02), so the body is deserialized directly.</summary>
    static async Task<(CreateAccessRequest? Request, Dictionary<string, string[]> Errors)> ReadBodyAsync(HttpRequest request, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        CreateAccessRequest? req;
        try { req = await JsonSerializer.DeserializeAsync<CreateAccessRequest>(request.Body, Json, ct); }
        catch (Exception e) when (e is JsonException or FormatException) { return (null, new() { ["body"] = ["The body must be a valid JSON object"] }); }
        if (req is null) return (null, new() { ["body"] = ["The body must be a valid JSON object"] });

        if (req.PackageId == Guid.Empty) errors["offeringId"] = ["offeringId is required"];
        if (req.Brief is null) errors["brief"] = ["brief is required"];
        else
        {
            var length = (req.Brief.Text ?? "").Trim().Length;
            if (length is < 20 or > 2000) errors["brief.text"] = ["brief.text must be 20 to 2000 characters"];
            if (req.Brief.ProblemSlug is { Length: > 60 }) errors["brief.problemSlug"] = ["brief.problemSlug must be at most 60 characters"];
        }
        return (req, errors);
    }

    /// <summary>SHA-256 hex of the canonical body: offeringId, brief.text trimmed, problemSlug, aiAssisted (startUtc is ignored).</summary>
    static string Hash(CreateAccessRequest r, string trimmedText) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new object?[]
        {
            r.PackageId.ToString("D"), trimmedText, r.Brief.ProblemSlug, r.Brief.AiAssisted,
        }))).ToLowerInvariant();

    static IResult Problem(string code, string title, int status, Dictionary<string, string[]>? errors = null, string? detail = null) =>
        Results.Json(ApiProblem.Of(code, title, status, detail, errors), statusCode: status, contentType: "application/problem+json");
}

/// <summary>Per-idempotency-key mutual exclusion for providers without unique indexes (InMemory).</summary>
static class KeyLocks
{
    static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Locks = new();

    public static async Task<IDisposable> AcquireAsync(Guid key, CancellationToken ct)
    {
        var gate = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        return new Releaser(gate);
    }

    sealed class Releaser(SemaphoreSlim gate) : IDisposable { public void Dispose() => gate.Release(); }
}
