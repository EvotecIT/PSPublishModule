namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static bool IsRecordedNestedGitRepository(string repositoryRoot, string outerGitRoot)
    {
        string currentRoot = NormalizeBuildInputPathRoot(repositoryRoot);
        string outerRoot = NormalizeBuildInputPathRoot(outerGitRoot);
        StringComparison comparison = IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!HasNoGitReplacementRefs(currentRoot) ||
            (!string.Equals(currentRoot, outerRoot, comparison) &&
             !HasCleanNestedGitWorktree(currentRoot)))
            return false;
        while (!string.Equals(currentRoot, outerRoot, comparison))
        {
            string? parentDirectory = Path.GetDirectoryName(currentRoot);
            if (string.IsNullOrWhiteSpace(parentDirectory))
                return false;

            string? parentRepository = ReadGitText(parentDirectory!, "rev-parse --show-toplevel");
            if (string.IsNullOrWhiteSpace(parentRepository))
                return false;
            parentRepository = NormalizeBuildInputPathRoot(parentRepository!);
            if (string.Equals(parentRepository, currentRoot, comparison) ||
                !IsSameOrBelowBuildInputPath(currentRoot, parentRepository) ||
                !IsSameOrBelowBuildInputPath(parentRepository, outerRoot) ||
                !HasNoGitReplacementRefs(parentRepository) ||
                (!string.Equals(parentRepository, outerRoot, comparison) &&
                 !HasCleanNestedGitWorktree(parentRepository)))
            {
                return false;
            }

            string relativePath = FrameworkCompatibility.GetRelativePath(parentRepository, currentRoot)
                .Replace('\\', '/')
                .TrimStart('/');
            string? stagedEntry = ReadGitRawText(
                parentRepository,
                $"ls-files --stage -- {QuoteLiteralGitPath(relativePath)}");
            string? nestedRevision = ReadGitText(currentRoot, "rev-parse HEAD");
            if (string.IsNullOrWhiteSpace(stagedEntry) ||
                string.IsNullOrWhiteSpace(nestedRevision))
            {
                return false;
            }

            int tab = stagedEntry!.IndexOf('\t');
            string[] metadata = (tab < 0 ? stagedEntry : stagedEntry.Substring(0, tab))
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (metadata.Length < 2 ||
                !metadata[0].Equals("160000", StringComparison.Ordinal) ||
                !metadata[1].Equals(nestedRevision, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            currentRoot = parentRepository;
        }

        return true;
    }

    private static bool HasCleanNestedGitWorktree(string repositoryRoot)
    {
        string? status = ReadGitRawText(
            repositoryRoot,
            "status --porcelain=v1 -z --untracked-files=all");
        return status is not null && status.Length == 0;
    }

    private static bool HasNoGitReplacementRefs(string repositoryRoot)
    {
        string? replacementRefs = ReadGitRawText(
            repositoryRoot,
            "for-each-ref --format=\"%(refname)\" refs/replace");
        return replacementRefs is not null && string.IsNullOrWhiteSpace(replacementRefs);
    }
}
