using System.Text.RegularExpressions;

namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private static readonly Regex ExactSourceRevisionPattern = new(
        "^[0-9a-fA-F]{40}([0-9a-fA-F]{24})?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static bool HasPostBuildSourceStateGuard(PowerForgeReleaseRequest request)
        => !string.IsNullOrWhiteSpace(request.ExpectedSourceRevision);

    private static void ValidatePostBuildSourceState(PowerForgeReleaseRequest request)
    {
        DotNetPublishPipelineRunner.ValidateGeneratedConfigurationInputs(request.DotNetPublishPlan);
        if (!HasPostBuildSourceStateGuard(request))
            return;
        if (string.IsNullOrWhiteSpace(request.SourceRepositoryRoot))
        {
            throw new InvalidOperationException(
                "ExpectedSourceRevision requires SourceRepositoryRoot for the post-build source-state guard.");
        }

        string expectedRevision = request.ExpectedSourceRevision!.Trim();
        if (!ExactSourceRevisionPattern.IsMatch(expectedRevision))
        {
            throw new InvalidOperationException(
                "ExpectedSourceRevision must be a full 40- or 64-character Git object id.");
        }

        string repositoryRoot = Path.GetFullPath(request.SourceRepositoryRoot!.Trim().Trim('"'));
        GitCommandResult topLevel = GitClient.CreateTrustedSystemClient()
            .ShowTopLevelAsync(repositoryRoot, request.CancellationToken)
            .GetAwaiter()
            .GetResult();
        if (!topLevel.Succeeded || string.IsNullOrWhiteSpace(topLevel.StdOut))
            throw new InvalidOperationException("The post-build source-state guard requires a Git checkout.");

        string actualTopLevel = Path.GetFullPath(topLevel.StdOut.Trim());
        if (!AppleReleasePathsEqual(actualTopLevel, repositoryRoot))
        {
            throw new InvalidOperationException(
                "SourceRepositoryRoot must identify the actual Git top-level checkout.");
        }

        // ExpectedSourceRevision is an explicit checkout-integrity guard. Keep it
        // about the checkout: package-cache imports and MSBuild provenance are
        // validated by the build and artifact validation lanes and must not be
        // reclassified as working-tree mutations here.
        DotNetPublishPipelineRunner.SourceProvenance source =
            DotNetPublishPipelineRunner.ReadSourceProvenance(
                repositoryRoot,
                generatedPaths: request.GeneratedProvenancePaths,
                explicitInputPaths: request.SourceInputPaths,
                sourceRootPaths: new[] { repositoryRoot });
        ValidateExpectedSourceSnapshot(source, expectedRevision);
    }

    private static void ValidateExpectedSourceSnapshot(
        DotNetPublishPipelineRunner.SourceProvenance source,
        string expectedRevision)
    {
        if (string.IsNullOrWhiteSpace(source.Revision) ||
            !string.Equals(source.Revision, expectedRevision, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Release source revision changed after the release build; expected '{expectedRevision}', " +
                $"received '{source.Revision ?? "unknown"}'.");
        }
        if (source.Dirty is not false)
        {
            string paths = source.DirtyPaths.Length == 0
                ? "none reported"
                : string.Join(", ", source.DirtyPaths.Take(10)) +
                  (source.DirtyPaths.Length > 10 ? $" (+{source.DirtyPaths.Length - 10} more)" : string.Empty);
            string reasons = source.DirtyReasons.Length == 0
                ? "working-tree state is not clean"
                : string.Join("; ", source.DirtyReasons);
            throw new InvalidOperationException(
                "Release source changed after the release build. Publication is blocked before package, module, tool, or GitHub mutation. " +
                $"Changed paths: {paths}. Reason: {reasons}.");
        }
    }
}
