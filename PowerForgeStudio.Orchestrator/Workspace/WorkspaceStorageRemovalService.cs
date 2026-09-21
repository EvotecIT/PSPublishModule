using System.Security.Cryptography;
using System.Text;
using PowerForge;
using PowerForgeStudio.Domain.Workspace;
using PowerForgeStudio.Orchestrator.Hub;

namespace PowerForgeStudio.Orchestrator.Workspace;

/// <summary>Performs guarded, no-force removal of linked worktrees after exact evidence review.</summary>
public sealed class WorkspaceStorageRemovalService : IWorkspaceStorageRemovalService, IDisposable
{
    private static readonly string[] ArtifactDirectories =
        ["Artifacts", "Output", "out", "bin", "obj", Path.Combine("Build", "Packages")];
    private readonly GitClient _git;
    private readonly IGitHubProjectService _gitHub;
    private readonly IWorkspaceExternalUseInspectionService _externalUse;
    private readonly bool _ownsGitHub;

    internal Action? BeforePruneGuardAcquisition { get; init; }
    internal Action? BeforePruneAdministrativeMove { get; init; }

    public WorkspaceStorageRemovalService(
        GitClient? git = null,
        IGitHubProjectService? gitHub = null,
        IWorkspaceExternalUseInspectionService? externalUse = null)
    {
        _git = git ?? new GitClient(defaultTimeout: TimeSpan.FromSeconds(30));
        _gitHub = gitHub ?? new GitHubProjectService();
        _ownsGitHub = gitHub is null;
        _externalUse = externalUse ?? new WorkspaceExternalUseInspectionService();
    }

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
        int? mergedPullRequestNumber = null;
        string? mergedPullRequestUrl = null;
        var mergedPullRequest = false;
        string? mergeEvidenceWarning = null;
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
                mergeEvidenceWarning = "The current origin default branch could not be resolved without changing local refs.";
            else if (!string.IsNullOrEmpty(head))
            {
                var objectProbe = await _git.RunRawAsync(primary,
                    ["cat-file", "-e", remoteSha + "^{commit}"], cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!objectProbe.Succeeded)
                    mergeEvidenceWarning = "The current remote default commit is not available locally.";
                else
                {
                    var ancestry = await _git.RunRawAsync(worktree,
                        ["merge-base", "--is-ancestor", head, remoteSha], cancellationToken: cancellationToken).ConfigureAwait(false);
                    remoteAncestry = ancestry.Succeeded;
                }

                if (!remoteAncestry && !string.IsNullOrWhiteSpace(remoteRef))
                {
                    try
                    {
                        var slug = await _gitHub.ResolveRepositoryAsync(worktree, cancellationToken).ConfigureAwait(false);
                        var baseBranch = remoteRef["refs/heads/".Length..];
                        if (slug is not null)
                        {
                            var pull = await _gitHub.FindMergedPullRequestByHeadAsync(
                                slug, head, baseBranch, cancellationToken).ConfigureAwait(false);
                            if (pull is not null)
                            {
                                mergedPullRequest = true;
                                mergedPullRequestNumber = pull.Number;
                                mergedPullRequestUrl = pull.HtmlUrl;
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex) when (ex is GitHubAccessException or InvalidDataException or IOException or HttpRequestException)
                    {
                        mergeEvidenceWarning = "GitHub merged-PR evidence could not be refreshed.";
                    }
                }
            }
        }

        if (!remoteAncestry && !mergedPullRequest)
        {
            findings.Add("The exact worktree HEAD is neither an ancestor of the current remote default branch nor the recorded head of a merged pull request into that branch.");
            if (mergeEvidenceWarning is not null) findings.Add(mergeEvidenceWarning);
        }

        var studioUse = !protectedWorkingCopies.Any(path => PathsOverlap(path, worktree));
        if (!studioUse)
            findings.Add("PowerForge Studio is using this working copy in a selection, document, build, or release operation.");
        var externalUse = Directory.Exists(worktree)
            ? await _externalUse.InspectAsync(worktree, cancellationToken).ConfigureAwait(false)
            : new WorkspaceExternalUseEvidence(false, 0, [], "The working copy no longer exists.");
        if (externalUse.HasDetectedProcesses)
            findings.Add("Open worktree file handles were detected: " +
                         string.Join(", ", externalUse.Processes.Take(5).Select(static process => process.Display)) + ".");
        var artifacts = Directory.Exists(worktree)
            ? await ReadRetainedArtifactsAsync(worktree, findings, cancellationToken).ConfigureAwait(false)
            : [];
        var fingerprint = ComputeFingerprint(root, primary, worktree, head, remoteRef, remoteSha,
            containment, clean, registered, unlocked, remoteAncestry, mergedPullRequestNumber, mergedPullRequestUrl,
            studioUse, externalUse, artifacts);
        return new(root, primary, worktree, head, remoteRef, remoteSha, containment, registered, unlocked, clean,
            remoteAncestry, mergedPullRequestNumber, mergedPullRequestUrl, mergedPullRequest, studioUse,
            externalUse, artifacts, findings, fingerprint, DateTimeOffset.UtcNow);
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

    public async Task<WorkspaceStoragePruneReview> ReviewPruneAsync(
        string workspaceRoot,
        WorkspaceStorageEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var root = Path.GetFullPath(workspaceRoot);
        var primary = Path.GetFullPath(entry.PrimaryPath);
        var selected = Path.GetFullPath(entry.Path);
        var findings = new List<string>();
        var containment = Directory.Exists(root) && IsWithin(root, selected) && !PathsEqual(root, selected) &&
                          !PathsEqual(primary, selected);
        if (!containment)
            findings.Add("The stale registration must point inside the selected workspace and remain separate from the primary checkout.");
        if (Directory.Exists(selected) || File.Exists(selected))
        {
            containment = false;
            findings.Add("The selected worktree path still exists. Review normal worktree removal instead of pruning its registration.");
        }

        var listing = Directory.Exists(primary)
            ? await ReadRegistrationsAsync(primary, cancellationToken).ConfigureAwait(false)
            : new RegistrationListing(false, false, []);
        var primaryPassed = Directory.Exists(primary) && listing.Succeeded &&
                            listing.IsComplete && listing.Items.Count(item => PathsEqual(item.Path, primary)) == 1;
        if (!primaryPassed)
            findings.Add("The primary checkout or its Git worktree registry could not be verified.");
        var administrative = Directory.Exists(primary)
            ? await ReadAdministrativeRegistrationsAsync(primary, cancellationToken).ConfigureAwait(false)
            : new AdministrativeRegistrationListing(false, []);
        var administrativePassed = listing.Succeeded && listing.IsComplete &&
                                   AdministrativeRegistryMatches(listing.Items, administrative, primary);
        if (!administrativePassed)
            findings.Add("Git's complete administrative worktree registry could not be mapped one-to-one to the disclosed working copies. Hidden, unreadable, linked or malformed entries must be resolved before pruning.");

        var selectedRegistration = listing.Items.FirstOrDefault(item => PathsEqual(item.Path, selected));
        var selectedAdministrativeMatches = administrative.Items.Where(item =>
            item.WorktreePath is not null && PathsEqual(item.WorktreePath, selected)).ToArray();
        var selectedAdministrative = selectedAdministrativeMatches.Length == 1
            ? selectedAdministrativeMatches[0]
            : null;
        var selectedPassed = selectedRegistration is { PrunableReason: not null } &&
                             selectedAdministrative is { IsValid: true, GitDirTarget: not null };
        if (!selectedPassed)
            findings.Add("Git does not currently mark the selected missing registration as prunable.");

        var prunable = selectedPassed
            ? new[]
            {
                new WorkspacePrunableRegistration(selected, selectedRegistration!.PrunableReason!,
                    selectedAdministrative!.Identity, selectedAdministrative.IdentityPath,
                    selectedAdministrative.GitDirTarget!)
            }
            : [];
        var duplicatePaths = listing.Items
            .GroupBy(item => Path.GetFullPath(item.Path), PathComparer)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();
        var unsafePrunable = prunable.Where(item => Directory.Exists(item.Path) || File.Exists(item.Path) ||
                                                     PathsEqual(item.Path, primary) || !IsWithin(root, item.Path)).ToArray();
        var prunableSetPassed = listing.IsComplete && administrativePassed && prunable.Length == 1 &&
                                unsafePrunable.Length == 0 && duplicatePaths.Length == 0;
        if (!listing.IsComplete)
            findings.Add("Git returned a malformed or unrecognized worktree registry entry. Pruning is blocked until the registry can be represented completely.");
        if (duplicatePaths.Length > 0)
            findings.Add("Git reports duplicate worktree registrations for the same path. Resolve the duplicate administrative entries before pruning.");
        if (unsafePrunable.Length > 0)
            findings.Add("The selected stale registration is outside the workspace, is the primary checkout, or still has a filesystem path.");
        if (prunable.Length == 0)
            findings.Add("Git reports no exact stale registration eligible for reviewed cleanup.");

        var preserved = listing.Items
            .Where(item => !PathsEqual(item.Path, selected))
            .Select(static item => item.Path)
            .OrderBy(static path => path, PathComparer)
            .ToArray();
        var fingerprint = ComputePruneFingerprint(root, primary, selected, listing.Items, administrative.Items);
        var identityFingerprint = ComputeRegistryIdentityFingerprint(root, primary, selected, listing.Items, administrative.Items);
        return new(root, primary, selected, containment, primaryPassed, administrativePassed, selectedPassed, prunableSetPassed,
            prunable, preserved, findings, fingerprint, identityFingerprint, DateTimeOffset.UtcNow);
    }

    public async Task PruneAsync(
        WorkspaceStoragePruneReview reviewed,
        bool confirmRegistrations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        if (!reviewed.ReadyForConfirmation)
            throw new InvalidOperationException("The reviewed evidence does not permit pruning Git registrations.");
        if (!confirmRegistrations)
            throw new InvalidOperationException("Confirm every stale registration included in the reviewed prune set.");

        var currentEntry = new WorkspaceStorageEntry(
            Path.GetFileName(reviewed.PrimaryPath), reviewed.SelectedPath, reviewed.PrimaryPath, "", false,
            false, false, 0, 0, 0, "Broken reference", "", "Not checked", "", false, null);
        var current = await ReviewPruneAsync(reviewed.WorkspaceRoot, currentEntry, cancellationToken).ConfigureAwait(false);
        if (!current.ReadyForConfirmation || !string.Equals(current.Fingerprint, reviewed.Fingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("Git worktree registrations changed after review. Refresh the prune review before continuing.");

        var registration = current.PrunableRegistrations.Single();
        BeforePruneGuardAcquisition?.Invoke();
        using var guard = WorkingCopyPathGuard.Acquire(registration.Path);

        var guardedListing = await ReadRegistrationsAsync(current.PrimaryPath, cancellationToken).ConfigureAwait(false);
        var guardedAdministrative = await ReadAdministrativeRegistrationsAsync(current.PrimaryPath, cancellationToken).ConfigureAwait(false);
        var guardedFingerprint = ComputeRegistryIdentityFingerprint(current.WorkspaceRoot, current.PrimaryPath,
            current.SelectedPath, guardedListing.Items, guardedAdministrative.Items);
        if (!guardedListing.Succeeded || !guardedListing.IsComplete ||
            !AdministrativeRegistryMatches(guardedListing.Items, guardedAdministrative, current.PrimaryPath) ||
            !string.Equals(guardedFingerprint, current.RegistryIdentityFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("Git worktree registrations changed while the selected missing path was being guarded. No registration was removed.");

        var guardedRegistration = guardedAdministrative.Items.SingleOrDefault(item =>
            string.Equals(item.Identity, registration.AdministrativeIdentity, StringComparison.Ordinal) &&
            PathsEqual(item.IdentityPath, registration.AdministrativePath) &&
            item.GitDirTarget is not null && PathsEqual(item.GitDirTarget, registration.GitDirTarget) &&
            item.WorktreePath is not null && PathsEqual(item.WorktreePath, registration.Path));
        if (guardedRegistration is null)
            throw new InvalidOperationException("The reviewed administrative registration identity changed before cleanup. No registration was removed.");

        BeforePruneAdministrativeMove?.Invoke();
        var quarantine = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(registration.AdministrativePath)!)!,
            $"powerforge-studio-worktree-{registration.AdministrativeIdentity}-{Guid.NewGuid():N}");
        Directory.Move(registration.AdministrativePath, quarantine);
        var restore = true;
        try
        {
            var quarantined = await ReadAdministrativeTargetAsync(quarantine, cancellationToken,
                registration.AdministrativePath).ConfigureAwait(false);
            if (!quarantined.IsValid || quarantined.GitDirTarget is null ||
                !PathsEqual(quarantined.GitDirTarget, registration.GitDirTarget) ||
                quarantined.WorktreePath is null || !PathsEqual(quarantined.WorktreePath, registration.Path))
                throw new InvalidOperationException("The administrative registration was repointed before cleanup. Studio restored it without removing the repaired working copy.");

            var after = await ReadRegistrationsAsync(current.PrimaryPath, CancellationToken.None).ConfigureAwait(false);
            var afterAdministrative = await ReadAdministrativeRegistrationsAsync(current.PrimaryPath, CancellationToken.None).ConfigureAwait(false);
            if (!after.Succeeded || !after.IsComplete ||
                !AdministrativeRegistryMatches(after.Items, afterAdministrative, current.PrimaryPath) ||
                after.Items.Any(item => PathsEqual(item.Path, registration.Path)) ||
                current.PreservedRegistrations.Any(path => !after.Items.Any(item => PathsEqual(item.Path, path))))
                throw new IOException("The exact registration was quarantined but the complete Git worktree registry did not satisfy the reviewed postconditions.");

            Directory.Delete(quarantine, recursive: true);
            restore = false;
        }
        finally
        {
            if (restore && Directory.Exists(quarantine) && !Directory.Exists(registration.AdministrativePath))
                Directory.Move(quarantine, registration.AdministrativePath);
        }
    }

    private async Task<RegistrationEvidence> ReadRegistrationAsync(string primary, string worktree, CancellationToken token)
    {
        var listing = await ReadRegistrationsAsync(primary, token).ConfigureAwait(false);
        if (!listing.Succeeded || !listing.IsComplete) return new(false, false);
        var matches = listing.Items.Where(registration => PathsEqual(registration.Path, worktree)).ToArray();
        if (matches.Length != 1) return new(false, false);
        var item = matches[0];
        return item is null ? new(false, false) : new(true, item.Locked);
    }

    private async Task<RegistrationListing> ReadRegistrationsAsync(string primary, CancellationToken token)
    {
        var result = await _git.RunRawAsync(primary, ["worktree", "list", "--porcelain"], cancellationToken: token).ConfigureAwait(false);
        if (!result.Succeeded)
            return new(false, false, []);
        var items = new List<WorktreeRegistration>();
        var complete = true;
        string? path = null;
        var locked = false;
        string? prunable = null;
        void Flush()
        {
            if (path is not null)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(path)) complete = false;
                    else items.Add(new(Path.GetFullPath(path), locked, prunable));
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    complete = false;
                }
            }
            path = null;
            locked = false;
            prunable = null;
        }
        foreach (var line in result.StdOut.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            if (line.StartsWith("worktree ", StringComparison.Ordinal))
            {
                Flush();
                path = line[9..];
                if (string.IsNullOrWhiteSpace(path)) complete = false;
            }
            else if (line.StartsWith("locked", StringComparison.Ordinal))
            {
                if (path is null) complete = false;
                locked = true;
            }
            else if (line.StartsWith("prunable", StringComparison.Ordinal))
            {
                if (path is null) complete = false;
                prunable = line.Length > 9 ? line[9..].Trim() : "Git marked this registration prunable.";
            }
            else if (line.StartsWith("HEAD ", StringComparison.Ordinal) ||
                     line.StartsWith("branch ", StringComparison.Ordinal) ||
                     line is "bare" or "detached")
            {
                if (path is null) complete = false;
                // Represented by path/lock/prunable state or irrelevant to pruning identity.
            }
            else if (line.Length == 0 && path is not null)
                Flush();
            else if (line.Length != 0)
                complete = false;
        }
        Flush();
        return new(true, complete, items);
    }

    private async Task<AdministrativeRegistrationListing> ReadAdministrativeRegistrationsAsync(
        string primary,
        CancellationToken token)
    {
        var commonResult = await _git.RunRawAsync(primary,
            ["rev-parse", "--git-common-dir"], cancellationToken: token).ConfigureAwait(false);
        if (!commonResult.Succeeded || string.IsNullOrWhiteSpace(commonResult.StdOut))
            return new(false, []);
        string commonDirectory;
        try
        {
            var reported = commonResult.StdOut.Trim();
            commonDirectory = Path.GetFullPath(Path.IsPathFullyQualified(reported)
                ? reported
                : Path.Combine(primary, reported));
            if (!Directory.Exists(commonDirectory) ||
                (File.GetAttributes(commonDirectory) & FileAttributes.ReparsePoint) != 0)
                return new(false, []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new(false, []);
        }

        var registryDirectory = Path.Combine(commonDirectory, "worktrees");
        if (!Directory.Exists(registryDirectory)) return new(true, []);
        try
        {
            if ((File.GetAttributes(registryDirectory) & FileAttributes.ReparsePoint) != 0)
                return new(false, []);
            var items = new List<AdministrativeRegistration>();
            foreach (var directory in Directory.EnumerateFileSystemEntries(registryDirectory)
                         .OrderBy(static path => path, PathComparer))
            {
                token.ThrowIfCancellationRequested();
                var target = await ReadAdministrativeTargetAsync(directory, token).ConfigureAwait(false);
                items.Add(new(Path.GetFileName(directory), directory, target.GitDirTarget,
                    target.WorktreePath, target.IsValid));
            }
            return new(true, items);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(false, []);
        }
    }

    private static async Task<AdministrativeTarget> ReadAdministrativeTargetAsync(
        string directory,
        CancellationToken token,
        string? relativePathBase = null)
    {
        try
        {
            var attributes = File.GetAttributes(directory);
            if ((attributes & FileAttributes.Directory) == 0 ||
                (attributes & FileAttributes.ReparsePoint) != 0)
                return new(false, null, null);

            var gitDirFile = Path.Combine(directory, "gitdir");
            var info = new FileInfo(gitDirFile);
            if (!info.Exists || info.Length is <= 0 or > 32768 ||
                (info.Attributes & FileAttributes.ReparsePoint) != 0)
                return new(false, null, null);

            var reported = (await File.ReadAllTextAsync(gitDirFile, token).ConfigureAwait(false)).Trim();
            var target = Path.GetFullPath(Path.IsPathFullyQualified(reported)
                ? reported
                : Path.Combine(relativePathBase ?? directory, reported));
            if (!string.Equals(Path.GetFileName(target), ".git", Comparison))
                return new(false, target, null);
            var worktree = Path.GetDirectoryName(target);
            return new(!string.IsNullOrWhiteSpace(worktree), target, worktree);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new(false, null, null);
        }
    }

    private static bool AdministrativeRegistryMatches(
        IReadOnlyList<WorktreeRegistration> registrations,
        AdministrativeRegistrationListing administrative,
        string primary)
    {
        if (!administrative.Succeeded || administrative.Items.Any(static item => !item.IsValid)) return false;
        var linked = registrations.Where(item => !PathsEqual(item.Path, primary)).ToArray();
        if (linked.Length != administrative.Items.Count) return false;
        return linked.All(registration => administrative.Items.Count(item =>
                   item.WorktreePath is not null && PathsEqual(item.WorktreePath, registration.Path)) == 1) &&
               administrative.Items.All(item => item.WorktreePath is not null &&
                   linked.Count(registration => PathsEqual(registration.Path, item.WorktreePath)) == 1);
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
        int? mergedPullRequestNumber, string? mergedPullRequestUrl, bool studioUse,
        WorkspaceExternalUseEvidence externalUse, IReadOnlyList<string> artifacts)
    {
        var value = string.Join('\n', new[] { root, primary, worktree, head, remoteRef, remoteSha,
            containment.ToString(), clean.ToString(), registered.ToString(), unlocked.ToString(), remoteAncestry.ToString(),
            mergedPullRequestNumber?.ToString() ?? "", mergedPullRequestUrl ?? "", studioUse.ToString(),
            externalUse.IsAvailable.ToString(), externalUse.ResourceCount.ToString(), externalUse.Warning ?? "" }
            .Concat(externalUse.Processes.Select(static process => process.Display))
            .Concat(artifacts.OrderBy(static path => path, PathComparer)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static string ComputePruneFingerprint(
        string root,
        string primary,
        string selected,
        IReadOnlyList<WorktreeRegistration> registrations,
        IReadOnlyList<AdministrativeRegistration> administrative)
    {
        var value = string.Join('\n', new[] { root, primary, selected }.Concat(registrations
            .OrderBy(static item => item.Path, PathComparer)
            .Select(item => $"{item.Path}|{item.Locked}|{item.PrunableReason}|{Directory.Exists(item.Path)}|{File.Exists(item.Path)}"))
            .Concat(administrative.OrderBy(static item => item.IdentityPath, PathComparer)
                .Select(item => $"{item.Identity}|{item.IdentityPath}|{item.GitDirTarget}|{item.WorktreePath}|{item.IsValid}")));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static string ComputeRegistryIdentityFingerprint(
        string root,
        string primary,
        string selected,
        IReadOnlyList<WorktreeRegistration> registrations,
        IReadOnlyList<AdministrativeRegistration> administrative)
    {
        var value = string.Join('\n', new[] { root, primary, selected }.Concat(registrations
            .OrderBy(static item => item.Path, PathComparer)
            .Select(item => $"{item.Path}|{item.Locked}"))
            .Concat(administrative.OrderBy(static item => item.IdentityPath, PathComparer)
                .Select(item => $"{item.Identity}|{item.IdentityPath}|{item.GitDirTarget}|{item.WorktreePath}|{item.IsValid}")));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    public void Dispose()
    {
        if (_ownsGitHub && _gitHub is IDisposable disposable) disposable.Dispose();
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
    private sealed record WorktreeRegistration(string Path, bool Locked, string? PrunableReason);
    private sealed record RegistrationListing(bool Succeeded, bool IsComplete, IReadOnlyList<WorktreeRegistration> Items);
    private sealed record AdministrativeTarget(bool IsValid, string? GitDirTarget, string? WorktreePath);
    private sealed record AdministrativeRegistration(string Identity, string IdentityPath, string? GitDirTarget, string? WorktreePath, bool IsValid);
    private sealed record AdministrativeRegistrationListing(bool Succeeded, IReadOnlyList<AdministrativeRegistration> Items);
}
