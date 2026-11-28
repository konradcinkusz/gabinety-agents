using Gabinety.Booking.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Gabinety.Booking.Tests;

public class SkeletonMigrationStateTests
{
    [Fact]
    public async Task On_in_memory_the_migration_service_is_a_no_op_and_the_state_becomes_Done()
    {
        using var f = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseSetting("GABINETY_FAKE", "1")); // Mock payments need the fake profile (F-01)
        f.CreateClient();
        var state = f.Services.GetRequiredService<MigrationState>();
        await state.WaitForCompletionAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(MigrationStatus.Done, state.Status);
    }

    [Fact]
    public void The_migrations_match_the_model_so_MigrateAsync_will_not_refuse_to_run()
    {
        using var db = new AccessDbContextFactory().CreateDbContext([]);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}

[Trait("tier", "integration")]
public class SkeletonMigrationIntegrationTests
{
    // The migrations already released, in order. Never edit or reorder these; a new migration is NOT added here: the applied history must equal the assembly's own list
    // (a missing, extra or reordered migration fails) and must start with this pinned prefix (an edited or reordered released one fails).
    static readonly string[] Shipped = ["20261116000000_T-04.03_Baseline", "20261116110000_T-04.03_Availability", "20261116130000_T-04.04_Bookings", "20261116160000_T-04.05_Payments", "20261117090000_T-04.06_Webhook", "20261118090000_T-04.07b_Disputes", "20261118100000_T-04.08c_Payouts", "20261118110000_T-04.09a_Notifications", "20261122090914_T-09.05_StripeConnect", "20261122100000_T-04.09c_ClientEmail"];
    static readonly string[] Expected = [.. new AccessDbContextFactory().CreateDbContext([]).Database.GetMigrations()];

    static void AssertHistory(IReadOnlyList<string> applied)
    {
        Assert.Equal(Expected, applied);                                  // everything in the assembly, applied once, in order
        Assert.Equal(Shipped, applied.Take(Shipped.Length).ToArray());    // the released part is untouched
    }

    static WebApplicationFactory<Program> Host(string cs) => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
    {
        b.UseSetting("GABINETY_FAKE", "1"); // Mock payments need the fake profile (F-01)
        b.UseSetting("Database:Provider", "PostgreSQL");
        b.UseSetting("ConnectionStrings:bookingdb", cs);
    });

    static async Task<List<string>> Column(string cs, string sql)
    {
        await using var c = new NpgsqlConnection(cs);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c);
        await using var r = await cmd.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await r.ReadAsync()) rows.Add(r.GetString(0));
        return rows;
    }

    static async Task Start(string cs)
    {
        using var host = Host(cs);
        host.CreateClient();
        var state = host.Services.GetRequiredService<MigrationState>();
        await state.WaitForCompletionAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(MigrationStatus.Done, state.Status);
    }

    static AccessDbContext Db(string cs) =>
        new(new DbContextOptionsBuilder<AccessDbContext>().UseNpgsql(cs).Options);

    static async Task<PostgreSqlContainer> Pg()
    {
        var pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await pg.StartAsync();
        return pg;
    }

    [Fact]
    public async Task Migration_runs_once_and_a_second_start_is_a_no_op()
    {
        await using var pg = await Pg();
        var cs = pg.GetConnectionString();
        const string history = """select "MigrationId" from "__EFMigrationsHistory" order by 1""";

        await Start(cs);
        AssertHistory(await Column(cs, history));
        await Start(cs);
        AssertHistory(await Column(cs, history));
        Assert.Empty(await Column(cs, "select table_name from information_schema.tables where table_schema='public' and table_name like 'availability%' order by 1")); // E-01 dropped them
    }
}
