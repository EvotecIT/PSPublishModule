using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private void AssertApplePlanStillApproved(
        PowerForgeAppleReleasePlan plan,
        string? expectedPlanSha256)
    {
        if (string.IsNullOrWhiteSpace(expectedPlanSha256))
            return;

        var expected = expectedPlanSha256!.Trim();
        if (expected.Length != 64 || expected.Any(static value => !Uri.IsHexDigit(value)))
            throw new InvalidOperationException("The expected Apple plan SHA-256 must contain exactly 64 hexadecimal characters.");

        var current = CreateApplePlanReceipt(plan).PlanSha256;
        if (!string.Equals(expected, current, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Apple state or release inputs changed after plan approval. Review a new exact plan before allowing mutation.");
        }
    }

    private PowerForgeAppleReleaseReceipt CreateApplePlanReceipt(
        PowerForgeAppleReleasePlan plan,
        IReadOnlyCollection<PowerForgeAppleAppReleaseResult>? checkpointResults = null)
    {
        if (checkpointResults is not null)
        {
            foreach (var app in plan.Apps)
            {
                var result = checkpointResults.SingleOrDefault(candidate =>
                    candidate.Plan.Name.Equals(app.Name, StringComparison.OrdinalIgnoreCase));
                app.ExpectedArchiveSha256 = result?.ArchiveSha256;
            }
        }
        PowerForgeAppleVersionReceipt? versioning = null;
        if (plan.Action == PowerForgeAppleReleaseAction.Version)
            versioning = PlanAppleVersion(plan, whatIf: true);

        var screenshotSpecs = (plan.CheckReleaseReadiness ||
                               (plan.SubmitForReview && !plan.SkipReviewReadinessCheck))
            ? LoadAppleScreenshotSpecs(plan)
            : Array.Empty<(AppStoreConnectScreenshotSyncSpec Spec, string ConfigPath)>();
        var targets = plan.Apps
            .Select(app => CreateApplePlanTarget(plan, app, versioning, screenshotSpecs))
            .ToArray();
        var mutationInputs = CreateAppleMutationInputEvidence(plan);
        var receipt = new PowerForgeAppleReleaseReceipt
        {
            Action = plan.Action,
            SourceCommit = plan.SourceCommit,
            PlanOnly = true,
            CheckedAt = DateTimeOffset.UtcNow,
            Success = true,
            ReceiptPath = FrameworkCompatibility.GetRelativePath(plan.ProjectRoot, plan.PlanReceiptPath).Replace('\\', '/'),
            AdoptExistingBuild = plan.AdoptExistingBuild,
            MutationInputsSha256 = mutationInputs.Sha256,
            MutationInputFiles = mutationInputs.Files,
            Versioning = versioning,
            Targets = targets,
            NextActions = new[] { $"Run Apple action '{plan.Action}' without --plan after reviewing this plan receipt." }
        };
        receipt.PlanSha256 = ComputeApplePlanSha256(receipt);

        if (plan.Automation.WriteReceipt)
            _appleReceiptStore.WritePlan(plan.ProjectRoot, plan.PlanReceiptPath, receipt);
        return receipt;
    }

    private PowerForgeAppleReleaseTargetReceipt CreateApplePlanTarget(
        PowerForgeAppleReleasePlan plan,
        PowerForgeAppleAppReleaseTargetPlan app,
        PowerForgeAppleVersionReceipt? versioning,
        (AppStoreConnectScreenshotSyncSpec Spec, string ConfigPath)[] screenshotSpecs)
    {
        var target = new PowerForgeAppleReleaseTargetReceipt
        {
            Name = app.Name,
            BundleId = app.BundleId,
            Platform = app.Platform,
            DistributionRoute = app.DistributionRoute,
            ProductRole = app.ProductRole,
            ParentTarget = app.ParentTarget,
            Capabilities = app.Capabilities,
            TestFlightPolicy = app.TestFlightPolicy,
            AppId = app.AppStoreConnectAppId,
            AppIdDiscovered = app.AppStoreConnectAppIdDiscovered,
            Version = versioning?.MarketingVersion ?? app.MarketingVersion,
            Build = versioning?.BuildNumber ?? app.BuildNumber,
            ArchivePath = string.IsNullOrWhiteSpace(app.ExpectedArchiveSha256)
                ? null
                : FrameworkCompatibility.GetRelativePath(plan.ProjectRoot, app.ArchivePath).Replace('\\', '/'),
            ArchiveSha256 = app.ExpectedArchiveSha256,
            SkippedSteps = new[] { "plan-only" }
        };
        if (!RequiresObservedApplePlanState(plan, app))
        {
            return target;
        }

        var state = ReadAppleReleaseState(plan, app);
        var platform = AssertSinglePlatformState(state, app);
        var reviewSubmission = platform.ReviewSubmissions.FirstOrDefault(static value => value.IsSubmitted == true) ??
                               platform.ReviewSubmissions.FirstOrDefault();
        target.Version = state.VersionString ?? target.Version;
        target.Build = state.BuildNumber ?? target.Build;
        target.BuildId = platform.MatchedBuild?.Id;
        target.BuildProcessingState = platform.MatchedBuild?.ProcessingState;
        target.DistributionVersionId = platform.Version?.Id;
        target.DistributionState = platform.Version?.AppStoreState ?? platform.Version?.AppVersionState;
        target.BuildSelected = platform.MatchedBuildSelected;
        target.TestFlightInternalState = platform.BetaDetail?.InternalBuildState;
        target.TestFlightExternalState = platform.BetaDetail?.ExternalBuildState;
        target.TestFlightReviewState = platform.BetaReviewSubmission?.BetaReviewState;
        target.AppReviewSubmissionId = reviewSubmission?.Id;
        target.AppReviewState = reviewSubmission?.State;
        target.NextActions = platform.NextActions;
        if (plan.CheckReleaseReadiness ||
            (plan.SubmitForReview && !plan.SkipReviewReadinessCheck))
        {
            var values = ResolveAppleDistributionValues(app, versionUpdate: null);
            var matchingScreenshotSpec = ResolveMatchingScreenshotSpec(
                screenshotSpecs,
                app,
                values.MarketingVersion,
                required: screenshotSpecs.Length > 0);
            var boundScreenshotSpec = matchingScreenshotSpec is null
                ? null
                : BindScreenshotSpec(matchingScreenshotSpec.Value.Spec, app, values.MarketingVersion);
            var readiness = _checkAppleReleaseReadiness(
                CreateAppStoreConnectCredential(plan),
                new AppStoreConnectReleaseReadinessRequest
                {
                    AppId = app.AppStoreConnectAppId!,
                    VersionString = values.MarketingVersion,
                    BuildNumber = values.BuildNumber,
                    Platform = app.Platform,
                    ScreenshotSpec = boundScreenshotSpec
                });
            target.ReadinessChecked = true;
            target.ReadyForSubmission = readiness.IsReady;
            target.ScreenshotCount = readiness.ScreenshotSets.Sum(static set => set.Count);
            target.ScreenshotDeliveryStates = readiness.ScreenshotSets
                .SelectMany(static set => set.AssetDeliveryStates)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static stateValue => stateValue, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            target.ReadinessChecks = readiness.Checks
                .OrderBy(static check => check.Name, StringComparer.Ordinal)
                .ThenBy(static check => check.Message, StringComparer.Ordinal)
                .ToArray();
            target.ReadinessSha256 = ComputeReadinessSha256(readiness);
        }
        return target;
    }

    private static bool RequiresObservedApplePlanState(
        PowerForgeAppleReleasePlan plan,
        PowerForgeAppleAppReleaseTargetPlan app)
    {
        if (!UsesAppStoreConnect(app) || !ShouldExecuteAppleTarget(plan.Action, app))
            return false;
        if (plan.Action is PowerForgeAppleReleaseAction.SubmitTestFlightReview or
            PowerForgeAppleReleaseAction.SubmitAppReview or
            PowerForgeAppleReleaseAction.Release)
        {
            return true;
        }

        return plan.SubmitTestFlightBetaReview ||
               plan.SubmitForReview ||
               plan.ReleaseApprovedVersion;
    }

    private static string ComputeApplePlanSha256(PowerForgeAppleReleaseReceipt receipt)
    {
        var canonical = new
        {
            receipt.SchemaVersion,
            receipt.Action,
            receipt.SourceCommit,
            receipt.PlanOnly,
            receipt.AdoptExistingBuild,
            receipt.MutationInputsSha256,
            MutationInputFiles = receipt.MutationInputFiles
                .OrderBy(static value => value.Key, StringComparer.Ordinal)
                .ToArray(),
            receipt.Success,
            receipt.ErrorMessage,
            receipt.Versioning,
            receipt.Targets,
            receipt.Cleanup,
            receipt.Diagnostics,
            receipt.NextActions
        };
        return ComputeStableSha256(canonical);
    }

    private (Dictionary<string, string> Files, string Sha256) CreateAppleMutationInputEvidence(
        PowerForgeAppleReleasePlan plan)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        var configuredInputs = new List<string>();
        if (plan.SyncScreenshots || plan.CheckReleaseReadiness ||
            (plan.SubmitForReview && !plan.SkipReviewReadinessCheck))
        {
            if (!string.IsNullOrWhiteSpace(plan.ScreenshotConfigPath))
                configuredInputs.Add(plan.ScreenshotConfigPath!);
            configuredInputs.AddRange(plan.ScreenshotConfigPaths);
        }
        if (plan.SyncMetadata)
        {
            if (!string.IsNullOrWhiteSpace(plan.MetadataConfigPath))
                configuredInputs.Add(plan.MetadataConfigPath!);
            configuredInputs.AddRange(plan.MetadataConfigPaths);
        }
        if (plan.SyncAppInfo)
        {
            if (!string.IsNullOrWhiteSpace(plan.AppInfoConfigPath))
                configuredInputs.Add(plan.AppInfoConfigPath!);
            configuredInputs.AddRange(plan.AppInfoConfigPaths);
        }
        if (plan.CheckGovernance)
        {
            if (!string.IsNullOrWhiteSpace(plan.GovernanceConfigPath))
                configuredInputs.Add(plan.GovernanceConfigPath!);
            configuredInputs.AddRange(plan.GovernanceConfigPaths);
        }
        if (plan.Action == PowerForgeAppleReleaseAction.Version && !string.IsNullOrWhiteSpace(plan.VersionSourcePath))
            configuredInputs.Add(plan.VersionSourcePath!);
        var effectiveInputs = configuredInputs
            .Distinct(Path.DirectorySeparatorChar == '\\'
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal)
            .ToArray();
        foreach (var path in effectiveInputs)
            AddApplePlanInputFile(plan.ProjectRoot, path!, files);

        if (plan.SyncScreenshots || plan.CheckReleaseReadiness ||
            (plan.SubmitForReview && !plan.SkipReviewReadinessCheck))
        {
            foreach (var configured in LoadAppleScreenshotSpecs(plan))
            {
                var baseDirectory = Path.GetDirectoryName(configured.ConfigPath) ?? plan.ProjectRoot;
                foreach (var set in configured.Spec.ScreenshotSets)
                {
                    if (string.IsNullOrWhiteSpace(set.Path))
                        continue;
                    var assetPath = ResolveOutputPath(baseDirectory, set.Path);
                    EnsurePathWithinProjectRoot(plan.ProjectRoot, assetPath, "Apple screenshot plan input");
                    if (File.Exists(assetPath))
                    {
                        AddApplePlanInputFile(plan.ProjectRoot, assetPath, files);
                    }
                    else if (Directory.Exists(assetPath))
                    {
                        foreach (var file in Directory.EnumerateFiles(assetPath, "*", SearchOption.AllDirectories)
                                     .OrderBy(static value => value, StringComparer.Ordinal))
                            AddApplePlanInputFile(plan.ProjectRoot, file, files);
                    }
                    else
                    {
                        throw new FileNotFoundException($"Apple screenshot plan input was not found: {assetPath}", assetPath);
                    }
                }
            }
        }

        var options = new
        {
            plan.Archive,
            plan.Upload,
            plan.PrepareDistribution,
            plan.SelectBuildForDistribution,
            plan.AllowUnprocessedDistributionBuild,
            plan.SyncMetadata,
            plan.SyncAppInfo,
            plan.SyncScreenshots,
            plan.ReplaceScreenshots,
            plan.CheckGovernance,
            plan.CheckReleaseReadiness,
            plan.DistributeTestFlight,
            TestFlightBetaGroupIds = plan.TestFlightBetaGroupIds.OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
            TestFlightBetaGroupNames = plan.TestFlightBetaGroupNames.OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
            TestFlightTesterEmails = plan.TestFlightTesterEmails.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            plan.CreateMissingTestFlightTesters,
            plan.AllowUnprocessedTestFlightBuild,
            plan.SubmitTestFlightBetaReview,
            plan.SubmitForReview,
            plan.AllowUnselectedReviewBuild,
            plan.AllowUnprocessedReviewBuild,
            plan.SkipReviewReadinessCheck,
            plan.AllowReviewSubmissionWhenNotReady,
            plan.ReleaseApprovedVersion,
            plan.AllowNonPendingDeveloperRelease,
            Files = files.OrderBy(static value => value.Key, StringComparer.Ordinal).ToArray()
        };
        return (files, ComputeStableSha256(options));
    }

    private static void AddApplePlanInputFile(
        string projectRoot,
        string path,
        IDictionary<string, string> files)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Apple plan input was not found: {fullPath}", fullPath);
        EnsurePathWithinProjectRoot(projectRoot, fullPath, "Apple plan input");
        using var stream = File.OpenRead(fullPath);
        using var sha256 = SHA256.Create();
        var hash = BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        var relative = FrameworkCompatibility.GetRelativePath(projectRoot, fullPath).Replace('\\', '/');
        files[relative] = hash;
    }

    private static string ComputeStableSha256<T>(T value)
    {
        var options = CreateJsonOptions();
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, options);
        using var sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(payload)).Replace("-", string.Empty);
    }

    private static string ComputeReadinessSha256(AppStoreConnectReleaseReadinessResult readiness)
    {
        var canonical = new
        {
            readiness.AppId,
            readiness.VersionString,
            readiness.BuildNumber,
            readiness.Platform,
            readiness.IsReady,
            readiness.Version,
            readiness.Build,
            readiness.SelectedBuildId,
            readiness.Localization,
            ScreenshotSets = readiness.ScreenshotSets
                .OrderBy(static set => set.ScreenshotDisplayType, StringComparer.Ordinal)
                .Select(static set => new
                {
                    set.ScreenshotDisplayType,
                    set.ScreenshotSetId,
                    set.Count,
                    AssetDeliveryStates = set.AssetDeliveryStates.OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
                    FileNames = set.FileNames.OrderBy(static value => value, StringComparer.Ordinal).ToArray()
                })
                .ToArray(),
            Checks = readiness.Checks
                .OrderBy(static check => check.Name, StringComparer.Ordinal)
                .ThenBy(static check => check.Message, StringComparer.Ordinal)
                .Select(static check => new { check.Name, check.Passed, check.Message })
                .ToArray()
        };
        return ComputeStableSha256(canonical);
    }

    private PowerForgeAppleVersionReceipt SelectAppleVersion(PowerForgeAppleReleasePlan plan)
    {
        var versioning = PlanAppleVersion(plan, whatIf: false);
        foreach (var app in plan.Apps)
        {
            app.MarketingVersion = versioning.MarketingVersion;
            app.BuildNumber = versioning.BuildNumber;
        }

        return versioning;
    }

    private PowerForgeAppleAppReleaseResult[] RunAppleVersion(PowerForgeAppleReleasePlan plan)
    {
        var generatedProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<PowerForgeAppleAppReleaseResult>();
        foreach (var app in plan.Apps)
        {
            var result = new PowerForgeAppleAppReleaseResult
            {
                Plan = app,
                Success = true,
                SkippedSteps = new[] { "archive", "upload", "distribution", "review", "release" }
            };
            if (generatedProjects.Add(app.ProjectPath))
            {
                var generationPlan = new PowerForgeAppleAppReleaseTargetPlan
                {
                    Name = app.Name,
                    ProjectPath = app.ProjectPath,
                    GenerateProjectIfMissing = true,
                    RegenerateProject = true,
                    XcodeGenExecutable = app.XcodeGenExecutable,
                    ProjectGenerationTimeoutSeconds = app.ProjectGenerationTimeoutSeconds
                };
                result.ProjectGenerated = _generateAppleProject(generationPlan);
            }
            results.Add(result);
        }

        return results.ToArray();
    }

    private PowerForgeAppleVersionReceipt PlanAppleVersion(PowerForgeAppleReleasePlan plan, bool whatIf)
    {
        if (string.IsNullOrWhiteSpace(plan.VersionSourcePath))
            throw new InvalidOperationException("Apple version source path is required for Version.");
        if (string.IsNullOrWhiteSpace(plan.RequestedMarketingVersion))
            throw new InvalidOperationException("Requested Apple marketing version is required for Version.");

        var source = new AppleReleaseVersionSourceService();
        var current = source.Read(plan.VersionSourcePath!);
        if (!long.TryParse(current.BuildNumber, out var currentBuild) || currentBuild < 0)
            throw new InvalidOperationException($"Apple version source build number '{current.BuildNumber}' is not a non-negative integer.");

        var storeApps = plan.Apps.Where(UsesAppStoreConnect).ToArray();
        var requested = plan.RequestedMarketingVersion!.Trim();
        var isPattern = requested.IndexOf("X", StringComparison.OrdinalIgnoreCase) >= 0;
        var highestRemote = 0L;
        AppleReleaseMarketingVersionResolution? resolution = null;
        if (storeApps.Length > 0)
        {
            var credential = CreateAppStoreConnectCredential(plan);
            if (isPattern)
            {
                var inventories = storeApps
                    .Select(app => _getAppleVersionInventory(credential, app.AppStoreConnectAppId!, app.Platform))
                    .ToArray();
                highestRemote = GetHighestRemoteBuildNumber(
                    inventories.SelectMany(static inventory => inventory.Builds));
                resolution = AppleReleaseMarketingVersionResolver.Resolve(
                    requested,
                    current.MarketingVersion,
                    inventories.SelectMany(static inventory => inventory.AppStoreVersions),
                    inventories.SelectMany(static inventory => inventory.Builds));
            }
            else
            {
                highestRemote = storeApps
                    .Select(app => _getHighestAppleBuildNumber(credential, app.AppStoreConnectAppId!, app.Platform))
                    .DefaultIfEmpty(0)
                    .Max();
            }
        }
        else if (isPattern)
        {
            resolution = AppleReleaseMarketingVersionResolver.Resolve(
                requested,
                current.MarketingVersion,
                Array.Empty<AppStoreConnectVersionInfo>(),
                Array.Empty<AppStoreConnectBuildInfo>());
        }

        var requestedVersion = resolution?.MarketingVersion ?? requested;
        var nextBuild = string.Equals(current.MarketingVersion, requestedVersion, StringComparison.OrdinalIgnoreCase) &&
                        currentBuild > highestRemote
            ? currentBuild
            : checked(Math.Max(currentBuild, highestRemote) + 1);
        var receipt = source.Update(
            plan.VersionSourcePath!,
            requestedVersion,
            nextBuild.ToString(System.Globalization.CultureInfo.InvariantCulture),
            highestRemote,
            whatIf);
        receipt.RequestedMarketingVersion = requested;
        receipt.MarketingVersionPattern = resolution?.Pattern;
        receipt.HighestRemoteMarketingVersion = resolution?.HighestRemoteMarketingVersion;
        receipt.ReusedUnreleasedMarketingVersion = resolution?.ReusedUnreleasedMarketingVersion == true;
        receipt.SourcePath = FrameworkCompatibility.GetRelativePath(plan.ProjectRoot, plan.VersionSourcePath!).Replace('\\', '/');
        return receipt;
    }

    private static long GetHighestAppleBuildNumber(
        AppStoreConnectApiCredential credential,
        string appId,
        ApplePlatform platform)
    {
        using var client = new AppStoreConnectClient(credential);
        return GetHighestRemoteBuildNumber(GetAppleBuildInventory(client, appId, platform));
    }

    private static PowerForgeAppleRemoteVersionInventory GetAppleVersionInventory(
        AppStoreConnectApiCredential credential,
        string appId,
        ApplePlatform platform)
    {
        using var client = new AppStoreConnectClient(credential);
        var builds = GetAppleBuildInventory(client, appId, platform);
        return new PowerForgeAppleRemoteVersionInventory
        {
            AppStoreVersions = client.GetVersionsAsync(appId, platform: platform, limit: 200)
                .GetAwaiter()
                .GetResult(),
            Builds = builds
        };
    }

    private static AppStoreConnectBuildInfo[] GetAppleBuildInventory(
        AppStoreConnectClient client,
        string appId,
        ApplePlatform platform)
    {
        var expectedPlatform = AppStoreConnectClient.ToAppStoreConnectPlatform(platform);
        var builds = client.GetBuildsWithPreReleaseVersionAsync(appId, limit: 200)
            .GetAwaiter()
            .GetResult();
        AppleReleaseMarketingVersionResolver.ValidateRemoteEvidence(
            Array.Empty<AppStoreConnectVersionInfo>(),
            builds);

        return builds
            .Where(build => string.Equals(build.Platform, expectedPlatform, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static long GetHighestRemoteBuildNumber(IEnumerable<AppStoreConnectBuildInfo> builds)
    {
        var highest = 0L;
        foreach (var build in builds)
        {
            if (string.IsNullOrWhiteSpace(build.Version) ||
                !long.TryParse(
                    build.Version,
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var number) ||
                number < 0)
            {
                throw new InvalidOperationException(
                    $"App Store Connect build number '{build.Version ?? "<missing>"}' is not a non-negative integer. Resolve the incompatible remote build before automatic version selection.");
            }

            highest = Math.Max(highest, number);
        }

        return highest;
    }
}
