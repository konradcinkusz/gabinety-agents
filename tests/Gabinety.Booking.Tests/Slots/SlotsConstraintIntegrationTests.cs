using Gabinety.Billing;
using Gabinety.Booking.Features.Slots;
using Gabinety.Booking.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Gabinety.Booking.Tests;

/// <summary>Booking schema and the slots endpoint on real PostgreSQL 17. The no-double-booking exclusion constraint (CONTRACTS 4.1) is gone since A-02 (T-12.02): its tests were removed with it.</summary>
[Trait("tier", "integration")]
public class SlotsConstraintIntegrationTests : IAsyncLifetime
{
    static readonly DateTimeOffset Base = new(2027, 3, 1, 8, 0, 0, TimeSpan.Zero);
    PostgreSqlContainer pg = null!;
    string cs = "";

    public async Task InitializeAsync()
    {
        pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await pg.StartAsync();
        cs = pg.GetConnectionString();
        await using var db = Db();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await pg.DisposeAsync();

    BookingDbContext Db() => new(new DbContextOptionsBuilder<BookingDbContext>().UseNpgsql(cs).Options);

    static async Task Insert(BookingDbContext db, Guid practitioner, DateTimeOffset start, int minutes = 60, BookingStatus status = BookingStatus.Confirmed)
    {
        db.Bookings.Add(Slots.NewBooking(practitioner, start, minutes, status));
        await BookingSlotGuard.SaveAsync(db, default);
    }

    static async Task<bool> TryInsert(string cs, Guid practitioner, DateTimeOffset start, int minutes = 60, BookingStatus status = BookingStatus.Confirmed)
    {
        await using var db = new BookingDbContext(new DbContextOptionsBuilder<BookingDbContext>().UseNpgsql(cs).Options);
        try { await Insert(db, practitioner, start, minutes, status); return true; }
        catch (SlotTakenException) { return false; }
    }

    [Fact]
    public async Task The_migration_creates_the_table_and_the_indexes_and_T_12_02_has_dropped_the_overlap_constraint_and_the_history_lists_every_migration()
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
        Assert.Equal(declared, await Rows("""select "MigrationId" from "__EFMigrationsHistory" order by 1"""));
        Assert.Contains("20261116130000_T-04.04_Bookings", declared);
        Assert.DoesNotContain("bookings_no_overlap", await Rows("select conname from pg_constraint where contype = 'x'")); // dropped by T-12.02 (A-02): packages of one agent may overlap
        Assert.Contains("btree_gist", await Rows("select extname from pg_extension"));
        Assert.Contains("IX_bookings_client_user_id_idempotency_key", await Rows("select indexname from pg_indexes where tablename = 'bookings'"));
        Assert.Contains("timestamp with time zone", await Rows("select data_type from information_schema.columns where table_name='bookings' and column_name='start_utc'"));
        Assert.Contains("character varying", await Rows("select data_type from information_schema.columns where table_name='bookings' and column_name='status'"));
    }

    [Fact]
    public async Task The_slots_endpoint_runs_against_real_PostgreSQL_and_a_booked_slot_disappears()
    {
        var practitioner = Guid.NewGuid();
        var offering = Guid.NewGuid();
        await using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("GABINETY_FAKE", "1"); // Mock payments need the fake profile (F-01)
            b.UseSetting("Database:Provider", "PostgreSQL");
            b.UseSetting("ConnectionStrings:bookingdb", cs);
            b.ConfigureServices(s =>
            {
                s.RemoveAll<Gabinety.Booking.Clients.IDirectoryClient>();
                s.AddSingleton<Gabinety.Booking.Clients.IDirectoryClient>(new StubOffering(practitioner, offering));
                s.RemoveAll<TimeProvider>();
                s.AddSingleton<TimeProvider>(new ManualClock());
            });
        });
        var client = host.CreateClient();
        await host.Services.GetRequiredService<MigrationState>().WaitForCompletionAsync(TimeSpan.FromSeconds(60));
        await using (var db = Db())
        {
            db.Rules.Add(new() { Id = Guid.NewGuid(), PractitionerId = practitioner, IsoWeekday = 2, StartLocal = new(9, 0), EndLocal = new(17, 0) });
            await db.SaveChangesAsync();
            await Insert(db, practitioner, new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero));
        }
        var json = await client.GetStringAsync($"/api/v1/practitioners/{practitioner}/slots?offeringId={offering}&from=2026-11-17&to=2026-11-17");
        Assert.DoesNotContain("2026-11-17T10:00:00Z", json);
        Assert.Contains("2026-11-17T08:00:00Z", json);
        Assert.Contains("2026-11-17T11:30:00Z", json);
    }

    sealed class StubOffering(Guid practitioner, Guid offering) : Gabinety.Booking.Clients.IDirectoryClient
    {
        public Task<Gabinety.Booking.Clients.DirectoryOffering?> GetOfferingAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<Gabinety.Booking.Clients.DirectoryOffering?>(id != offering ? null : new(offering, practitioner, "demo-user-1",
                Gabinety.Contracts.Directory.PractitionerStatus.Approved, "demo-offering", 60, new(10000), null, true));
        public Task<Gabinety.Booking.Clients.DirectoryPractitioner?> GetPractitionerByUserAsync(string userId, CancellationToken ct) => Task.FromResult<Gabinety.Booking.Clients.DirectoryPractitioner?>(null);
        public Task<Gabinety.Booking.Clients.DirectoryPractitionerName?> GetPractitionerNameAsync(Guid id, CancellationToken ct) => Task.FromResult<Gabinety.Booking.Clients.DirectoryPractitionerName?>(null);
        public Task<Gabinety.Booking.Clients.DirectoryContact?> GetPractitionerContactAsync(string userId, CancellationToken ct) => Task.FromResult<Gabinety.Booking.Clients.DirectoryContact?>(null);
        public Task<bool> IsAliveAsync(CancellationToken ct) => Task.FromResult(true);
    }
}
