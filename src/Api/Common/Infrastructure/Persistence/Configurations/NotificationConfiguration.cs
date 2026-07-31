using JiraLite.Api.Common.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JiraLite.Api.Common.Infrastructure.Persistence.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notification");
        builder.HasKey(n => n.Id);

        builder.Property(n => n.Type).HasMaxLength(30).IsRequired();
        builder.Property(n => n.Summary).HasMaxLength(500).IsRequired();
        builder.Property(n => n.EntityType).HasMaxLength(50).IsRequired();
        builder.Property(n => n.IsRead).IsRequired();
        builder.Property(n => n.CreatedAtUtc).IsRequired();

        builder.HasIndex(n => new { n.RecipientUserId, n.CreatedAtUtc });
        builder.HasIndex(n => new { n.RecipientUserId, n.IsRead });

        builder.HasOne<User>().WithMany().HasForeignKey(n => n.RecipientUserId).OnDelete(DeleteBehavior.NoAction);
    }
}
