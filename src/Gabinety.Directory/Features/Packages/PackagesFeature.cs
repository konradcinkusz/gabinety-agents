using System.Security.Claims;
using Gabinety.Contracts.Common;
using Gabinety.Contracts.Directory;
using Gabinety.Directory.Features.Profiles;
using Gabinety.Directory.Features.Taxonomy;
using Gabinety.Directory.Persistence;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gabinety.Directory.Features.Packages;

/// <summary>/api/v1/me/practitioner/offerings: list, create, replace, deactivate (CONTRACTS 3.3). Every call runs on one of the caller's own practitioner rows: the one named by <c>?agent=</c> (E-04), or the only one.</summary>
public sealed class PackagesFeature : IServiceFeature
{
    const string Route = "/api/v1/me/practitioner/offerings";
    const int MaxActive = 5, MaxTechs = 4;
    const long DefaultMinPrice = 5000, DefaultMaxPrice = 150000;

    public void Add(IHostApplicationBuilder builder)
    {
        builder.Services.TryAddSingleton(TimeProvider.System);
        // Added after (or before) ProfilesFeature: its registration is a TryAdd, so this one is the last or the only one either way.
        builder.Services.AddSingleton<IChecklistFacts, PackageFacts>();
    }

    public void Map(IEndpointRouteBuilder endpoints)
    {
        var g = endpoints.MapGroup(Route).RequireAuthorization("Authenticated");
        g.MapGet("", List);
        g.MapPost("", Create);
        g.MapPut("{id:guid}", Update);
        g.MapPost("{id:guid}/deactivate", Deactivate);
        g.MapPost("{id:guid}/activate", Activate);
    }

    static IResult Problem(int status, string code, string title, Dictionary<string, string[]>? errors = null) =>
        Results.Json(ApiProblem.Of(code, title, status, null, errors), statusCode: status, contentType: "application/problem+json");

    static string? UserId(ClaimsPrincipal u) => u.FindFirst("sub")?.Value ?? u.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    static PackagePrivate View(Package o) => new(o.Id, o.Title, o.Description, (int)o.DurationMinutes, new MoneyDto(o.PriceMinor, o.Currency),
        o.ProblemSlug, o.TechSlugs, o.IsActive, o.MeetingUrl, o.Kind, o.Units);

    /// <summary>
    /// Runs <paramref name="body"/> on the caller's practitioner row (the agent named by <paramref name="agent"/>, see <see cref="OwnAgents"/>), or answers 404 / 422. On PostgreSQL the
    /// row is read `FOR UPDATE` inside a transaction, so two requests of one agent run one after the other (the 5-active-offerings check and the
    /// insert cannot interleave); the retrying execution strategy needs the whole unit inside one delegate. InMemory has no transactions or locks.
    /// </summary>
    static async Task<IResult> OnOwnRow(DirectoryDbContext db, string uid, string? agent, CancellationToken ct, Func<Agent, Task<IResult>> body)
    {
        if (!db.Database.IsRelational())
        {
            var (p, rejected) = await OwnAgents.FindAsync(db, uid, agent, ct);
            return p is null ? rejected! : await body(p);
        }

        var slug = string.IsNullOrWhiteSpace(agent) ? null : agent.Trim();
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear(); // a retry starts from the database, not from a half-applied attempt
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var rows = await db.Agents.FromSql($"SELECT * FROM practitioners WHERE user_id = {uid} AND (CAST({slug} AS text) IS NULL OR slug = {slug}) ORDER BY created_at, id FOR UPDATE").ToListAsync(ct);
            if (OwnAgents.Reject(rows.Count, slug) is { } rejected) return rejected;
            var result = await body(rows[0]);
            await tx.CommitAsync(ct);
            return result;
        });
    }

    static async Task<IResult> List(string? agent, ClaimsPrincipal user, DirectoryDbContext db, CancellationToken ct)
    {
        if (UserId(user) is not { } uid) return Results.Unauthorized();
        var (p, rejected) = await OwnAgents.FindAsync(db, uid, agent, ct);
        if (p is null) return rejected!;
        var rows = await db.Packages.AsNoTracking().Where(o => o.AgentId == p.Id).OrderBy(o => o.CreatedAt).ThenBy(o => o.Id).ToListAsync(ct); // order, then project
        return Results.Ok(new { items = rows.Select(View) });
    }

    static Task<IResult> Create(PackageRequest req, string? agent, ClaimsPrincipal user, DirectoryDbContext db, IContactDetailDetector contact,
        IConfiguration config, TimeProvider time, CancellationToken ct) =>
        UserId(user) is not { } uid ? Task.FromResult(Results.Unauthorized()) : OnOwnRow(db, uid, agent, ct, async p =>
        {
            if (await Validate(req, db, contact, config, ct) is { } rejected) return rejected;
            if (await db.Packages.CountAsync(o => o.AgentId == p.Id && o.IsActive, ct) >= MaxActive)
                return Problem(422, ErrorCodes.TooManyPackages, "At most 5 active offerings");
            var o = new Package { Id = Guid.NewGuid(), AgentId = p.Id, Currency = "PLN", IsActive = true, CreatedAt = time.GetUtcNow() };
            Apply(o, req);
            db.Packages.Add(o);
            await Save(db, p, time, ct);
            return Results.Created($"{Route}/{o.Id}", View(o));
        });

    static Task<IResult> Update(Guid id, PackageRequest req, string? agent, ClaimsPrincipal user, DirectoryDbContext db, IContactDetailDetector contact,
        IConfiguration config, TimeProvider time, CancellationToken ct) =>
        UserId(user) is not { } uid ? Task.FromResult(Results.Unauthorized()) : OnOwnRow(db, uid, agent, ct, async p =>
        {
            var o = await db.Packages.SingleOrDefaultAsync(x => x.Id == id && x.AgentId == p.Id, ct);
            if (o is null) return Problem(404, ErrorCodes.NotFound, "Not found");
            if (await Validate(req, db, contact, config, ct) is { } rejected) return rejected;
            Apply(o, req); // IsActive is not touched: reactivation is its own route (activate)
            await Save(db, p, time, ct);
            return Results.Ok(View(o));
        });

    static Task<IResult> Deactivate(Guid id, string? agent, ClaimsPrincipal user, DirectoryDbContext db, TimeProvider time, CancellationToken ct) =>
        UserId(user) is not { } uid ? Task.FromResult(Results.Unauthorized()) : OnOwnRow(db, uid, agent, ct, async p =>
        {
            var o = await db.Packages.SingleOrDefaultAsync(x => x.Id == id && x.AgentId == p.Id, ct);
            if (o is null) return Problem(404, ErrorCodes.NotFound, "Not found");
            o.IsActive = false; // already inactive: nothing changes, still 204
            await Save(db, p, time, ct);
            return Results.NoContent();
        });

    /// <summary>Turns a deactivated offering back on (the practitioner's undo of "Wyłącz"). The limit of 5 active offerings applies; already active: nothing changes, still 204.</summary>
    static Task<IResult> Activate(Guid id, string? agent, ClaimsPrincipal user, DirectoryDbContext db, TimeProvider time, CancellationToken ct) =>
        UserId(user) is not { } uid ? Task.FromResult(Results.Unauthorized()) : OnOwnRow(db, uid, agent, ct, async p =>
        {
            var o = await db.Packages.SingleOrDefaultAsync(x => x.Id == id && x.AgentId == p.Id, ct);
            if (o is null) return Problem(404, ErrorCodes.NotFound, "Not found");
            if (o.IsActive) return Results.NoContent();
            if (await db.Packages.CountAsync(x => x.AgentId == p.Id && x.IsActive, ct) >= MaxActive)
                return Problem(422, ErrorCodes.TooManyPackages, "At most 5 active offerings");
            o.IsActive = true;
            await Save(db, p, time, ct);
            return Results.NoContent();
        });

    static void Apply(Package o, PackageRequest r)
    {
        o.Title = r.Title.Trim();
        o.Description = r.Description?.Trim() ?? "";
        o.DurationMinutes = (short)r.DurationMinutes; // validated to 30 or 60
        o.PriceMinor = r.PriceMinor;
        o.ProblemSlug = r.ProblemSlug;
        o.Kind = r.Kind;
        o.Units = r.Units;
        o.TechSlugs = (r.TechSlugs ?? []).ToArray();
        o.MeetingUrl = string.IsNullOrWhiteSpace(r.MeetingUrl) ? null : r.MeetingUrl.Trim();
    }

    /// <summary>Stores the change, then recomputes the practitioner's completeness from the offerings as they now are (the "one active offering" component).</summary>
    static async Task Save(DirectoryDbContext db, Agent p, TimeProvider time, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        var hasActive = await db.Packages.AnyAsync(o => o.AgentId == p.Id && o.IsActive, ct);
        p.ProfileCompleteness = ProfileCompleteness.Compute(p.Headline.Length > 0, p.Bio.Length >= 200, hasActive, p.GithubLinked,
            p.PortfolioUrl is not null, p.NipValid, p.OpenSlots14d > 0);
        Ranking.RankingRecalculator.Recompute(p);
        p.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>CONTRACTS 3.5. Returns the 422 to send, or null when the request is acceptable. Field errors come first, then the contact-detail check.</summary>
    static async Task<IResult?> Validate(PackageRequest r, DirectoryDbContext db, IContactDetailDetector contact, IConfiguration config, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var title = r.Title?.Trim() ?? "";
        var description = r.Description?.Trim() ?? "";
        if (title.Length is < 5 or > 80 || title.Any(char.IsControl)) errors["title"] = ["5 to 80 characters, no control characters"];
        if (description.Length > 600) errors["description"] = ["at most 600 characters"];
        if (r.DurationMinutes is not (30 or 60)) errors["durationMinutes"] = ["30 or 60"];
        var (minUnits, maxUnits) = r.Kind == PackageKind.Chat ? (100, 100000) : (1, 1000);
        if (!Enum.IsDefined(r.Kind)) errors["kind"] = ["Tasks or Chat"];
        else if (r.Units < minUnits || r.Units > maxUnits) errors["units"] = [$"{minUnits} to {maxUnits}"];
        var min = config.GetValue<long?>("Directory:Price:MinMinor") ?? DefaultMinPrice;
        var max = config.GetValue<long?>("Directory:Price:MaxMinor") ?? DefaultMaxPrice;
        if (r.PriceMinor < min || r.PriceMinor > max) errors["priceMinor"] = [$"{min} to {max} grosz"];

        var meeting = string.IsNullOrWhiteSpace(r.MeetingUrl) ? null : r.MeetingUrl.Trim();
        if (meeting is not null)
        {
            var hosts = config.GetSection("Directory:MeetingHosts").Get<string[]>() is { Length: > 0 } c ? c : UrlRules.DefaultMeetingHosts;
            if (!UrlRules.IsAcceptable(meeting, out var host) || !UrlRules.HostAllowed(host, hosts))
                errors["meetingUrl"] = ["an https URL on an allowed meeting host, at most 300 characters"];
        }

        var techs = r.TechSlugs ?? [];
        if (techs.Count > MaxTechs) errors["techSlugs"] = ["at most 4"];
        else if (techs.Distinct().Count() != techs.Count) errors["techSlugs"] = ["no duplicates"];

        var wanted = techs.Append(r.ProblemSlug ?? "").Distinct().ToList();
        var active = await db.Categories.AsNoTracking().Where(x => x.IsActive && wanted.Contains(x.Slug)).Select(x => new { x.Slug, x.Kind }).ToListAsync(ct);
        if (!active.Any(x => x.Slug == r.ProblemSlug && x.Kind == CategoryKind.Problem)) errors["problemSlug"] = ["an active problem category"];
        if (!errors.ContainsKey("techSlugs") && techs.Any(t => !active.Any(x => x.Slug == t && x.Kind == CategoryKind.Tech)))
            errors["techSlugs"] = ["each an active tech category"];

        if (errors.Count > 0) return Problem(422, ErrorCodes.ValidationFailed, "Validation failed", errors);
        return contact.ContainsContactDetails(title) || contact.ContainsContactDetails(description)
            ? Problem(422, ErrorCodes.ContactDetailsNotAllowed, "Contact details are not allowed in the offering text")
            : null;
    }
}
