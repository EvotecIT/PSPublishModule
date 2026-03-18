namespace PowerForgeStudio.Domain.Hub;

public sealed record GitFileChange(
    string Path,
    GitChangeKind Kind,
    string? DiffContent = null)
{
    public string KindDisplay => Kind switch
    {
        GitChangeKind.Added => "A",
        GitChangeKind.Modified => "M",
        GitChangeKind.Deleted => "D",
        GitChangeKind.Renamed => "R",
        GitChangeKind.Copied => "C",
        GitChangeKind.Untracked => "?",
        _ => "?"
    };
}
