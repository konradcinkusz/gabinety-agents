using Gabinety.Booking.Features.AgentApi;
using Gabinety.Booking.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Gabinety.Booking.Tests;

/// <summary>The one-statement <c>UnitsUsed + n</c> on real PostgreSQL 17 (the in-memory provider takes the semaphore path): parallel requests never overwrite each other.</summary>
[Trait("tier", "integration")]
public class EntitlementStoreIntegrationTests : IAsyncLifetime
{
    PostgreSqlContainer pg = null!;
    CreateHost host = null!;

    public async Task InitializeAsync()
    {
        pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await pg.StartAsync();
        host = Hold.Host(null, ("Database:Provider", "PostgreSQL"), ("ConnectionStrings:bookingdb", pg.GetConnectionString()));
        host.CreateClient();
        await host.Services.GetRequiredService<MigrationState>().WaitForCompletionAsync(TimeSpan.FromSeconds(60));
    }

    public async Task DisposeAsync() { await host.DisposeAsync(); await pg.DisposeAsync(); }

    [Fact]
    public async Task Fifty_parallel_consumes_add_up_and_a_refund_floors_at_zero()
    {
        var id = await AgentApiFx.Package(host, AgentApiFx.NewUser(), total: 1000);
        await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(async () =>
        {
            using var scope = host.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<EntitlementStore>().ConsumeAsync(id, 2, default);
        })));
        await using var db = new BookingDbContext(new DbContextOptionsBuilder<BookingDbContext>().UseNpgsql(pg.GetConnectionString()).Options);
        Assert.Equal(100, (await db.Bookings.AsNoTracking().SingleAsync(b => b.Id == id)).UnitsUsed);

        using var scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<EntitlementStore>().RefundUnitsAsync(id, 500, default);
        Assert.Equal(0, (await db.Bookings.AsNoTracking().SingleAsync(b => b.Id == id)).UnitsUsed);
    }

    [Fact]
    public async Task Usage_records_round_trip_with_the_kind_stored_as_text()
    {
        await using var db = new BookingDbContext(new DbContextOptionsBuilder<BookingDbContext>().UseNpgsql(pg.GetConnectionString()).Options);
        var row = new UsageRecord { Id = Guid.NewGuid(), BookingId = Guid.NewGuid(), ApiKeyId = null, Kind = Gabinety.Contracts.Directory.OfferingKind.Chat, Units = 42, CreatedAt = DateTimeOffset.UtcNow };
        db.UsageRecords.Add(row);
        await db.SaveChangesAsync();
        Assert.Equal("Chat", await db.Database.SqlQuery<string>($"select kind as \"Value\" from usage_records where id = {row.Id}").SingleAsync());
    }
}
