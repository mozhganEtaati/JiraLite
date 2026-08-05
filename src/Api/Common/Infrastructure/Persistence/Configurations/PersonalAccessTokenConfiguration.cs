using JiraLite.Api.Common.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JiraLite.Api.Common.Infrastructure.Persistence.Configurations;

public class PersonalAccessTokenConfiguration : IEntityTypeConfiguration<PersonalAccessToken>
{
    public void Configure(EntityTypeBuilder<PersonalAccessToken> builder)
    {
        builder.ToTable("PersonalAccessToken");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).HasMaxLength(100).IsRequired();
        builder.Property(t => t.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(t => t.CreatedAtUtc).IsRequired();
        builder.Property(t => t.ExpiresAtUtc).IsRequired();

        builder.Ignore(t => t.IsActive);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Authentication looks the presented token up by hash and nothing else, so this index
        // carries every /mcp request. Unique because two rows sharing a hash would mean a
        // collision in the token generator, not a legitimate state.
        builder.HasIndex(t => t.TokenHash).IsUnique();

        // Active-token listing and the BR-05 count of 10.
        builder.HasIndex(t => new { t.UserId, t.RevokedAtUtc });
    }
}
