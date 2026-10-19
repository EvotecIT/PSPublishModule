namespace PowerForge;

/// <summary>Reads build source context without imposing clean-source attestation.</summary>
internal static class ReleaseSourceCommit
{
    /// <summary>Captures an immutable HEAD for a checkout, or null for non-Git input.</summary>
    internal static string? Capture(string root, CancellationToken cancellationToken = default)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(root));
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, ".git")) &&
               !Directory.Exists(Path.Combine(directory.FullName, ".git")))
            directory = directory.Parent;
        if (directory is null)
            return null;

        // Read the selected checkout, independent of inherited GIT_DIR/configuration.
        // This is only a commit lookup; it does not require a clean working tree.
        var git = GitClient.CreateIsolatedPathClient();
        var result = git.RunRawAsync(
            root, new[] { "rev-parse", "--verify", "--quiet", "HEAD^{commit}" },
            cancellationToken: cancellationToken).GetAwaiter().GetResult();
        cancellationToken.ThrowIfCancellationRequested();
        var commit = result.StdOut.Trim();
        if (result.ExitCode == 0 && !result.TimedOut && GitObjectId.IsFull(commit))
            return commit;

        // A newly initialized checkout is a supported build input, but has no
        // immutable commit to pin. Distinguish it from a broken ref/object store.
        var branch = git.RunRawAsync(root, new[] { "symbolic-ref", "--quiet", "HEAD" },
            cancellationToken: cancellationToken).GetAwaiter().GetResult();
        if (branch.ExitCode == 0 && !branch.TimedOut &&
            branch.StdOut.Trim().StartsWith("refs/heads/", StringComparison.Ordinal))
        {
            var reference = git.RunRawAsync(root,
                new[] { "show-ref", "--verify", "--quiet", branch.StdOut.Trim() },
                cancellationToken: cancellationToken).GetAwaiter().GetResult();
            if (reference.ExitCode == 1 && !reference.TimedOut)
                return null;
        }
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException("Cannot resolve the Git commit for the release source checkout.");
    }
}
