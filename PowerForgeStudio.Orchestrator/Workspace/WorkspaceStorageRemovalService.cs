using System.Security.Cryptography;
using System.Text;
using PowerForge;
using PowerForgeStudio.Domain.Workspace;

namespace PowerForgeStudio.Orchestrator.Workspace;

/// <summary>Performs guarded, no-force removal of linked worktrees after exact evidence review.</summary>
public sealed class WorkspaceStorageRemovalService : IWorkspaceStorageRemovalService
{
    private static readonly string[] ArtifactDirectories =
        ["Artifacts", "Output", "out", "bin", "obj", Path.Combine("Build", "Packages")];
    private readonly GitClient _git;

    public WorkspaceStorageRemovalService(GitClient? git = null)
        => _git = git ?? new GitClient(defaultTimeout: TimeSpan.FromSeconds(30));

    public async Task<WorkspaceStorageRemovalReview> ReviewAsync(
        string workspaceRoot,
        WorkspaceStorageEntry entry,
        IReadOnlyCollection<string> protectedWorkingCopies,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(protectedWorkingCopies);
        var root = Path.GetFullPath(workspaceRoot);
        var primary = Path.GetFullPath(entry.PrimaryPath);
        var worktree = Path.GetFullPath(entry.Path);
        var findings = new List<string>();
        var containment = Directory.Exists(root) && IsWithin(root, worktree) && !PathsEqual(root, worktree) &&
                          !PathsEqual(primary, worktree);
        if (containment && HasLinkedPathBoundary(root, worktree))
            containment = false;
        if (!containment)
            findings.Add("The linked worktree must be a physical path inside the selected workspace and separate from its primary checkout.");
        if (!Directory.Exists(primary) || !Directory.Exists(worktree))
            findings.Add("The primary checkout and linked worktree must both exist.");

        var registered = false;
        var unlocked = false;
        var clean = false;
        var head = "";
        var remoteRef = "";
        var remoteSha = "";
        var remoteAncestry = false;
        if (Directory.Exists(primary) && Directory.Exists(worktree))
        {
            var registration = await ReadRegistrationAsync(primary, worktree, cancellationToken).ConfigureAwait(false);
            registered = registration.Exists;
            unlocked = registration.Exists && !registration.Locked;
            if (!registered)
                findings.Add("Git no longer reports this path as a linked worktree of the selected primary checkout.");
            else if (!unlocked)
                findings.Add("Git reports this linked worktree as locked. Unlock it explicitly before removal.");
            var status = await _git.GetStatusAsync(worktree, cancellationToken).ConfigureAwait(false);
            clean = status.IsGitRepository && status.CommandResult.Succeeded &&
                    status.TrackedChangeCount == 0 && status.UntrackedChangeCount == 0;
            if (!clean)
                findings.Add("The worktree has local changes or Git status could not be read.");
            var headResult = await _git.RunRawAsync(worktree, ["rev-parse", "--verify", "HEAD"], cancellationToken: cancellationToken).ConfigureAwait(false);
            if (headResult.Succeeded)
                head = headResult.StdOut.Trim();
            else
                findings.Add("The worktree HEAD could not be resolved.");
            var remote = await ReadRemoteDefaultAsync(primary, cancellationToken).ConfigureAwait(false);
            remoteRef = remote.Ref;
            remoteSha = remote.Sha;
            if (string.IsNullOrEmpty(remoteSha))
                findings.Add("The current origin default branch could not be resolved without changing local refs.");
            else if (!string.IsNullOrEmpty(head))
            {
                var objectProbe = await _git.RunRawAsync(primary,
                    ["cat-file", "-e", remoteSha + "^{commit}"], cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!objectProbe.Succeeded)
                    findings.Add("The current remote default commit is not available locally. Fetch before reviewing removal.");
                else
                {
                    var ancestry = await _git.RunRawAsync(worktree,
                        ["merge-base", "--is-ancestor", head, remoteSha], cancellationToken: cancellationToken).ConfigureAwait(false);
                    remoteAncestry = ancestry.Succeeded;
                    if (!remoteAncestry)
                        findings.Add("The exact worktree HEAD is not an ancestor of the current remote default branch.");
                }
            }
        }

        var studioUse = !protectedWorkingCopies.Any(path => PathsOverlap(path, worktree));
        if (!studioUse)
            findings.Add("PowerForge Studio is using this working copy in a selection, document, build, or release operation.");
        var artifacts = Directory.Exists(worktree)
            ? await ReadRetainedArtifactsAsync(worktree, findings, cancellationToken).ConfigureAwait(false)
            : [];
        var fingerprint = ComputeFingerprint(root, primary, worktree, head, remoteRef, remoteSha,
            containment, clean, registered, unlocked, remoteAncestry, studioUse, artifacts);
        return new(root, primary, worktree, head, remoteRef, remoteSha, containment, registered, unlocked, clean,
            remoteAncestry, studioUse, artifacts, findings, fingerprint, DateTimeOffset.UtcNow);
    }

    public async Task RemoveAsync(
        WorkspaceStorageRemovalReview reviewed,
        IReadOnlyCollection<string> protectedWorkingCopies,
        bool confirmNoExternalUse,
        bool confirmRetainedArtifacts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        ArgumentNullException.ThrowIfNull(protectedWorkingCopies);
        if (!reviewed.ReadyForConfirmation)
            throw new InvalidOperationException("The reviewed evidence does not permit removal.");
        if (!confirmNoExternalUse)
            throw new InvalidOperationException("Confirm that no external editor, terminal, task, or process is using this worktree.");
        if (reviewed.HasRetainedArtifacts && !confirmRetainedArtifacts)
            throw new InvalidOperationException("Review and confirm the retained artifact paths before removing this worktree.");
        var currentEntry = new WorkspaceStorageEntry(
            Path.GetFileName(reviewed.PrimaryPath), reviewed.WorktreePath, reviewed.PrimaryPath, "", false,
            Directory.Exists(reviewed.WorktreePath), false, 0, 0, 0, "", "", "", "", false, null);
        var current = await ReviewAsync(reviewed.WorkspaceRoot, currentEntry, protectedWorkingCopies, cancellationToken).ConfigureAwait(false);
        if (!current.ReadyForConfirmation || !string.Equals(current.Fingerprint, reviewed.Fingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("Worktree evidence changed after review. Refresh the removal review before continuing.");
        if (current.HasRetainedArtifacts && !confirmRetainedArtifacts)
            throw new InvalidOperationException("New retained artifacts appeared after review.");

        var result = await _git.RunRawAsync(current.PrimaryPath,
            ["worktree", "remove", "--", current.WorktreePath], cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
            throw new IOException("Git refused to remove the worktree. It may still be locked or in use.");
        if (Directory.Exists(current.WorktreePath) ||
            (await ReadRegistrationAsync(current.PrimaryPath, current.WorktreePath, CancellationToken.None).ConfigureAwait(false)).Exists)
            throw new IOException("Git reported success but the worktree path or registration still exists.");
    }

    private async Task<RegistrationEvidence> ReadRegistrationAsync(string primary, string worktree, CancellationToken token)
    {
        var result = await _git.RunRawAsync(primary, ["worktree", "list", "--porcelain"], cancellationToken: token).ConfigureAwait(false);
        if (!result.Succeeded)
            return new(false, false);
        string? path = null;
        var locked = false;
        foreach (var line in result.StdOut.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            if (line.StartsWith("worktree ", StringComparison.Ordinal))
            {
                if (path is not null && PathsEqual(path, worktree)) return new(true, locked);
                path = line[9..];
                locked = false;
            }
            else if (line.StartsWith("locked", StringComparison.Ordinal))
                locked = true;
            else if (line.Length == 0 && path is not null)
            {
                if (PathsEqual(path, worktree)) return new(true, locked);
                path = null;
                locked = false;
            }
        }
        return path is not null && PathsEqual(path, worktree) ? new(true, locked) : new(false, false);
    }

    private async Task<RemoteDefault> ReadRemoteDefaultAsync(string primary, CancellationToken token)
    {
        var result = await _git.RunRawAsync(primary, ["ls-remote", "--symref", "origin", "HEAD"], cancellationToken: token).ConfigureAwait(false);
        if (!result.Succeeded)
            return new("", "");
        string? reference = null;
        string? sha = null;
        foreach (var line in result.StdOut.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t', 2);
            if (parts.Length != 2 || parts[1] != "HEAD") continue;
            if (parts[0].StartsWith("ref: ", StringComparison.Ordinal)) reference = parts[0][5..];
            else sha = parts[0];
        }
        if (reference is null || !reference.StartsWith("refs/heads/", StringComparison.Ordinal) ||
            sha is null || !IsObjectId(sha))
            return new("", "");
        return new(reference, sha);
    }

    private async Task<IReadOnlyList<string>> ReadRetainedArtifactsAsync(
        string worktree,
        ICollection<string> findings,
        CancellationToken token)
    {
        var found = new HashSet<string>(PathComparer);
        foreach (var relative in ArtifactDirectories)
        {
            token.ThrowIfCancellationRequested();
            var path = Path.Combine(worktree, relative);
            if (!Directory.Exists(path)) continue;
            try
            {
                if (Directory.EnumerateFileSystemEntries(path).Any()) found.Add(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                found.Add(path);
            }
        }

        var ignored = await _git.RunRawAsync(worktree,
            ["status", "--porcelain=v1", "--ignored=matching", "--untracked-files=normal", "-z"],
            cancellationToken: token).ConfigureAwait(false);
        if (!ignored.Succeeded)
        {
            findings.Add("Ignored worktree content could not be inventoried.");
            return found.OrderBy(static path => path, PathComparer).ToArray();
        }

        foreach (var record in ignored.StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            token.ThrowIfCancellationRequested();
            if (!record.StartsWith("!! ", StringComparison.Ordinal) || record.Length <= 3) continue;
            var relative = record[3..].Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            var separator = relative.IndexOf(Path.DirectorySeparatorChar);
            var reviewRoot = separator >= 0 ? relative[..separator] : relative.TrimEnd(Path.DirectorySeparatorChar);
            if (!string.IsNullOrWhiteSpace(reviewRoot)) found.Add(Path.GetFullPath(Path.Combine(worktree, reviewRoot)));
        }
        return found.OrderBy(static path => path, PathComparer).ToArray();
    }

    private static string ComputeFingerprint(string root, string primary, string worktree, string head,
        string remoteRef, string remoteSha, bool containment, bool clean, bool registered, bool unlocked, bool remoteAncestry,
        bool studioUse, IReadOnlyList<string> artifacts)
    {
        var value = string.Join('\n', new[] { root, primary, worktree, head, remoteRef, remoteSha,
            containment.ToString(), clean.ToString(), registered.ToString(), unlocked.ToString(), remoteAncestry.ToString(), studioUse.ToString() }
            .Concat(artifacts.OrderBy(static path => path, PathComparer)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static bool IsObjectId(string value)
        => value.Length is 40 or 64 && value.All(Uri.IsHexDigit);

    private static bool PathsOverlap(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left)) return false;
        var first = Path.GetFullPath(left);
        var second = Path.GetFullPath(right);
        return PathsEqual(first, second) || IsWithin(first, second) || IsWithin(second, first);
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), Comparison);

    private static bool HasLinkedPathBoundary(string root, string path)
    {
        try
        {
            var current = Path.GetFullPath(path);
            while (!PathsEqual(current, root))
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    return true;
                current = Directory.GetParent(current)?.FullName ?? "";
                if (string.IsNullOrEmpty(current) || !IsWithin(root, current) && !PathsEqual(root, current))
                    return true;
            }
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static bool IsWithin(string root, string path)
        => Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, Comparison);

    private static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private sealed record RemoteDefault(string Ref, string Sha);
    private sealed record RegistrationEvidence(bool Exists, bool Locked);
}
