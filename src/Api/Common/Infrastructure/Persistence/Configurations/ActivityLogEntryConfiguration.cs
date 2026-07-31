using JiraLite.Api.Common.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JiraLite.Api.Common.Infrastructure.Persistence.Configurations;

public class ActivityLogEntryConfiguration : IEntityTypeConfiguration<ActivityLogEntry>
{
    public void Configure(EntityTypeBuilder<ActivityLogEntry> builder)
    {
        builder.ToTable("ActivityLogEntry");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.EntityType).HasMaxLength(50).IsRequired();
        builder.Property(e => e.Action).HasMaxLength(50).IsRequired();
        builder.Property(e => e.Summary).HasMaxLength(500).IsRequired();
        builder.Property(e => e.OccurredAtUtc).IsRequired();

        builder.HasIndex(e => new { e.WorkspaceId, e.OccurredAtUtc });
        builder.HasIndex(e => new { e.ActorUserId, e.OccurredAtUtc });

        builder.HasOne<User>().WithMany().HasForeignKey(e => e.ActorUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Workspace>().WithMany().HasForeignKey(e => e.WorkspaceId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Project>().WithMany().HasForeignKey(e => e.ProjectId).OnDelete(DeleteBehavior.NoAction);
    }
}
