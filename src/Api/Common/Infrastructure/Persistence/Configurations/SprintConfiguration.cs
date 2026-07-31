using JiraLite.Api.Common.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JiraLite.Api.Common.Infrastructure.Persistence.Configurations;

public class SprintConfiguration : IEntityTypeConfiguration<Sprint>
{
    public void Configure(EntityTypeBuilder<Sprint> builder)
    {
        builder.ToTable("Sprint");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Goal).HasMaxLength(500);
        builder.Property(s => s.Status).HasMaxLength(20).IsRequired();
        builder.Property(s => s.PlannedStartDateUtc).HasColumnType("date").IsRequired();
        builder.Property(s => s.PlannedEndDateUtc).HasColumnType("date").IsRequired();
        builder.Property(s => s.CreatedAtUtc).IsRequired();

        // spec/08-sprints.md BR-01 + NFR-01: DB-enforced atomicity for "at most one Active
        // Sprint per Board" — a filtered unique index closes the race a check-then-insert
        // in application code alone cannot, under concurrent StartSprint calls.
        builder.HasIndex(s => s.BoardId)
            .IsUnique()
            .HasFilter("[Status] = N'Active'")
            .HasDatabaseName("IX_Sprint_BoardId_ActiveOnly");

        builder.HasOne<Board>().WithMany().HasForeignKey(s => s.BoardId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Project>().WithMany().HasForeignKey(s => s.ProjectId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
    }
}
