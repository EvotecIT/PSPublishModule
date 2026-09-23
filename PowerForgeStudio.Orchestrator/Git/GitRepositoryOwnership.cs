namespace PowerForgeStudio.Orchestrator.Git;

/// <summary>Prevents a project folder from inheriting an enclosing repository's Git evidence.</summary>
internal static class GitRepositoryOwnership
{
    public static bool HasOwnWorkingCopy(string directory)
    {
        if (!Directory.Exists(directory)) return false;
        var metadata = Path.Combine(directory, ".git");
        return Directory.Exists(metadata) || File.Exists(metadata);
    }
}
