using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gabinety.Booking.Features.AgentApi;

public enum AgentTaskStatus { Queued, Running, Succeeded, Failed }

/// <summary>
/// One task ordered from an agent (docs/agentic/PLAN.md B-05). <see cref="InputText"/> and <see cref="OutputText"/> are user free text: stored, shown to the owner,
/// erased with the user, and never logged, never in a metric, an exception message or an error answer (rule 6).
/// </summary>
public sealed class AgentTask
{
    public Guid Id { get; set; }
    public Guid AccessId { get; set; }
    public string ClientUserId { get; set; } = "";
    public string AgentSlug { get; set; } = "";
    public Guid PackageId { get; set; }
    public AgentTaskStatus Status { get; set; }
    public string InputText { get; set; } = "";
    public string? OutputText { get; set; }
    /// <summary>How many times the platform was called for this task (1 after the first start, at most 3).</summary>
    public int Attempts { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

sealed class AgentTaskConfiguration : IEntityTypeConfiguration<AgentTask>
{
    public void Configure(EntityTypeBuilder<AgentTask> b)
    {
        b.ToTable("agent_tasks");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.AccessId).HasColumnName("booking_id");
        b.Property(x => x.ClientUserId).HasColumnName("client_user_id").HasMaxLength(64);
        b.Property(x => x.AgentSlug).HasColumnName("practitioner_slug").HasMaxLength(60);
        b.Property(x => x.PackageId).HasColumnName("offering_id");
        b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.InputText).HasColumnName("input_text").HasMaxLength(20000);
        b.Property(x => x.OutputText).HasColumnName("output_text");
        b.Property(x => x.Attempts).HasColumnName("attempts");
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.StartedAt).HasColumnName("started_at");
        b.Property(x => x.CompletedAt).HasColumnName("completed_at");
        b.HasIndex(x => new { x.Status, x.CreatedAt });
        b.HasIndex(x => new { x.AccessId, x.CreatedAt });
    }
}
