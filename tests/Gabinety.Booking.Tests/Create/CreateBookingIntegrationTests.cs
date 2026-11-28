using System.Net;
using Gabinety.Booking.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Gabinety.Booking.Tests;

/// <summary>Booking creation on real PostgreSQL 17 through the real HTTP pipeline: unique keys, retrying execution strategy.</summary>
[Trait("tier", "integration")]
public class CreateBookingIntegrationTests : IAsyncLifetime
{
    PostgreSqlContainer pg = null!;
    string cs = "";
    CreateHost host = null!;

    public async Task InitializeAsync()
    {
        pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await pg.StartAsync();
        cs = pg.GetConnectionString();
        host = new CreateHost();
        host.Settings["Database:Provider"] = "PostgreSQL";
        host.Settings["ConnectionStrings:bookingdb"] = cs;
        host.CreateClient(); // builds the host; the migration service runs after start
        await host.Services.GetRequiredService<MigrationState>().WaitForCompletionAsync(TimeSpan.FromSeconds(60));
    }

    public async Task DisposeAsync() { await host.DisposeAsync(); await pg.DisposeAsync(); }

    BookingDbContext Db() => new(new DbContextOptionsBuilder<BookingDbContext>().UseNpgsql(cs).Options);

    /// <summary>Start of the n-th weekday after the clock's Monday 2026-11-16: 10:00 UTC (11:00 Warsaw).</summary>
    static string Day(int round)
    {
        var d = new DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc);
        for (var left = round; left > 0; d = d.AddDays(1)) if (d.AddDays(1).DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) left--;
        while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) d = d.AddDays(1);
        return d.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
    }

    [Fact]
    public async Task Two_requests_with_the_same_key_at_once_create_one_booking_and_both_answers_carry_the_same_booking_id()
    {
        for (var round = 0; round < 10; round++)
        {
            var c = host.ClientFor($"demo-client-s-r{round}"); // see above: the rate limit is per user and the clock is fixed
            var start = Day(round + 20);
            var key = CreateHost.Key();
            var rs = await Task.WhenAll(Task.Run(() => CreateHost.Post(c, host.Req(start), key)), Task.Run(() => CreateHost.Post(c, host.Req(start), key)));
            Assert.All(rs, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
            Assert.Equal((await rs[0].Body()).GetProperty("bookingId").GetGuid(), (await rs[1].Body()).GetProperty("bookingId").GetGuid());
            await using var db = Db();
            Assert.Equal(1, await db.Accesses.CountAsync(x => x.IdempotencyKey == Guid.Parse(key)));
            Assert.Equal(1, await db.IdempotencyRecords.CountAsync(x => x.IdempotencyKey == Guid.Parse(key)));
        }
    }

    [Fact]
    public async Task The_migration_applies_the_history_equals_every_declared_migration_and_the_unique_constraints_exist()
    {
        await using var c = new NpgsqlConnection(cs);
        await c.OpenAsync();
        async Task<List<string>> Rows(string sql)
        {
            await using var cmd = new NpgsqlCommand(sql, c);
            await using var r = await cmd.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await r.ReadAsync()) rows.Add(r.GetString(0));
            return rows;
        }

        var declared = Db().Database.GetMigrations().ToList();
        Assert.Contains("20261116160000_T-04.05_Payments", declared);
        Assert.Equal(declared, await Rows("""select "MigrationId" from "__EFMigrationsHistory" order by 1"""));
        Assert.Contains("IX_payments_booking_id", await Rows("select indexname from pg_indexes where tablename = 'payments' and indexdef like 'CREATE UNIQUE%'"));
        Assert.Contains("PK_idempotency_records", await Rows("select conname from pg_constraint where conrelid = 'idempotency_records'::regclass and contype = 'p'"));
        Assert.Equal(["client_user_id", "idempotency_key"], await Rows(
            "select a.attname from pg_index i join pg_attribute a on a.attrelid = i.indrelid and a.attnum = any(i.indkey) " +
            "where i.indrelid = 'idempotency_records'::regclass and i.indisprimary order by a.attnum"));
        Assert.Contains("IX_bookings_client_user_id_idempotency_key", await Rows("select indexname from pg_indexes where tablename = 'bookings' and indexdef like 'CREATE UNIQUE%'"));
        Assert.Contains("text", await Rows("select data_type from information_schema.columns where table_name='idempotency_records' and column_name='response_body'"));
    }
}
