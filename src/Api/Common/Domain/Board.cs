namespace JiraLite.Api.Common.Domain;

/// <summary>Visual arrangement of a Project's Issues. spec/18-database.md §6, spec/06-boards.md.</summary>
public class Board
{
    public Guid Id { get; init; }
    public Guid ProjectId { get; init; }
    public required string Name { get; set; }
    public required string Type { get; init; }
    public int DisplayOrder { get; set; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; set; }
}
