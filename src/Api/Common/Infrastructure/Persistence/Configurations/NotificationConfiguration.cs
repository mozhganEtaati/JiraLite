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

        // spec/18-database.md §Notification declares exactly one index here:
        // (RecipientUserId, IsRead, CreatedAtUtc). It seeks the unread-count query outright,
        // and for the recipient's list it still seeks on RecipientUserId — with IsRead
        // between the two, the CreatedAtUtc ordering costs a sort over that one user's rows,
        // which is what the spec accepts in exchange for one index instead of two.
        builder.HasIndex(n => new { n.RecipientUserId, n.IsRead, n.CreatedAtUtc });

        builder.HasOne<User>().WithMany().HasForeignKey(n => n.RecipientUserId).OnDelete(DeleteBehavior.NoAction);
    }
}
