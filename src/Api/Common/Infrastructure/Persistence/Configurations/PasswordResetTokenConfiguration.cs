using JiraLite.Api.Common.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JiraLite.Api.Common.Infrastructure.Persistence.Configurations;

public class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.ToTable("PasswordResetToken");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(t => t.ExpiresAtUtc).IsRequired();
        builder.Property(t => t.CreatedAtUtc).IsRequired();

        builder.Ignore(t => t.IsActive);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Redemption looks the presented token up by hash and nothing else. Unique because two
        // rows sharing a hash would mean a collision in the generator, not a legitimate state.
        builder.HasIndex(t => t.TokenHash).IsUnique();

        // BR-10: requesting a new link invalidates any outstanding one, which needs the user's
        // unused tokens.
        builder.HasIndex(t => new { t.UserId, t.UsedAtUtc });
    }
}
