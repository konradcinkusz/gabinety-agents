using Gabinety.Billing;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Features.Payouts;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Booking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using AccessStatus = Gabinety.Billing.AccessStatus;
using AgentTask = Gabinety.Booking.Features.AgentApi.AgentTask;
using AgentTaskStatus = Gabinety.Booking.Features.AgentApi.AgentTaskStatus;
using Gabinety.Contracts.Directory;

namespace Gabinety.Booking.Tests;

/// <summary>
/// ADR-0013, owner decision 2, on real PostgreSQL 17: the reopening takes the booking's row lock (<c>SELECT ... FOR UPDATE</c>), so a completion and the failure of the last task that run at the same
/// time, in two jobs, end in ONE state whatever the interleaving: an open (<c>Confirmed</c>) package with its unit back and no fee row. The ledger delete and the payout checks are the real SQL.
/// </summary>
[Trait("tier", "integration")]
public class PackageReopeningIntegrationTests : IAsyncLifetime
{
    PostgreSqlContainer pg = null!;
    CreateHost host = null!;
    readonly string Slug = "demo-agent-" + Guid.NewGuid().ToString("N")[..8];
    readonly Guid Offer = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await pg.StartAsync();
        host = Reopen.Host(("Database:Provider", "PostgreSQL"), ("ConnectionStrings:bookingdb", pg.GetConnectionString()));
        host.CreateClient();
        await host.Services.GetRequiredService<MigrationState>().WaitForCompletionAsync(TimeSpan.FromSeconds(60));
    }

    public async Task DisposeAsync() { await host.DisposeAsync(); await pg.DisposeAsync(); }

    BookingDbContext NewDb() => new(new DbContextOptionsBuilder<BookingDbContext>().UseNpgsql(pg.GetConnectionString()).Options);

    [Fact]
    public async Task The_whole_path_reopens_the_package_removes_the_unpaid_fee_row_and_frees_the_pair()
    {
        var client = AgentApiFx.NewUser();
        var (package, tasks) = await Reopen.Ordered(host, client, Slug, Offer);
        Assert.Equal(1, await Reopen.Complete(host));
        await using (var db = NewDb())
        {
            var done = await db.Accesses.AsNoTracking().SingleAsync(b => b.Id == package);
            Assert.Equal((AccessStatus.Completed, true), (done.Status, done.FeeMinor is not null));
            Assert.Single(await db.LedgerEntries.AsNoTracking().Where(l => l.AccessId == package && l.Kind == LedgerKind.PlatformFee).ToListAsync());
        }

        await Reopen.FailAll(host, client);

        await using (var db = NewDb())
        {
            var b = await db.Accesses.AsNoTracking().SingleAsync(x => x.Id == package);
            Assert.Equal((AccessStatus.Confirmed, 0L, null, null, null, null), (b.Status, b.UnitsUsed, b.FeeMinor, b.PayoutMinor, b.PayoutDueAt, b.EndedAt));
            Assert.Empty(await db.LedgerEntries.AsNoTracking().Where(l => l.AccessId == package && l.Kind == LedgerKind.PlatformFee).ToListAsync());
            Assert.Equal(AgentTaskStatus.Failed, (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == tasks[0])).Status);
        }

        // the unit is usable and the next completion claims the pair's fee again (the unique partial index on pair_key is free)
        Assert.True((await host.ClientFor(client).PostAsync($"/api/v1/bookings/{package}/tasks", TaskFx.Json(new { input = Reopen.Input }))).IsSuccessStatusCode);
        Assert.Equal(1, await Reopen.Complete(host));
        await using var after = NewDb();
        Assert.Single(await after.LedgerEntries.AsNoTracking().Where(l => l.AccessId == package && l.Kind == LedgerKind.PlatformFee).ToListAsync());
    }

    [Fact]
    public async Task A_payout_row_of_any_status_or_a_payout_entry_blocks_the_reopening()
    {
        foreach (var (status, entry) in new[] { (PayoutStatus.Sent, true), (PayoutStatus.Pending, false) })
        {
            var client = AgentApiFx.NewUser();
            var (package, _) = await Reopen.Ordered(host, client, Slug, Offer);
            await Reopen.Complete(host);
            long payout;
            await using (var db = NewDb()) payout = (await db.Accesses.AsNoTracking().SingleAsync(b => b.Id == package)).PayoutMinor!.Value;
            await host.Seed(db =>
            {
                db.Payouts.Add(new Payout { Id = Guid.NewGuid(), AccessId = package, AgentId = host.Agent, AmountMinor = payout, Status = status, ProviderRef = "demo-ref", CreatedAt = host.Clock.GetUtcNow() });
                if (entry) db.LedgerEntries.Add(new LedgerEntry { Id = Guid.NewGuid(), AccessId = package, Kind = LedgerKind.PractitionerPayout, AmountMinor = payout, CreatedAt = host.Clock.GetUtcNow() });
            });

            await Reopen.FailAll(host, client);

            await using var check = NewDb();
            var b = await check.Accesses.AsNoTracking().SingleAsync(x => x.Id == package);
            Assert.Equal((AccessStatus.Completed, 0L, payout), (b.Status, b.UnitsUsed, b.PayoutMinor));
            Assert.Single(await check.LedgerEntries.AsNoTracking().Where(l => l.AccessId == package && l.Kind == LedgerKind.PlatformFee).ToListAsync());
        }
    }

    [Fact]
    public async Task Two_completion_jobs_and_the_failure_of_the_last_task_at_once_end_with_an_open_package_in_every_interleaving()
    {
        for (var round = 0; round < 25; round++)
        {
            var client = AgentApiFx.NewUser();
            var package = await AgentApiFx.Package(host, client, Slug, PackageKind.Tasks, total: 1, used: 1, package: Offer);
            var now = host.Clock.GetUtcNow();
            await host.Seed(db => db.AgentTasks.Add(new AgentTask
            {
                Id = Guid.NewGuid(), AccessId = package, ClientUserId = client, AgentSlug = Slug, PackageId = Offer, Status = AgentTaskStatus.Running, InputText = Reopen.Input,
                Attempts = 3, CreatedAt = now.AddHours(-2), StartedAt = now.AddHours(-1), // on its last start and past the timeout: this run fails it
            }));

            // no exception from any of them: a lost lock race is a skip, never an error
            await Task.WhenAll(Task.Run(() => Reopen.Complete(host)), Task.Run(() => Reopen.Complete(host)), Task.Run(() => TaskFx.Job(host, client).RunOnceAsync(now, default)));

            await using var db = NewDb();
            var b = await db.Accesses.AsNoTracking().SingleAsync(x => x.Id == package);
            Assert.True((b.Status, b.UnitsUsed, b.FeeMinor) == (AccessStatus.Confirmed, 0L, null), $"round {round}: {b.Status}, used {b.UnitsUsed}, fee {b.FeeMinor}");
            Assert.Empty(await db.LedgerEntries.AsNoTracking().Where(l => l.AccessId == package && l.Kind == LedgerKind.PlatformFee).ToListAsync());
        }
    }
}
