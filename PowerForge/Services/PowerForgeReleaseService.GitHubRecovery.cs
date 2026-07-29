namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private PowerForgeUnifiedGitHubReleaseResult? RestorePublishedNuGetAssetsForGitHubRecovery(
        PowerForgeReleaseSpec spec,
        PowerForgeReleaseGitHubOptions gitHub,
        PowerForgeReleaseResult result,
        string owner,
        string repository,
        string version,
        CancellationToken cancellationToken,
        out string[] recoveredAssets)
    {
        recoveredAssets = Array.Empty<string>();
        if (!gitHub.RequirePublishedNuGetAssets)
            return null;

        if (!gitHub.ReuseExistingRelease ||
            !gitHub.RequireExpectedExistingRelease ||
            gitHub.ExpectedExistingReleaseId.GetValueOrDefault() <= 0)
        {
            return CreateNuGetRecoveryError(
                owner,
                repository,
                version,
                "Published NuGet byte recovery requires an exact preflight-bound existing GitHub release.");
        }

        try
        {
            recoveredAssets = _restorePublishedNuGetAssets(
                spec.Packages?.PublishSource ?? string.Empty,
                version,
                result.ReleaseAssets,
                cancellationToken);
            RewriteReleaseSummaryFiles(result);
            return null;
        }
        catch (Exception exception)
        {
            return CreateNuGetRecoveryError(
                owner,
                repository,
                version,
                "Unable to restore exact published NuGet bytes for verified GitHub recovery. " +
                exception.Message);
        }
    }

    private static PowerForgeUnifiedGitHubReleaseResult CreateNuGetRecoveryError(
        string owner,
        string repository,
        string version,
        string message)
        => new()
        {
            Owner = owner,
            Repository = repository,
            Version = version,
            Success = false,
            ErrorMessage = message
        };
}
