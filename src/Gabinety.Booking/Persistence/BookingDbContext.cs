using Gabinety.Booking.Features.AgentApi;
using Gabinety.Booking.Features.ApiKeys;
using Gabinety.Booking.Features.Bookings.Create;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Features.Payouts;
using Gabinety.Booking.Notifications;
using Microsoft.EntityFrameworkCore;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Booking;

namespace Gabinety.Booking.Persistence;

/// <summary>Owns bookingdb (P3). The other Booking tables arrive with later tasks.</summary>
public sealed class BookingDbContext(DbContextOptions<BookingDbContext> options) : DbContext(options)
{
    public DbSet<BookingEntity> Bookings => Set<BookingEntity>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<Payout> Payouts => Set<Payout>();
    public DbSet<PaymentAccount> PaymentAccounts => Set<PaymentAccount>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<UsageRecord> UsageRecords => Set<UsageRecord>();
    public DbSet<AgentTask> AgentTasks => Set<AgentTask>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BookingDbContext).Assembly);
}

/// <summary>Only for `dotnet ef`; never used at runtime and never opens a connection.</summary>
public sealed class BookingDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<BookingDbContext>
{
    public BookingDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<BookingDbContext>()
        .UseNpgsql("Host=localhost;Database=design-time", o => o.MigrationsAssembly(typeof(BookingDbContext).Assembly.GetName().Name)).Options);
}
