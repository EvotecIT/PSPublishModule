namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static void ValidateCurrentSourceProvenance(
        string projectRoot,
        string gitRoot,
        string expectedRevision,
        string verifiedUntrackedOutput,
        IEnumerable<string> generatedPaths,
        IEnumerable<string> trackedGeneratedPaths,
        SourceDirtyScope dirtyScope)
    {
        string? initialRevision = ReadGitText(projectRoot, "rev-parse HEAD");
        string? initialReplacementRefs = ReadGitRawText(
            gitRoot,
            "for-each-ref --format=\"%(refname)\" refs/replace");
        string? initialTrackedStatus = ReadGitRawText(
            gitRoot,
            "status --porcelain=v1 -z --untracked-files=no");
        string? initialUntrackedOutput = ReadGitText(
            gitRoot,
            "ls-files --others --exclude-standard -z");
        string? finalTrackedStatus = ReadGitRawText(
            gitRoot,
            "status --porcelain=v1 -z --untracked-files=no");
        string? finalUntrackedOutput = ReadGitText(
            gitRoot,
            "ls-files --others --exclude-standard -z");
        string? finalReplacementRefs = ReadGitRawText(
            gitRoot,
            "for-each-ref --format=\"%(refname)\" refs/replace");
        string? finalRevision = ReadGitText(projectRoot, "rev-parse HEAD");

        if (string.IsNullOrWhiteSpace(initialRevision) ||
            string.IsNullOrWhiteSpace(finalRevision) ||
            !string.Equals(initialRevision, expectedRevision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(finalRevision, expectedRevision, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Release source revision changed after publish provenance was confirmed; expected '{expectedRevision}', " +
                $"received '{finalRevision ?? initialRevision ?? "unknown"}'.");
        }

        if (initialReplacementRefs is null || finalReplacementRefs is null)
        {
            throw new InvalidOperationException(
                "Release source could not be revalidated because the Git replacement-ref query failed.");
        }
        if (!string.IsNullOrWhiteSpace(initialReplacementRefs) ||
            !string.IsNullOrWhiteSpace(finalReplacementRefs))
        {
            throw new InvalidOperationException(
                "Release source changed after publish provenance was confirmed; Git replacement refs are active.");
        }
        if (initialTrackedStatus is null || finalTrackedStatus is null ||
            initialUntrackedOutput is null || finalUntrackedOutput is null)
        {
            throw new InvalidOperationException(
                "Release source could not be revalidated because a Git status query failed.");
        }
        if (!string.Equals(initialTrackedStatus, finalTrackedStatus, StringComparison.Ordinal) ||
            !string.Equals(initialUntrackedOutput, finalUntrackedOutput, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Release source changed during final publish-provenance validation.");
        }

        string[]? trackedSourceChanges = FindTrackedSourceChanges(
            projectRoot,
            gitRoot,
            finalTrackedStatus,
            trackedGeneratedPaths,
            dirtyScope);
        string[] untrackedSourceFiles = FindUntrackedSourceFiles(
            projectRoot,
            gitRoot,
            finalUntrackedOutput,
            generatedPaths,
            dirtyScope);
        string[] newUntrackedProjectFiles = FindNewUntrackedProjectFiles(
            projectRoot,
            gitRoot,
            verifiedUntrackedOutput,
            finalUntrackedOutput,
            generatedPaths,
            dirtyScope);
        if (trackedSourceChanges is null)
        {
            throw new InvalidOperationException(
                "Release source could not be revalidated because tracked Git status could not be parsed.");
        }
        if (trackedSourceChanges.Length == 0 &&
            untrackedSourceFiles.Length == 0 &&
            newUntrackedProjectFiles.Length == 0)
            return;

        string details = string.Join(
            ", ",
            trackedSourceChanges
                .Concat(untrackedSourceFiles)
                .Concat(newUntrackedProjectFiles)
                .Distinct(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                .Take(10));
        throw new InvalidOperationException(
            "Release source changed after publish provenance was confirmed; final manifest generation is blocked." +
            (details.Length == 0 ? string.Empty : " Paths: " + details));
    }

    private static string[] FindNewUntrackedProjectFiles(
        string projectRoot,
        string gitRoot,
        string verifiedUntrackedOutput,
        string currentUntrackedOutput,
        IEnumerable<string> generatedPaths,
        SourceDirtyScope dirtyScope)
    {
        StringComparer comparer = IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        StringComparison comparison = IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var verifiedPaths = new HashSet<string>(
            verifiedUntrackedOutput.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(static path => path.Replace('\\', '/').TrimStart('/')),
            comparer);
        string[] exclusions = BuildGeneratedPathExclusions(projectRoot, gitRoot, generatedPaths);
        return currentUntrackedOutput
            .Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static path => path.Replace('\\', '/').TrimStart('/'))
            .Where(path => !verifiedPaths.Contains(path))
            .Where(path => !IsGeneratedPath(path, exclusions, comparison))
            .Where(path => dirtyScope.IsWithinProjectDirectory(path, comparison))
            .Distinct(comparer)
            .OrderBy(path => path, comparer)
            .ToArray();
    }
}
