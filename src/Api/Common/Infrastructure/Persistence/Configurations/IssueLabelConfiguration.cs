using JiraLite.Api.Common.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JiraLite.Api.Common.Infrastructure.Persistence.Configurations;

public class IssueLabelConfiguration : IEntityTypeConfiguration<IssueLabel>
{
    public void Configure(EntityTypeBuilder<IssueLabel> builder)
    {
        builder.ToTable("IssueLabel");
        builder.HasKey(il => new { il.IssueId, il.LabelId });

        builder.HasOne<Issue>().WithMany().HasForeignKey(il => il.IssueId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Label>().WithMany().HasForeignKey(il => il.LabelId).OnDelete(DeleteBehavior.Cascade);
    }
}
