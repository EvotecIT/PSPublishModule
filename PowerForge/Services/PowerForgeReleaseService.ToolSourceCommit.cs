namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private static string? CaptureToolSourceCommit(string projectRoot, CancellationToken cancellationToken)
        => string.IsNullOrWhiteSpace(projectRoot)
            ? null
            : ReleaseSourceCommit.Capture(projectRoot, cancellationToken);

    private static string? ResolveBuiltToolSourceCommit(PowerForgeReleaseResult builtResult)
    {
        if (!string.IsNullOrWhiteSpace(builtResult.ToolSourceCommitSha))
            return builtResult.ToolSourceCommitSha;
        if (GitObjectId.IsFull(builtResult.DotNetSourceCommitSha))
            return builtResult.DotNetSourceCommitSha;
        return null;
    }

    private static string? ResolveBuiltReleaseCommitish(
        PowerForgeReleaseGitHubOptions gitHub, PowerForgeReleaseResult builtResult)
    {
        if (!string.IsNullOrWhiteSpace(gitHub.Commitish))
            return gitHub.Commitish!.Trim();

        var commits = new[]
            {
                ResolveBuiltToolSourceCommit(builtResult),
                builtResult.ModuleSourceCommitSha,
                builtResult.Packages?.Result.Release?.SourceCommitSha
            }
            .Concat(builtResult.ModulePackagePlans.Select(plan => plan.Release.SourceCommitSha))
            .Where(GitObjectId.IsFull)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (commits.Length > 1)
            throw new InvalidOperationException(
                "Unified GitHub release artifacts were built from different source commits. Configure GitHub.Commitish explicitly to choose the release tag source.");
        return commits.SingleOrDefault();
    }

    private static string? ResolveToolReleaseCommitish(PowerForgeReleaseSpec spec, string? sourceCommit)
    {
        var configured = spec.GitHub?.Commitish?.Trim();
        if (string.Equals(configured, "HEAD", StringComparison.Ordinal))
            throw new InvalidOperationException("GitHub.Commitish HEAD requires a verified DotNet publish source checkout before publishing.");
        return string.IsNullOrWhiteSpace(configured) ? sourceCommit : configured;
    }
}
