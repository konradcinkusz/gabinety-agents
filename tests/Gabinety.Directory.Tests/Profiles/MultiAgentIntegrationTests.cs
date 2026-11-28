using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gabinety.Contracts.Common;
using Gabinety.Contracts.Directory;
using Gabinety.Directory.Persistence;
using Gabinety.Directory.Tests.Offerings;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using static Gabinety.Directory.Tests.Offerings.Kit;

namespace Gabinety.Directory.Tests.Profiles;

/// <summary>E-04 on real PostgreSQL 17: the advisory lock keeps the agent limit under a race, the agent-scoped row lock works with a slug, and the migration made the user index non-unique.</summary>
[Trait("tier", "integration")]
public class MultiAgentIntegrationTests
{
    sealed class PgFactory(string cs) : PackageFactory
    {
        protected override string Provider => "PostgreSQL";
        protected override string? ConnectionString => cs;
    }

    static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);
    const string Me = "/api/v1/me/practitioner";

    static async Task<(PostgreSqlContainer Pg, PgFactory F)> Start()
    {
        var pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await pg.StartAsync();
        var f = new PgFactory(pg.GetConnectionString());
        f.CreateClient();
        await f.Services.GetRequiredService<MigrationState>().WaitForCompletionAsync(TimeSpan.FromSeconds(60));
        await WaitForSeed(f);
        return (pg, f);
    }

    [Fact]
    public async Task Concurrent_creates_of_one_account_stop_at_the_limit_and_the_rest_are_too_many_agents()
    {
        var (pg, f) = await Start();
        await using var _ = pg; using var __ = f;
        var sub = Id();
        var clients = Enumerable.Range(0, 13).Select(_ => f.ClientFor(sub)).ToList();

        using var gate = new ManualResetEventSlim(false);
        var calls = clients.Select((c, i) => Task.Run(async () =>
        {
            gate.Wait();
            return await c.PostAsJsonAsync(Me, new CreateAgentRequest($"Demo Wyścig {i} {Guid.NewGuid().ToString("n")[..6]}"));
        })).ToList();
        gate.Set();
        var results = await Task.WhenAll(calls);

        Assert.Equal(10, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        var refused = results.Where(r => r.StatusCode != HttpStatusCode.Created).ToList();
        Assert.Equal(3, refused.Count);
        foreach (var r in refused)
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
            Assert.Equal("too_many_agents", (await r.Content.ReadFromJsonAsync<ApiProblem>(J))!.Code);
        }
        var list = (await f.ClientFor(sub).GetFromJsonAsync<JsonElement>("/api/v1/me/practitioners", J)).GetProperty("items");
        Assert.Equal(10, list.GetArrayLength());
    }

    [Fact]
    public async Task Packages_on_a_chosen_agent_lock_and_change_only_that_agents_row()
    {
        var (pg, f) = await Start();
        await using var _ = pg; using var __ = f;
        var c = f.ClientFor(Id());
        var a = (await (await c.PostAsJsonAsync(Me, new CreateAgentRequest("Demo PG Alfa " + Guid.NewGuid().ToString("n")[..6]))).Content.ReadFromJsonAsync<AgentPrivate>(J))!;
        var b = (await (await c.PostAsJsonAsync(Me, new CreateAgentRequest("Demo PG Beta " + Guid.NewGuid().ToString("n")[..6]))).Content.ReadFromJsonAsync<AgentPrivate>(J))!;

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PostAsJsonAsync($"{Me}/offerings", Valid())).StatusCode); // two agents, none named
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync($"{Me}/offerings?agent={b.Slug}", Valid())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.ClientFor(Id()).PostAsJsonAsync($"{Me}/offerings?agent={b.Slug}", Valid())).StatusCode); // another account
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsJsonAsync($"{Me}/offerings?agent=demo-no-such-agent", Valid())).StatusCode);

        async Task<int> Count(string slug) => (await c.GetFromJsonAsync<JsonElement>($"{Me}/offerings?agent={slug}", J)).GetProperty("items").GetArrayLength();
        Assert.Equal(0, await Count(a.Slug));
        Assert.Equal(1, await Count(b.Slug));
    }

    [Fact]
    public async Task The_user_id_index_is_not_unique_after_the_migration()
    {
        var (pg, f) = await Start();
        await using var _ = pg; using var __ = f;
        await using var conn = new NpgsqlConnection(pg.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("select indexdef from pg_indexes where indexname = 'IX_practitioners_user_id'", conn);
        var def = (string)(await cmd.ExecuteScalarAsync())!;
        Assert.DoesNotContain("UNIQUE", def, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("(user_id)", def);
    }
}
