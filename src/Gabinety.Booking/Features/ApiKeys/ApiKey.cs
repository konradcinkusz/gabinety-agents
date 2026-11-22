using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gabinety.Booking.Features.ApiKeys;

/// <summary>
/// An API key of a client (docs/agentic/PLAN.md 2.2). The plain key is never stored: only its SHA-256 (<see cref="Hash"/>) and the 8 characters after
/// <c>gab_</c> (<see cref="Prefix"/>), which let the owner tell keys apart.
/// </summary>
public sealed class ApiKey
{
    public Guid Id { get; set; }
    public string ClientUserId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Prefix { get; set; } = "";
    public string Hash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> b)
    {
        b.ToTable("api_keys");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.ClientUserId).HasColumnName("client_user_id").HasMaxLength(64);
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(60);
        b.Property(x => x.Prefix).HasColumnName("prefix").HasMaxLength(8);
        b.Property(x => x.Hash).HasColumnName("hash").HasMaxLength(64);
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.LastUsedAt).HasColumnName("last_used_at");
        b.Property(x => x.RevokedAt).HasColumnName("revoked_at");
        b.HasIndex(x => x.Hash).IsUnique();
        b.HasIndex(x => x.ClientUserId);
    }
}
