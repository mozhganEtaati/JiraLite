using JiraLite.Api.Common.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JiraLite.Api.Common.Infrastructure.Persistence.Configurations;

public class IssueConfiguration : IEntityTypeConfiguration<Issue>
{
    public void Configure(EntityTypeBuilder<Issue> builder)
    {
        builder.ToTable("Issue");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Key).HasMaxLength(20).IsRequired();
        builder.Property(i => i.Type).HasMaxLength(20).IsRequired();
        builder.Property(i => i.Title).HasMaxLength(255).IsRequired();
        builder.Property(i => i.Description).HasMaxLength(50000);
        builder.Property(i => i.Priority).HasMaxLength(20).IsRequired();
        builder.Property(i => i.Rank).HasMaxLength(255).IsRequired();
        builder.Property(i => i.DueDateUtc).HasColumnType("date");
        builder.Property(i => i.Estimate).HasColumnType("decimal(5,2)");
        builder.Property(i => i.CreatedAtUtc).IsRequired();
        builder.Property(i => i.UpdatedAtUtc).IsRequired();
        builder.Property(i => i.RowVersion).IsRowVersion();

        builder.HasIndex(i => new { i.ProjectId, i.Number }).IsUnique();
        builder.HasIndex(i => new { i.ProjectId, i.BoardColumnId });
        builder.HasIndex(i => new { i.ProjectId, i.SprintId, i.Rank });
        builder.HasIndex(i => new { i.ProjectId, i.AssigneeUserId });
        builder.HasIndex(i => new { i.ProjectId, i.DueDateUtc });

        // spec/18-database.md §9 — every FK on Issue is NO ACTION; cross-entity deletion/nullification
        // (Project delete cascade, Sprint delete nulling SprintId, Epic delete detaching children,
        // Board/Column delete guards) is executed explicitly in application code instead.
        builder.HasOne<Project>().WithMany().HasForeignKey(i => i.ProjectId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<BoardColumn>().WithMany().HasForeignKey(i => i.BoardColumnId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Sprint>().WithMany().HasForeignKey(i => i.SprintId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Issue>().WithMany().HasForeignKey(i => i.ParentIssueId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>().WithMany().HasForeignKey(i => i.AssigneeUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>().WithMany().HasForeignKey(i => i.ReporterUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>().WithMany().HasForeignKey(i => i.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>().WithMany().HasForeignKey(i => i.UpdatedByUserId).OnDelete(DeleteBehavior.NoAction);
    }
}
