namespace PowerForgeStudio.Orchestrator.Catalog;

/// <summary>
/// Shared worktree detection logic. Checks the .git entry to determine
/// if a directory is a git worktree (linked to a parent repository).
/// </summary>
public static class WorktreeDetector
{
    /// <summary>
    /// Returns true if the directory is a git worktree (has a .git file with gitdir: reference).
    /// </summary>
    public static bool IsWorktree(string directoryPath)
    {
        var gitPath = Path.Combine(directoryPath, ".git");

        try
        {
            if (!File.Exists(gitPath))
            {
                return false;
            }

            var attributes = File.GetAttributes(gitPath);
            if (attributes.HasFlag(FileAttributes.Directory))
            {
                return false;
            }

            var content = File.ReadAllText(gitPath).Trim();
            return content.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns true if the directory has a .git entry (either directory or file).
    /// Use this to filter out non-git directories from project lists.
    /// </summary>
    public static bool IsGitRepository(string directoryPath)
    {
        var gitPath = Path.Combine(directoryPath, ".git");
        return Directory.Exists(gitPath) || File.Exists(gitPath);
    }

    /// <summary>
    /// Resolves the parent repository name from a worktree's .git file.
    /// Returns null if not a worktree or if the parent can't be determined.
    /// </summary>
    public static string? ResolveParentRepositoryName(string worktreePath)
    {
        var gitFilePath = Path.Combine(worktreePath, ".git");

        try
        {
            if (!File.Exists(gitFilePath))
            {
                return null;
            }

            var content = File.ReadAllText(gitFilePath).Trim();
            if (!content.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // Format: "gitdir: C:/Support/GitHub/ParentRepo/.git/worktrees/WorktreeName"
            var gitdir = content["gitdir:".Length..].Trim().Replace('\\', '/');
            var worktreesIndex = gitdir.IndexOf("/.git/worktrees/", StringComparison.OrdinalIgnoreCase);
            if (worktreesIndex >= 0)
            {
                var parentRepoPath = gitdir[..worktreesIndex];
                return Path.GetFileName(parentRepoPath);
            }
        }
        catch
        {
            // Ignore read errors
        }

        return null;
    }
}
