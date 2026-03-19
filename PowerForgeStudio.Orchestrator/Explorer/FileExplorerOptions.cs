namespace PowerForgeStudio.Orchestrator.Explorer;

public sealed class FileExplorerOptions
{
    public IReadOnlySet<string> ExcludedFolders { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        "node_modules",
        "bin",
        "obj",
        ".vs",
        ".idea",
        "__pycache__",
        "packages"
    };

    public bool ShowHiddenFiles { get; init; } = false;
}
