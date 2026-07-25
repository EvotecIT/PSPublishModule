using System.Text.Json;
using PowerForge;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Orchestrator.Host;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed partial class ReleasePublishExecutionService
{
    private IReadOnlyList<ReleasePublishTarget> BuildUnifiedPublishTargets(
        ReleaseQueueItem item,
        ReleaseSigningExecutionResult signingResult)
    {
        try
        {
            var buildResult = _checkpointSerializer.TryDeserialize<ReleaseBuildExecutionResult>(signingResult.SourceCheckpointStateJson);
            if (string.IsNullOrWhiteSpace(buildResult?.UnifiedReleaseStateJson))
                return [];

            var unified = JsonSerializer.Deserialize<PowerForgeReleaseResult>(buildResult.UnifiedReleaseStateJson!);
            if (unified is null)
                return [];

            var repository = _catalogScanner.InspectRepository(item.RootPath);
            var configPath = unified.ConfigPath ?? repository.UnifiedReleaseConfigPath;
            if (string.IsNullOrWhiteSpace(configPath))
                return [];

            UnifiedReleaseConfigFingerprint.Validate(configPath, buildResult.UnifiedReleaseConfigSha256);
            var spec = PowerForgeReleaseService.LoadConfiguration(configPath!);
            var targets = new List<ReleasePublishTarget>();
            var assets = unified.ReleaseAssets
                .Concat(unified.ReleaseAssetEntries.Select(static entry => entry.StagedPath ?? entry.Path))
                .Concat(new[] { unified.ReleaseManifestPath, unified.ReleaseChecksumsPath })
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(static path => path!)
                .ToArray();
            var missingAssets = assets
                .Where(static path => !File.Exists(path))
                .ToArray();
            if ((spec.GitHub?.Publish == true || spec.Tools?.GitHub.Publish == true) &&
                missingAssets.Length > 0)
            {
                throw new InvalidOperationException(
                    "Checkpointed unified GitHub release assets are missing: " +
                    string.Join(", ", missingAssets));
            }

            if ((spec.GitHub?.Publish == true || spec.Tools?.GitHub.Publish == true) &&
                assets.FirstOrDefault() is { } sourcePath)
            {
                targets.Add(new ReleasePublishTarget(
                    RootPath: item.RootPath,
                    RepositoryName: item.RepositoryName,
                    AdapterKind: "UnifiedRelease",
                    TargetName: $"{assets.Length} unified GitHub asset(s)",
                    TargetKind: "GitHub",
                    SourcePath: sourcePath,
                    Destination: "Configured unified GitHub release"));
            }

            var wingetSubmissionEnabled = spec.Winget is { } winget &&
                                          (winget.Submit || winget.Submission?.Enabled == true);
            var wingetPaths = unified.WingetManifestPaths
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var missingWingetPaths = wingetPaths
                .Where(static path => !File.Exists(path))
                .ToArray();
            if (wingetSubmissionEnabled && (wingetPaths.Length == 0 || missingWingetPaths.Length > 0))
            {
                throw new InvalidOperationException(
                    wingetPaths.Length == 0
                        ? "WinGet submission is enabled, but the build checkpoint contains no WinGet manifests."
                        : "Checkpointed WinGet manifests are missing: " + string.Join(", ", missingWingetPaths));
            }

            if (wingetSubmissionEnabled)
            {
                targets.Add(new ReleasePublishTarget(
                    RootPath: item.RootPath,
                    RepositoryName: item.RepositoryName,
                    AdapterKind: "UnifiedRelease",
                    TargetName: $"{wingetPaths.Length} WinGet manifest(s)",
                    TargetKind: "Winget",
                    SourcePath: wingetPaths[0],
                    Destination: "Windows Package Manager"));
            }

            if (unified.ModulePackagePlans.Length > 0)
            {
                if (!string.IsNullOrWhiteSpace(spec.Module?.ConfigPath))
                {
                    foreach (var lane in ModulePackageReleaseCheckpointService
                                 .ResolveLanes(configPath!, spec)
                                 .Where(static lane => lane.PublishNuget || lane.PublishGitHub))
                    {
                        _ = ModulePackageReleaseCheckpointService.Restore(
                            lane,
                            unified.ModulePackagePlans);
                    }
                }

                targets.Add(new ReleasePublishTarget(
                    RootPath: item.RootPath,
                    RepositoryName: item.RepositoryName,
                    AdapterKind: "UnifiedRelease",
                    TargetName: "Module-owned package release",
                    TargetKind: "ModulePackages",
                    SourcePath: configPath,
                    Destination: "Configured module package destinations"));
            }

            var enabledAppleApps = spec.AppleApps?.Apps.Count(static app => app.Enabled) ?? 0;
            if (enabledAppleApps > 0 && HasConfiguredApplePublishAction(spec.AppleApps!))
            {
                targets.Add(new ReleasePublishTarget(
                    RootPath: item.RootPath,
                    RepositoryName: item.RepositoryName,
                    AdapterKind: "UnifiedRelease",
                    TargetName: $"{enabledAppleApps} Apple application(s)",
                    TargetKind: "Apple",
                    SourcePath: configPath,
                    Destination: "Configured Apple release destinations"));
            }

            return targets;
        }
        catch (Exception ex)
        {
            return [
                new ReleasePublishTarget(
                    RootPath: item.RootPath,
                    RepositoryName: item.RepositoryName,
                    AdapterKind: "UnifiedRelease",
                    TargetName: "Unified release configuration",
                    TargetKind: "ConfigurationError",
                    SourcePath: item.RootPath,
                    Destination: FirstLine(ex.Message) ?? "Unified release configuration could not be loaded.")
            ];
        }
    }

    private async Task<IReadOnlyList<ReleasePublishReceipt>> ExecuteUnifiedPublishAsync(
        PowerForgeStudio.Domain.Catalog.RepositoryCatalogEntry repository,
        ReleaseSigningExecutionResult signingResult,
        CancellationToken cancellationToken)
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
            UnifiedReleaseConfigFingerprint.Validate(
                repository.UnifiedReleaseConfigPath!,
                buildResult.UnifiedReleaseConfigSha256);
            var spec = PowerForgeReleaseService.LoadConfiguration(repository.UnifiedReleaseConfigPath!);
            cancellationToken.ThrowIfCancellationRequested();
            var result = await Task.Run(
                    () => _publishUnifiedRelease(
                        repository.UnifiedReleaseConfigPath!,
                        buildResult.UnifiedReleaseStateJson!,
                        cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
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

            if (result.WingetSubmission is { } winget)
            {
                receipts.Add(ReleaseQueueReceiptFactory.CreatePublishReceipt(
                    repository.RootPath,
                    repository.Name,
                    "UnifiedRelease",
                    "WinGet submission",
                    "Winget",
                    "Windows Package Manager",
                    winget.Succeeded ? ReleasePublishReceiptStatus.Published : ReleasePublishReceiptStatus.Failed,
                    winget.Succeeded ? "WinGet manifests submitted." : winget.ErrorMessage ?? "WinGet submission failed.",
                    result.WingetManifestPaths.FirstOrDefault()));
            }

            foreach (var apple in result.AppleApps)
            {
                receipts.Add(ReleaseQueueReceiptFactory.CreatePublishReceipt(
                    repository.RootPath,
                    repository.Name,
                    "UnifiedRelease",
                    apple.Plan.Name,
                    "Apple",
                    apple.Plan.BundleId ?? apple.Plan.Destination,
                    apple.Success ? ReleasePublishReceiptStatus.Published : ReleasePublishReceiptStatus.Failed,
                    apple.Success ? "Configured Apple release actions completed." : apple.ErrorMessage ?? "Apple application release failed.",
                    apple.Archive?.ArchivePath ?? apple.Plan.ArchivePath));
            }

            if (!result.Success && receipts.All(receipt => receipt.Status != ReleasePublishReceiptStatus.Failed))
                receipts.Add(FailedReceipt(repository.RootPath, repository.Name, "UnifiedRelease", "GitHub release", null, result.ErrorMessage ?? "Unified GitHub publishing failed."));

            return receipts;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return [
                FailedReceipt(repository.RootPath, repository.Name, "UnifiedRelease", "GitHub release", null, FirstLine(ex.Message) ?? "Unified GitHub publishing failed.")
            ];
        }
    }

    private static PowerForgeReleaseResult PublishUnifiedRelease(
        string configPath,
        string stateJson,
        CancellationToken cancellationToken)
    {
        var spec = PowerForgeReleaseService.LoadConfiguration(configPath);
        var builtResult = JsonSerializer.Deserialize<PowerForgeReleaseResult>(stateJson)
            ?? throw new InvalidOperationException("Unified release build state could not be deserialized.");
        return new PowerForgeReleaseService(new NullLogger()).PublishBuiltReleaseOutputs(
            spec,
            new PowerForgeReleaseRequest {
                ConfigPath = configPath,
                ModuleHostPath = PowerForgeStudioHostPaths.ResolvePSPublishModulePath(),
                ModuleRunMode = ConfigurationGateMode.Publish,
                AppleActionConfirmed = true,
                CancellationToken = cancellationToken
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

    private static bool HasConfiguredApplePublishAction(PowerForgeAppleReleaseOptions options)
        => options.Archive ||
           options.Upload ||
           options.PrepareDistribution ||
           options.SyncMetadata ||
           options.SyncAppInfo ||
           options.SyncScreenshots ||
           options.CheckReleaseReadiness ||
           options.DistributeTestFlight ||
           options.SubmitTestFlightBetaReview ||
           options.SubmitForReview ||
           options.ReleaseApprovedVersion;
}
