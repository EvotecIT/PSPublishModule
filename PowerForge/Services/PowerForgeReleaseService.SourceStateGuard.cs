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

        DotNetPublishPipelineRunner.SourceProvenance source =
            DotNetPublishPipelineRunner.ReadSourceProvenance(
                repositoryRoot,
                generatedPaths: request.GeneratedProvenancePaths,
                explicitInputPaths: request.SourceInputPaths);
        if (string.IsNullOrWhiteSpace(source.Revision) ||
            !string.Equals(source.Revision, expectedRevision, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Release source revision changed after the module build; expected '{expectedRevision}', " +
                $"received '{source.Revision ?? "unknown"}'.");
        }
        if (source.Dirty is not false)
        {
            throw new InvalidOperationException(
                "Release source changed after the module build. Publication is blocked before package, module, tool, or GitHub mutation.");
        }
    }
}
