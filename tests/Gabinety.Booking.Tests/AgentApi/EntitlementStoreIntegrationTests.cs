using System.Net.Http.Json;
using Gabinety.Booking.Features.AgentApi;
using Gabinety.Contracts.Directory;
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
        Assert.Equal(100, (await db.Accesses.AsNoTracking().SingleAsync(b => b.Id == id)).UnitsUsed);

        using var scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<EntitlementStore>().RefundUnitsAsync(id, 500, default);
        Assert.Equal(0, (await db.Accesses.AsNoTracking().SingleAsync(b => b.Id == id)).UnitsUsed);
    }

    [Fact]
    public async Task Usage_records_round_trip_with_the_kind_stored_as_text()
    {
        await using var db = new BookingDbContext(new DbContextOptionsBuilder<BookingDbContext>().UseNpgsql(pg.GetConnectionString()).Options);
        var row = new UsageRecord { Id = Guid.NewGuid(), AccessId = Guid.NewGuid(), ApiKeyId = null, Kind = Gabinety.Contracts.Directory.PackageKind.Chat, Units = 42, CreatedAt = DateTimeOffset.UtcNow };
        db.UsageRecords.Add(row);
        await db.SaveChangesAsync();
        Assert.Equal("Chat", await db.Database.SqlQuery<string>($"select kind as \"Value\" from usage_records where id = {row.Id}").SingleAsync());
    }

    [Fact]
    public async Task Twenty_parallel_orders_for_five_units_succeed_exactly_five_times()
    {
        var id = await AgentApiFx.Package(host, AgentApiFx.NewUser(), kind: PackageKind.Tasks, total: 5);
        var taken = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(async () =>
        {
            using var scope = host.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<EntitlementStore>().TryConsumeAsync(id, 1, default);
        })));
        Assert.Equal(5, taken.Count(t => t));
        await using var db = new BookingDbContext(new DbContextOptionsBuilder<BookingDbContext>().UseNpgsql(pg.GetConnectionString()).Options);
        Assert.Equal(5, (await db.Accesses.AsNoTracking().SingleAsync(b => b.Id == id)).UnitsUsed);
    }

    [Fact]
    public async Task Two_task_jobs_at_once_start_each_task_exactly_once_and_the_texts_round_trip()
    {
        var client = AgentApiFx.NewUser();
        var package = await AgentApiFx.Package(host, client, kind: PackageKind.Tasks, total: 10);
        var texts = Enumerable.Range(0, 4).Select(i => $"demo-task-{i}: zażółć gęślą jaźń — migration plan with unicode").ToList();
        var ids = new List<Guid>();
        foreach (var text in texts)
            ids.Add((await (await host.ClientFor(client).PostAsJsonAsync($"/api/v1/bookings/{package}/tasks", new { input = text })).Body()).GetProperty("id").GetGuid());

        await Task.WhenAll(Task.Run(() => TaskFx.Job(host, client).RunOnceAsync(host.Clock.GetUtcNow(), default)), Task.Run(() => TaskFx.Job(host, client).RunOnceAsync(host.Clock.GetUtcNow(), default)));

        await using var db = new BookingDbContext(new DbContextOptionsBuilder<BookingDbContext>().UseNpgsql(pg.GetConnectionString()).Options);
        var rows = await db.AgentTasks.AsNoTracking().Where(t => t.AccessId == package).OrderBy(t => t.CreatedAt).ToListAsync();
        Assert.Equal(4, rows.Count);
        Assert.All(rows, t => { Assert.Equal(AgentTaskStatus.Succeeded, t.Status); Assert.Equal(1, t.Attempts); Assert.False(string.IsNullOrWhiteSpace(t.OutputText)); });
        Assert.Equal(texts.Order().ToList(), rows.Select(t => t.InputText).Order().ToList());
        Assert.Equal(4, (await db.Accesses.AsNoTracking().SingleAsync(b => b.Id == package)).UnitsUsed);
    }
}
