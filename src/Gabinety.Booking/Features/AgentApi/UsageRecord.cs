using Gabinety.Contracts.Directory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gabinety.Booking.Features.AgentApi;

/// <summary>
/// One consumption of a package through the public API (docs/agentic/PLAN.md B-02): numbers only, never any content (rule 6). <see cref="Units"/> is tokens for
/// <see cref="OfferingKind.Chat"/> and tasks for <see cref="OfferingKind.Tasks"/>. <see cref="ApiKeyId"/> is null when a signed-in client used the web panel.
/// </summary>
public sealed class UsageRecord
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public Guid? ApiKeyId { get; set; }
    public OfferingKind Kind { get; set; }
    public long Units { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

sealed class UsageRecordConfiguration : IEntityTypeConfiguration<UsageRecord>
{
    public void Configure(EntityTypeBuilder<UsageRecord> b)
    {
        b.ToTable("usage_records");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.BookingId).HasColumnName("booking_id");
        b.Property(x => x.ApiKeyId).HasColumnName("api_key_id");
        b.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.Units).HasColumnName("units");
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.HasIndex(x => new { x.BookingId, x.CreatedAt });
    }
}
