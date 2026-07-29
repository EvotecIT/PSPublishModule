namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    internal static bool ApplyVerifiedGitHubRecoveryPublishingOverrides(
        PowerForgeReleaseSpec spec,
        PowerForgeReleaseRequest request)
    {
        var gitHub = spec.GitHub;
        if (gitHub is null ||
            request.ModuleRunMode != ConfigurationGateMode.Publish ||
            request.PublishProjectGitHub == false ||
            !gitHub.Publish ||
            !IsVerifiedGitHubRecoveryRequested(gitHub))
        {
            return false;
        }

        ValidateVerifiedGitHubRecoveryConfiguration(gitHub);
        request.PublishNuget = false;
        request.ModuleIncludePublishing = false;
        return true;
    }

    private static void ValidateVerifiedGitHubRecoveryConfiguration(PowerForgeReleaseGitHubOptions gitHub)
    {
        var error = GetVerifiedGitHubRecoveryConfigurationError(gitHub);
        if (error is not null)
            throw new InvalidOperationException(error);
    }

    private static string? GetVerifiedGitHubRecoveryConfigurationError(PowerForgeReleaseGitHubOptions gitHub)
    {
        if (!IsExactGitCommitSha(gitHub.Commitish))
            return "Verified GitHub recovery requires GitHub.Commitish to be an exact 40-character commit SHA.";
        if (!gitHub.RequireExpectedExistingRelease || gitHub.ExpectedExistingReleaseId.GetValueOrDefault() <= 0)
            return "Verified GitHub recovery requires an exact preflight-bound existing release ID.";
        if (!gitHub.RequirePublishedStableRelease)
            return "Verified GitHub recovery requires the existing release to remain published and stable.";
        if (!gitHub.ReplaceExistingAssets)
            return "Verified GitHub recovery requires same-name asset replacement mode.";
        if (!gitHub.RequirePublishedNuGetAssets)
            return "Verified GitHub recovery requires exact published NuGet-byte restoration.";
        if (!gitHub.RequirePublishedModuleAssets || string.IsNullOrWhiteSpace(gitHub.PublishedModuleSource))
            return "Verified GitHub recovery requires exact published module-payload restoration and its repository source.";
        return null;
    }

    private static bool IsVerifiedGitHubRecoveryRequested(PowerForgeReleaseGitHubOptions gitHub)
        => gitHub.RequirePublishedNuGetAssets ||
           gitHub.RequirePublishedModuleAssets;

    private PowerForgeUnifiedGitHubReleaseResult? RestorePublishedNuGetAssetsForGitHubRecovery(
        PowerForgeReleaseSpec spec,
        PowerForgeReleaseGitHubOptions gitHub,
        PowerForgeReleaseResult result,
        string owner,
        string repository,
        string version,
        CancellationToken cancellationToken,
        out string[] recoveredAssets,
        out string[] recoveredReleaseZips)
    {
        recoveredAssets = Array.Empty<string>();
        recoveredReleaseZips = Array.Empty<string>();
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
            var recovered = _restorePublishedNuGetAssets(
                spec.Packages?.PublishSource ?? string.Empty,
                version,
                result.ReleaseAssets,
                cancellationToken);
            recoveredAssets = recovered
                .Where(path => string.Equals(Path.GetExtension(path), ".nupkg", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            recoveredReleaseZips = recovered
                .Where(path => string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
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

    private static bool IsExactGitCommitSha(string? value)
        => value is { Length: 40 } && value.All(Uri.IsHexDigit);

    private PowerForgeUnifiedGitHubReleaseResult? RestorePublishedModuleAssetsForGitHubRecovery(
        PowerForgeReleaseGitHubOptions gitHub,
        PowerForgeReleaseResult result,
        string owner,
        string repository,
        string version,
        CancellationToken cancellationToken,
        out string[] recoveredAssets)
    {
        recoveredAssets = Array.Empty<string>();
        if (!gitHub.RequirePublishedModuleAssets)
            return null;

        var moduleAssets = result.ReleaseAssetEntries
            .Where(static entry => entry.Category == PowerForgeReleaseAssetCategory.Module)
            .Select(static entry => entry.StagedPath ?? entry.Path)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (moduleAssets.Length == 0)
            return null;

        try
        {
            recoveredAssets = _restorePublishedModuleAssets(
                gitHub.PublishedModuleSource ?? string.Empty,
                result.ModulePlan?.ModuleName ?? string.Empty,
                version,
                moduleAssets,
                cancellationToken);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return CreateNuGetRecoveryError(
                owner,
                repository,
                version,
                "Unable to restore the published module payload for verified GitHub recovery. " +
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
