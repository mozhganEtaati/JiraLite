namespace JiraLite.Api.Common.Domain;

/// <summary>spec/06-boards.md — Board.Type values.</summary>
public static class BoardType
{
    public const string Scrum = "Scrum";
    public const string Kanban = "Kanban";

    public static readonly string[] All = [Scrum, Kanban];
}
