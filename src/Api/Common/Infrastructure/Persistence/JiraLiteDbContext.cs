using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Common.Infrastructure.Persistence;

public class JiraLiteDbContext(DbContextOptions<JiraLiteDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(JiraLiteDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
