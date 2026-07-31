using JiraLite.Api.Common.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JiraLite.Api.Common.Infrastructure.Persistence.Configurations;

public class BoardConfiguration : IEntityTypeConfiguration<Board>
{
    public void Configure(EntityTypeBuilder<Board> builder)
    {
        builder.ToTable("Board");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Name).HasMaxLength(100).IsRequired();
        builder.Property(b => b.Type).HasMaxLength(20).IsRequired();
        builder.Property(b => b.DisplayOrder).IsRequired();
        builder.Property(b => b.CreatedAtUtc).IsRequired();
        builder.Property(b => b.UpdatedAtUtc).IsRequired();

        builder.HasOne<Project>().WithMany().HasForeignKey(b => b.ProjectId).OnDelete(DeleteBehavior.NoAction);
    }
}
