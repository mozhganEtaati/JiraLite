using JiraLite.Api.Common.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JiraLite.Api.Common.Infrastructure.Persistence.Configurations;

public class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.ToTable("Attachment");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.FileName).HasMaxLength(255).IsRequired();
        builder.Property(a => a.StorageKey).HasMaxLength(512).IsRequired();
        builder.Property(a => a.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(a => a.CreatedAtUtc).IsRequired();

        builder.HasIndex(a => a.IssueId);

        builder.HasOne<Issue>().WithMany().HasForeignKey(a => a.IssueId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.UploadedByUserId).OnDelete(DeleteBehavior.NoAction);
    }
}
