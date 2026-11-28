using System.Net.Http.Json;
using Gabinety.Billing;
using Gabinety.Contracts.Directory;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Access;

namespace Gabinety.Booking.Tests;

/// <summary>Helpers of the public agent API tests (B-02, B-04, B-05): packages seeded straight into the database, API keys made through the real endpoint.</summary>
static class AgentApiFx
{
    public const string Slug = "demo-bartosz-testowy";

    public static string NewUser() => "demo-api-" + Guid.NewGuid().ToString("N");

    /// <summary>The in-memory database is shared by every host of a run: each test uses its own user, and a fresh slug when it counts rows per agent.</summary>
    public static async Task<Guid> Package(CreateHost h, string client, string slug = Slug, PackageKind kind = PackageKind.Chat, long total = 100_000, long used = 0,
        AccessStatus status = AccessStatus.Confirmed, TimeSpan? validFor = null, TimeSpan? created = null, Guid? offering = null)
    {
        var id = Guid.NewGuid();
        var now = h.Clock.GetUtcNow();
        await h.Seed(db => db.Accesses.Add(new BookingEntity
        {
            Id = id, ClientUserId = client, AgentId = h.Agent, PractitionerUserId = h.OwnerUserId, PackageId = offering ?? h.Package,
            PackageTitle = "demo-package", DurationMinutes = 30, PriceMinor = 9900, StartUtc = now, EndUtc = now + (validFor ?? TimeSpan.FromDays(30)),
            Kind = kind, UnitsTotal = total, UnitsUsed = used, PractitionerSlug = slug, Status = status, BriefText = "demo-brief: a first review of my plan",
            IdempotencyKey = Guid.NewGuid(), CreatedAt = now + (created ?? TimeSpan.Zero), ConfirmedAt = now,
        }));
        return id;
    }

    /// <summary>A key made through <c>POST /api/v1/me/api-keys</c>; the plain key is returned once, with its id.</summary>
    public static async Task<(string Key, Guid Id)> MakeKey(CreateHost h, string client, string name = "demo-key")
    {
        var r = await h.ClientFor(client).PostAsJsonAsync("/api/v1/me/api-keys", new { name });
        r.EnsureSuccessStatusCode();
        var j = await r.Body();
        return (j.GetProperty("key").GetString()!, j.GetProperty("id").GetGuid());
    }

    public static HttpClient ApiClient(CreateHost h, string key)
    {
        var c = h.CreateClient();
        c.DefaultRequestHeaders.Authorization = new("Bearer", key);
        return c;
    }

    /// <summary>Runs the ApiKey scheme the way the pipeline does, without a route (the endpoints that need it arrive with B-04).</summary>
    public static async Task<(AuthenticateResult Result, HttpContext Ctx)> Authenticate(CreateHost h, string? authorization)
    {
        var scope = h.Services.CreateScope();
        var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        ctx.Response.Body = new MemoryStream();
        if (authorization is not null) ctx.Request.Headers.Authorization = authorization;
        return (await ctx.AuthenticateAsync("ApiKey"), ctx);
    }
}
