using JiraLite.Api.Common.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JiraLite.Api.Common.Infrastructure.Persistence.Configurations;

public class LabelConfiguration : IEntityTypeConfiguration<Label>
{
    public void Configure(EntityTypeBuilder<Label> builder)
    {
        builder.ToTable("Label");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Name).HasMaxLength(50).IsRequired();
        builder.Property(l => l.Color).HasMaxLength(7).IsRequired();
        builder.Property(l => l.CreatedAtUtc).IsRequired();

        builder.HasIndex(l => new { l.ProjectId, l.Name }).IsUnique();

        builder.HasOne<Project>().WithMany().HasForeignKey(l => l.ProjectId).OnDelete(DeleteBehavior.Cascade);
    }
}
