namespace PowerForgeStudio.Domain.Hub;

public sealed record GitHubIssue(
    int Number,
    string Title,
    string State,
    string? AuthorLogin,
    IReadOnlyList<string> Labels,
    IReadOnlyList<string> Assignees,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt,
    string? HtmlUrl = null,
    string? BodyMarkdown = null)
{
    public bool IsOpen => string.Equals(State, "open", StringComparison.OrdinalIgnoreCase);

    public string StateDisplay => IsOpen ? "Open" : "Closed";

    public string LabelDisplay => Labels.Count == 0 ? "" : string.Join(", ", Labels);

    public string AssigneeDisplay => Assignees.Count == 0 ? "Unassigned" : string.Join(", ", Assignees);
}
