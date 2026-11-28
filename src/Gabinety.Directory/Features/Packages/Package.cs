using Gabinety.Contracts.Directory;
using Gabinety.Directory.Features.Profiles;
using Gabinety.Directory.Features.Taxonomy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gabinety.Directory.Features.Packages;

/// <summary>Row of `offerings` (CONTRACTS 3.1). Deactivated, never deleted: bookings keep pointing at it.</summary>
public sealed class Package
{
    public Guid Id { get; set; }
    public Guid AgentId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public short DurationMinutes { get; set; }
    public long PriceMinor { get; set; }
    public string Currency { get; set; } = "PLN";
    public string ProblemSlug { get; set; } = "";
    public string[] TechSlugs { get; set; } = [];
    public string? MeetingUrl { get; set; }
    public PackageKind Kind { get; set; } = PackageKind.Tasks;
    public int Units { get; set; } = 1;
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PackageConfiguration : IEntityTypeConfiguration<Package>
{
    public void Configure(EntityTypeBuilder<Package> b)
    {
        b.ToTable("offerings");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.AgentId).HasColumnName("practitioner_id");
        b.Property(x => x.Title).HasColumnName("title").HasMaxLength(80).IsRequired();
        b.Property(x => x.Description).HasColumnName("description").HasMaxLength(600).IsRequired();
        b.Property(x => x.DurationMinutes).HasColumnName("duration_minutes").HasColumnType("smallint");
        b.Property(x => x.PriceMinor).HasColumnName("price_minor");
        b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        b.Property(x => x.ProblemSlug).HasColumnName("problem_slug").HasMaxLength(60).IsRequired();
        b.Property(x => x.TechSlugs).HasColumnName("tech_slugs").HasColumnType("text[]"); // a list on InMemory, text[] on PostgreSQL
        b.Property(x => x.MeetingUrl).HasColumnName("meeting_url").HasMaxLength(300);
        b.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(10).HasDefaultValue(PackageKind.Tasks).IsRequired();
        b.Property(x => x.Units).HasColumnName("units").HasDefaultValue(1);
        b.Property(x => x.IsActive).HasColumnName("is_active");
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.HasOne<Agent>().WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Category>().WithMany().HasForeignKey(x => x.ProblemSlug).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.ProblemSlug, x.IsActive });
        b.HasIndex(x => x.TechSlugs).HasMethod("gin"); // `tech_slugs @> ARRAY[...]`; the annotation only matters to the Npgsql provider
        b.HasIndex(x => x.AgentId);
    }
}
