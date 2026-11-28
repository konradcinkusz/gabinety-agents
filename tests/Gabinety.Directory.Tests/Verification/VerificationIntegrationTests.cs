using System.Net.Http.Json;
using Gabinety.Contracts.Directory;
using Gabinety.Directory.Features.Verification;
using Gabinety.Directory.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using static Gabinety.Directory.Tests.Verification.Vk;

namespace Gabinety.Directory.Tests.Verification;

/// <summary>Real PostgreSQL 17 (Docker needed; CI job `integration` runs these).</summary>
[Trait("tier", "integration")]
public class VerificationIntegrationTests
{
    sealed class PgFactory(string cs) : VerificationFactory
    {
        protected override string Provider => "PostgreSQL";
        protected override string? ConnectionString => cs;
    }

    static async Task<(PostgreSqlContainer Pg, PgFactory F)> Start()
    {
        var pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await pg.StartAsync();
        var f = new PgFactory(pg.GetConnectionString());
        f.CreateClient();
        await f.Services.GetRequiredService<MigrationState>().WaitForCompletionAsync(TimeSpan.FromSeconds(60));
        return (pg, f);
    }

    [Fact]
    public async Task Migration_applies_from_empty_and_creates_the_tables_columns_and_index()
    {
        var (pg, f) = await Start();
        await using var _ = pg; using var __ = f;
        Assert.Equal(MigrationStatus.Done, f.Services.GetRequiredService<MigrationState>().Status);
        await using var conn = new NpgsqlConnection(pg.GetConnectionString());
        await conn.OpenAsync();
        async Task<long> Count(string sql) { await using var c = new NpgsqlCommand(sql, conn); return (long)(await c.ExecuteScalarAsync())!; }
        Assert.Equal(1, await Count("""select count(*) from "__EFMigrationsHistory" where "MigrationId" = '20261116150000_T-03.05_Verification'"""));
        Assert.Equal(2, await Count("select count(*) from information_schema.tables where table_name in ('verification_events','moderators')"));
        Assert.Equal(3, await Count("select count(*) from information_schema.columns where table_name='practitioners' and column_name in ('github_challenge_code','github_challenge_login','github_challenge_expires_at')"));
        Assert.Equal(1, await Count("select count(*) from pg_indexes where indexname='ix_practitioners_github_login_linked' and indexdef ilike '%lower%' and indexdef ilike '%github_linked%'"));
    }

    [Fact]
    public async Task The_github_index_is_not_unique_since_E_04_and_the_user_id_index_is_not_unique_either_but_the_slug_stays_unique()
    {
        var (pg, f) = await Start();
        await using var _ = pg; using var __ = f;
        await f.Seed(PractitionerStatus.Draft, p => { p.GithubLinked = true; p.GithubLogin = "Demo-Dev"; }, userId: "demo-one-account");
        await f.Seed(PractitionerStatus.Draft, p => { p.GithubLinked = true; p.GithubLogin = "demo-DEV"; }, userId: "demo-one-account"); // one account, two agents, one profile
        await using var conn = new NpgsqlConnection(pg.GetConnectionString());
        await conn.OpenAsync();
        async Task<string> Def(string index) { await using var c = new NpgsqlCommand($"select indexdef from pg_indexes where indexname='{index}'", conn); return (string)(await c.ExecuteScalarAsync())!; }
        Assert.DoesNotContain("UNIQUE", await Def("IX_practitioners_user_id"), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UNIQUE", await Def("ix_practitioners_github_login_linked"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UNIQUE", await Def("IX_practitioners_slug"), StringComparison.OrdinalIgnoreCase);
        await f.Seed(PractitionerStatus.Draft, p => p.Slug = "demo-same-slug");
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Seed(PractitionerStatus.Draft, p => p.Slug = "demo-same-slug"));
    }

    [Fact]
    public async Task The_admin_list_filters_orders_and_pages_on_PostgreSQL()
    {
        var (pg, f) = await Start();
        await using var _ = pg; using var __ = f;
        var expected = new List<(DateTimeOffset At, Guid Id)>();
        for (var i = 0; i < 13; i++)
        {
            var at = f.Clock.GetUtcNow().AddHours(-(i / 2)); // pairs share a time: the id breaks the tie
            var p = await f.Seed(PractitionerStatus.Submitted);
            await f.Db(async db => { db.VerificationEvents.Add(new VerificationEvent { Id = Guid.NewGuid(), PractitionerId = p.Id, Kind = VerificationEventKind.Submitted, ActorUserId = "demo", At = at }); await db.SaveChangesAsync(); return 0; });
            expected.Add((at, p.Id));
        }
        await f.Seed(PractitionerStatus.Rejected);
        var c = f.As("demo-admin", "Admin");
        var got = new List<Guid>();
        string? cursor = null;
        do
        {
            var page = (await c.GetFromJsonAsync<AdminPractitionerPage>("/api/v1/admin/practitioners?limit=5" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)), J))!;
            got.AddRange(page.Items.Select(r => r.Id)); cursor = page.NextCursor;
        } while (cursor is not null);
        Assert.Equal(expected.OrderBy(e => e.At).ThenBy(e => e.Id).Select(e => e.Id), got);
    }

    [Fact]
    public async Task The_status_change_and_its_event_commit_together_and_roll_back_together()
    {
        var (pg, f) = await Start();
        await using var _ = pg; using var __ = f;
        var p = await f.Seed(PractitionerStatus.Submitted);
        var dup = Guid.NewGuid();
        await f.Db(async db => { VerificationEvents.Record(db, p.Id, VerificationEventKind.Submitted, "demo", null, f.Clock, dup); await db.SaveChangesAsync(); return 0; });

        await Assert.ThrowsAsync<DbUpdateException>(() => f.Db(async db =>
        {
            (await db.Practitioners.SingleAsync(x => x.Id == p.Id)).Status = PractitionerStatus.Approved;
            VerificationEvents.Record(db, p.Id, VerificationEventKind.Approved, "demo-admin", null, f.Clock, dup); // same id: the insert fails
            await db.SaveChangesAsync();
            return 0;
        }));
        Assert.Equal(PractitionerStatus.Submitted, (await f.Load(p.Id)).Status); // the status change rolled back with it
        Assert.Single(await f.Events(p.Id));
    }
}
