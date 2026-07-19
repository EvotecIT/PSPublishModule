namespace PowerForge;

/// <summary>
/// Validates GitHub release settings that must remain replayable after a partial coordinated release.
/// </summary>
internal static class ProjectBuildGitHubRetrySafety
{
    internal static string? Validate(
        ProjectBuildConfiguration configuration,
        DotNetRepositoryReleaseResult release)
    {
        if (configuration is null)
            throw new ArgumentNullException(nameof(configuration));
        if (release is null)
            throw new ArgumentNullException(nameof(release));

        var normalizedReleaseMode = string.IsNullOrWhiteSpace(configuration.GitHubReleaseMode)
            ? "Single"
            : configuration.GitHubReleaseMode!.Trim();
        if (!string.Equals(normalizedReleaseMode, "Single", StringComparison.OrdinalIgnoreCase))
        {
            return "Coordinated GitHub publishing requires GitHubReleaseMode 'Single' because per-project releases cannot be resumed atomically after partial publication.";
        }

        var normalizedConflictPolicy = string.IsNullOrWhiteSpace(configuration.GitHubTagConflictPolicy)
            ? "Reuse"
            : configuration.GitHubTagConflictPolicy!.Trim();
        if (string.Equals(normalizedConflictPolicy, "AppendUtcTimestamp", StringComparison.OrdinalIgnoreCase))
        {
            return "Coordinated GitHub publishing cannot use GitHubTagConflictPolicy 'AppendUtcTimestamp' because a retry would resolve a different release tag.";
        }
        if (!string.Equals(normalizedConflictPolicy, "Reuse", StringComparison.OrdinalIgnoreCase))
        {
            return "Coordinated GitHub publishing requires GitHubTagConflictPolicy 'Reuse' so a stable release can be resumed after an uncertain remote outcome.";
        }

        if (string.IsNullOrWhiteSpace(configuration.GitHubTagName) &&
            HasVolatileTagTemplate(configuration.GitHubTagTemplate))
        {
            return "Coordinated GitHub publishing requires a stable GitHub tag; timestamp tokens are not retry-safe unless GitHubTagName fixes the exact tag.";
        }

        if (string.IsNullOrWhiteSpace(configuration.GitHubTagName) &&
            string.IsNullOrWhiteSpace(ProjectBuildSupportService.ResolveGitHubBaseVersion(configuration, release)) &&
            UsesBaseVersion(configuration.GitHubTagTemplate))
        {
            return "Coordinated GitHub publishing requires GitHubTagName, GitHubPrimaryProject, or a stable GitHubTagTemplate that does not depend on {Version}/{PrimaryVersion} when the planned packages have no single base version. Otherwise the publisher falls back to a date-based tag that cannot be resumed safely.";
        }

        return null;
    }

    private static bool UsesBaseVersion(string? tagTemplate)
        => string.IsNullOrWhiteSpace(tagTemplate) ||
           tagTemplate!.IndexOf("{Version}", StringComparison.OrdinalIgnoreCase) >= 0 ||
           tagTemplate.IndexOf("{PrimaryVersion}", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool HasVolatileTagTemplate(string? tagTemplate)
    {
        if (string.IsNullOrWhiteSpace(tagTemplate))
            return false;

        return tagTemplate!.IndexOf("{Date}", StringComparison.OrdinalIgnoreCase) >= 0 ||
               tagTemplate.IndexOf("{UtcDate}", StringComparison.OrdinalIgnoreCase) >= 0 ||
               tagTemplate.IndexOf("{Timestamp}", StringComparison.OrdinalIgnoreCase) >= 0 ||
               tagTemplate.IndexOf("{UtcTimestamp}", StringComparison.OrdinalIgnoreCase) >= 0 ||
               tagTemplate.IndexOf("{DateTime}", StringComparison.OrdinalIgnoreCase) >= 0 ||
               tagTemplate.IndexOf("{UtcDateTime}", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
