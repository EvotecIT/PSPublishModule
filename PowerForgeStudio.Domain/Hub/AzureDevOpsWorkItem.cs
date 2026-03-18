namespace PowerForgeStudio.Domain.Hub;

public sealed record AzureDevOpsWorkItem(
    int Id,
    string Title,
    string State,
    string Type,
    string? AssignedTo,
    string? AreaPath,
    string? IterationPath)
{
    public string TypeDisplay => Type switch
    {
        "Bug" => "Bug",
        "Task" => "Task",
        "User Story" => "Story",
        "Feature" => "Feature",
        "Epic" => "Epic",
        _ => Type
    };
}
