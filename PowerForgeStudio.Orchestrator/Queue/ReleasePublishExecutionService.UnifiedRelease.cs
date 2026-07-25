using System.Text.Json;
using PowerForge;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Signing;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed partial class ReleasePublishExecutionService
{
    private IReadOnlyList<ReleasePublishReceipt> ExecuteUnifiedGitHubPublish(
        PowerForgeStudio.Domain.Catalog.RepositoryCatalogEntry repository,
        ReleaseSigningExecutionResult signingResult)
    {
        var buildResult = _checkpointSerializer.TryDeserialize<ReleaseBuildExecutionResult>(signingResult.SourceCheckpointStateJson);
        if (buildResult is null || string.IsNullOrWhiteSpace(buildResult.UnifiedReleaseStateJson))
        {
            return [
                FailedReceipt(repository.RootPath, repository.Name, "UnifiedRelease", "GitHub release", null, "Unified release build state was not preserved through the signing checkpoint.")
            ];
        }

        try
        {
            var result = _publishUnifiedGitHub(repository.UnifiedReleaseConfigPath!, buildResult.UnifiedReleaseStateJson!);
            var receipts = result.ToolGitHubReleases
                .Select(release => ReleaseQueueReceiptFactory.CreatePublishReceipt(
                    repository.RootPath,
                    repository.Name,
                    ReleaseBuildAdapterKind.ToolBuild.ToString(),
                    release.Target,
                    "GitHub",
                    release.ReleaseUrl ?? $"{release.Owner}/{release.Repository}",
                    release.Success ? ReleasePublishReceiptStatus.Published : ReleasePublishReceiptStatus.Failed,
                    release.Success ? $"GitHub release {release.TagName} published." : release.ErrorMessage ?? "Tool GitHub release failed.",
                    release.AssetPaths.FirstOrDefault()))
                .ToList();

            if (result.UnifiedGitHubRelease is { } unified)
            {
                receipts.Add(ReleaseQueueReceiptFactory.CreatePublishReceipt(
                    repository.RootPath,
                    repository.Name,
                    "UnifiedRelease",
                    "Unified GitHub release",
                    "GitHub",
                    unified.ReleaseUrl ?? $"{unified.Owner}/{unified.Repository}",
                    unified.Success ? ReleasePublishReceiptStatus.Published : ReleasePublishReceiptStatus.Failed,
                    unified.Success ? $"GitHub release {unified.TagName} published." : unified.ErrorMessage ?? "Unified GitHub release failed.",
                    unified.AssetPaths.FirstOrDefault()));
            }

            if (!result.Success && receipts.All(receipt => receipt.Status != ReleasePublishReceiptStatus.Failed))
                receipts.Add(FailedReceipt(repository.RootPath, repository.Name, "UnifiedRelease", "GitHub release", null, result.ErrorMessage ?? "Unified GitHub publishing failed."));

            return receipts;
        }
        catch (Exception ex)
        {
            return [
                FailedReceipt(repository.RootPath, repository.Name, "UnifiedRelease", "GitHub release", null, FirstLine(ex.Message) ?? "Unified GitHub publishing failed.")
            ];
        }
    }

    private static PowerForgeReleaseResult PublishUnifiedGitHub(string configPath, string stateJson)
    {
        var spec = PowerForgeReleaseService.LoadConfiguration(configPath);
        var builtResult = JsonSerializer.Deserialize<PowerForgeReleaseResult>(stateJson)
            ?? throw new InvalidOperationException("Unified release build state could not be deserialized.");
        return new PowerForgeReleaseService(new NullLogger()).PublishBuiltGitHubReleases(
            spec,
            new PowerForgeReleaseRequest {
                ConfigPath = configPath,
                ModuleRunMode = ConfigurationGateMode.Publish
            },
            builtResult);
    }

    private static bool UnifiedReleaseOwnsGitHub(string? configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
            return false;

        try
        {
            return PowerForgeReleaseService.LoadConfiguration(configPath!).GitHub?.Publish == true;
        }
        catch
        {
            return false;
        }
    }
}
