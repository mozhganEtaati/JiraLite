using JiraLite.Api.Common.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JiraLite.Api.Common.Infrastructure.Persistence.Configurations;

public class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        builder.ToTable("Comment");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Body).HasMaxLength(10000).IsRequired();
        builder.Property(c => c.CreatedAtUtc).IsRequired();

        builder.HasIndex(c => new { c.IssueId, c.CreatedAtUtc });

        builder.HasOne<Issue>().WithMany().HasForeignKey(c => c.IssueId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.AuthorUserId).OnDelete(DeleteBehavior.NoAction);
    }
}
