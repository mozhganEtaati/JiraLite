using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Persistence;

/// <summary>
/// Task T046: every composite index spec/18-database.md names must actually exist in the
/// migrated schema, with the specced column order — order is what decides whether a query
/// seeks or scans, so an index with the right columns in the wrong order is a regression the
/// EF model snapshot alone will not catch.
/// </summary>
public class IndexCoverageTests : IClassFixture<JiraLiteApiFactory>
{
    private readonly JiraLiteApiFactory _factory;

    public IndexCoverageTests(JiraLiteApiFactory factory) => _factory = factory;

    public static TheoryData<string, string[], bool> SpeccedIndexes() => new()
    {
        // §Issue
        { "Issue", ["ProjectId", "Number"], true },
        { "Issue", ["ProjectId", "BoardColumnId"], false },
        { "Issue", ["ProjectId", "SprintId", "Rank"], false },
        { "Issue", ["ProjectId", "AssigneeUserId"], false },
        { "Issue", ["ProjectId", "DueDateUtc"], false },
        // §ActivityLogEntry
        { "ActivityLogEntry", ["WorkspaceId", "OccurredAtUtc"], false },
        { "ActivityLogEntry", ["ActorUserId", "OccurredAtUtc"], false },
        // §Notification
        { "Notification", ["RecipientUserId", "IsRead", "CreatedAtUtc"], false },
        // §RefreshToken
        { "RefreshToken", ["UserId", "RevokedAtUtc"], false },
        // §PersonalAccessToken — the unique hash index carries every /mcp request's authentication
        { "PersonalAccessToken", ["TokenHash"], true },
        { "PersonalAccessToken", ["UserId", "RevokedAtUtc"], false },
        // §PasswordResetToken — the unique hash index is the redemption lookup
        { "PasswordResetToken", ["TokenHash"], true },
        { "PasswordResetToken", ["UserId", "UsedAtUtc"], false },
        // §User / §Invitation — single-column, but uniqueness is the business rule
        { "User", ["Email"], true },
        { "Invitation", ["Token"], true }
    };

    [Theory]
    [MemberData(nameof(SpeccedIndexes))]
    public async Task Specced_index_exists_with_the_specced_column_order(string table, string[] columns, bool unique)
    {
        var actual = await ReadIndexesAsync(table);

        var match = actual.SingleOrDefault(index => index.Columns.SequenceEqual(columns));

        Assert.True(
            match is not null,
            $"No index on [{table}] over ({string.Join(", ", columns)}) in that order. Found: " +
            string.Join(" | ", actual.Select(i => $"{i.Name}({string.Join(",", i.Columns)})")));
        Assert.Equal(unique, match!.IsUnique);
    }

    private sealed record IndexInfo(string Name, string[] Columns, bool IsUnique);

    private async Task<List<IndexInfo>> ReadIndexesAsync(string table)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();

        // Key columns only, in key order — INCLUDEd columns (is_included_column = 1) do not
        // affect seekability and are deliberately excluded from the comparison.
        const string sql = """
            SELECT i.name AS IndexName, i.is_unique AS IsUnique, c.name AS ColumnName, ic.key_ordinal
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID(@table) AND ic.is_included_column = 0
            ORDER BY i.name, ic.key_ordinal
            """;

        var rows = new List<(string Index, bool Unique, string Column)>();
        var connection = (SqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@table", table);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add((reader.GetString(0), reader.GetBoolean(1), reader.GetString(2)));
            }
        }
        finally
        {
            await connection.CloseAsync();
        }

        return rows
            .GroupBy(r => (r.Index, r.Unique))
            .Select(g => new IndexInfo(g.Key.Index, g.Select(r => r.Column).ToArray(), g.Key.Unique))
            .ToList();
    }
}
