namespace JiraLite.Api.Common.Domain;

/// <summary>Status lane on a Board. spec/18-database.md §6, spec/06-boards.md.</summary>
public class BoardColumn
{
    public Guid Id { get; init; }
    public Guid BoardId { get; init; }
    public required string Name { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsDefault { get; set; }
    public bool IsDoneColumn { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
