using JiraLite.Api.Common.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JiraLite.Api.Common.Infrastructure.Persistence.Configurations;

public class BoardColumnConfiguration : IEntityTypeConfiguration<BoardColumn>
{
    public void Configure(EntityTypeBuilder<BoardColumn> builder)
    {
        builder.ToTable("BoardColumn");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.Property(c => c.DisplayOrder).IsRequired();
        builder.Property(c => c.IsDefault).IsRequired();
        builder.Property(c => c.IsDoneColumn).IsRequired();
        builder.Property(c => c.RowVersion).IsRowVersion();

        builder.HasOne<Board>().WithMany().HasForeignKey(c => c.BoardId).OnDelete(DeleteBehavior.Cascade);
    }
}
