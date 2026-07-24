using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private static void ApplyAppleAction(
        PowerForgeAppleReleaseOptions? options,
        PowerForgeReleaseRequest request)
    {
        if (request.AppleAction == PowerForgeAppleReleaseAction.Configured && options is null)
            return;
        if (options is null)
            throw new InvalidOperationException("The selected Apple action requires an AppleApps release configuration.");
        if (!request.PlanOnly &&
            !request.ValidateOnly &&
            RequiresExplicitConfirmation(request.AppleAction, options) &&
            !request.AppleActionConfirmed)
        {
            throw new InvalidOperationException(
                $"Apple action '{request.AppleAction}' requires explicit confirmation. " +
                "Use --confirm-apple-action or PowerShell -ConfirmAppleAction after reviewing the compact status receipt.");
        }
        if (request.AppleAction == PowerForgeAppleReleaseAction.Configured)
            return;

        options.Archive = false;
        options.Upload = false;
        options.PrepareDistribution = false;
        options.SelectBuildForDistribution = false;
        options.SyncMetadata = false;
        options.SyncAppInfo = false;
        options.SyncScreenshots = false;
        options.CheckReleaseReadiness = false;
        options.DistributeTestFlight = false;
        options.SubmitTestFlightBetaReview = false;
        options.SubmitForReview = false;
        options.ReleaseApprovedVersion = false;

        switch (request.AppleAction)
        {
            case PowerForgeAppleReleaseAction.Status:
            case PowerForgeAppleReleaseAction.Cleanup:
                break;
            case PowerForgeAppleReleaseAction.Archive:
                options.Archive = true;
                break;
            case PowerForgeAppleReleaseAction.Upload:
                options.Archive = true;
                options.Upload = true;
                break;
            case PowerForgeAppleReleaseAction.UploadExisting:
                options.Upload = true;
                break;
            case PowerForgeAppleReleaseAction.Prepare:
                options.PrepareDistribution = true;
                options.SelectBuildForDistribution = true;
                options.SyncMetadata = HasConfiguredPath(options.MetadataConfigPath, options.MetadataConfigPaths);
                options.SyncAppInfo = HasConfiguredPath(options.AppInfoConfigPath, options.AppInfoConfigPaths);
                options.CheckReleaseReadiness = true;
                break;
            case PowerForgeAppleReleaseAction.Screenshots:
                options.SyncScreenshots = true;
                options.CheckReleaseReadiness = true;
                break;
            case PowerForgeAppleReleaseAction.TestFlight:
                options.DistributeTestFlight = true;
                break;
            case PowerForgeAppleReleaseAction.SubmitTestFlightReview:
                options.SubmitTestFlightBetaReview = true;
                break;
            case PowerForgeAppleReleaseAction.SubmitAppReview:
                options.SubmitForReview = true;
                break;
            case PowerForgeAppleReleaseAction.Release:
                options.ReleaseApprovedVersion = true;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(request.AppleAction), request.AppleAction, "Unsupported Apple release action.");
        }
    }

    private static bool RequiresExplicitConfirmation(
        PowerForgeAppleReleaseAction action,
        PowerForgeAppleReleaseOptions options)
        => (action == PowerForgeAppleReleaseAction.Configured &&
            (options.SubmitTestFlightBetaReview ||
             options.SubmitForReview ||
             options.ReleaseApprovedVersion ||
             (options.SyncScreenshots && options.ReplaceScreenshots))) ||
           action == PowerForgeAppleReleaseAction.SubmitTestFlightReview ||
           action == PowerForgeAppleReleaseAction.SubmitAppReview ||
           action == PowerForgeAppleReleaseAction.Release ||
           (action == PowerForgeAppleReleaseAction.Screenshots && options.ReplaceScreenshots);

    private static bool HasConfiguredPath(string? path, string[]? paths)
        => !string.IsNullOrWhiteSpace(path) ||
           (paths ?? Array.Empty<string>()).Any(static value => !string.IsNullOrWhiteSpace(value));

    private static bool IsUploadAction(PowerForgeAppleReleaseAction action)
        => action == PowerForgeAppleReleaseAction.Upload ||
           action == PowerForgeAppleReleaseAction.UploadExisting;

    private static void ValidateAppleAutomation(PowerForgeAppleReleaseAutomationOptions automation)
    {
        if (string.IsNullOrWhiteSpace(automation.ReceiptPath))
            throw new InvalidOperationException("AppleApps.Automation.ReceiptPath is required.");
        if (automation.ProcessingTimeoutSeconds <= 0)
            throw new InvalidOperationException("AppleApps.Automation.ProcessingTimeoutSeconds must be greater than zero.");
        if (automation.PollIntervalSeconds <= 0)
            throw new InvalidOperationException("AppleApps.Automation.PollIntervalSeconds must be greater than zero.");
        if (automation.PollIntervalSeconds > automation.ProcessingTimeoutSeconds)
            throw new InvalidOperationException("AppleApps.Automation.PollIntervalSeconds cannot exceed ProcessingTimeoutSeconds.");
        if (automation.MinimumFreeSpaceGB < 0)
            throw new InvalidOperationException("AppleApps.Automation.MinimumFreeSpaceGB cannot be negative.");
        if (automation.ArtifactRetentionDays < 0)
            throw new InvalidOperationException("AppleApps.Automation.ArtifactRetentionDays cannot be negative.");
    }

    private static void EnsurePathWithinProjectRoot(string projectRoot, string path, string settingName)
    {
        var root = Path.GetFullPath(projectRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!candidate.Equals(root, comparison) &&
            !candidate.StartsWith(root + Path.DirectorySeparatorChar, comparison) &&
            !candidate.StartsWith(root + Path.AltDirectorySeparatorChar, comparison))
        {
            throw new InvalidOperationException($"{settingName} must remain inside AppleApps.ProjectRoot.");
        }

        EnsureNoReparsePointsInExistingPath(root, candidate, settingName);
    }

    private static void EnsureNoReparsePointsInExistingPath(
        string projectRoot,
        string path,
        string settingName)
    {
        var root = Path.GetFullPath(projectRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var current = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        while (true)
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"{settingName} must not traverse a symbolic link or reparse point: {current}");
            }

            if (current.Equals(root, comparison))
                break;
            current = Path.GetDirectoryName(current)
                ?? throw new InvalidOperationException($"{settingName} could not be validated inside AppleApps.ProjectRoot.");
        }
    }

    private bool TryResumeAppleUpload(
        PowerForgeAppleReleasePlan plan,
        PowerForgeAppleAppReleaseTargetPlan app,
        PowerForgeAppleAppReleaseResult result)
    {
        if (!IsUploadAction(plan.Action) || !plan.Automation.Resume)
            return false;

        var state = ReadAppleReleaseState(plan, app);
        var platform = AssertSinglePlatformState(state, app);
        if (platform.MatchedBuild is null)
            return false;
        if (IsTerminalAppleBuildFailure(platform.MatchedBuild.ProcessingState))
        {
            throw new AppleBuildProcessingException(
                $"App Store Connect already contains build {state.VersionString} ({state.BuildNumber}) " +
                $"in terminal processing state '{platform.MatchedBuild.ProcessingState}' for '{app.Name}'. " +
                "Diagnose the processing failure and increment the build number before uploading again.",
                state);
        }

        if (plan.Automation.WaitForProcessing &&
            !string.Equals(platform.MatchedBuild.ProcessingState, "VALID", StringComparison.OrdinalIgnoreCase))
        {
            state = WaitForAppleBuild(plan, app, state);
        }

        result.RemoteState = state;
        result.ResumedExistingBuild = true;
        result.SkippedSteps = new[] { "archive", "upload" };
        return true;
    }

    private AppStoreConnectReleaseStateResult ReadAppleReleaseState(
        PowerForgeAppleReleasePlan plan,
        PowerForgeAppleAppReleaseTargetPlan app)
    {
        var version = ResolveAppleDistributionValues(app, versionUpdate: null);
        return _getAppleReleaseState(new AppStoreConnectReleaseStateRequest
        {
            Credential = CreateAppStoreConnectCredential(plan),
            AppId = app.AppStoreConnectAppId!,
            VersionString = version.MarketingVersion,
            BuildNumber = version.BuildNumber,
            Platforms = new[] { app.Platform },
            BetaGroupIds = plan.TestFlightBetaGroupIds,
            BetaGroupNames = plan.TestFlightBetaGroupNames,
            IncludeAllBetaGroups = false
        });
    }

    private AppStoreConnectReleaseStateResult WaitForAppleBuild(
        PowerForgeAppleReleasePlan plan,
        PowerForgeAppleAppReleaseTargetPlan app,
        AppStoreConnectReleaseStateResult? initial = null,
        string? buildUploadId = null)
    {
        var state = initial ?? ReadAppleReleaseState(plan, app);
        var maximumChecks = Math.Max(
            1,
            (int)Math.Ceiling((double)plan.Automation.ProcessingTimeoutSeconds / plan.Automation.PollIntervalSeconds) + 1);
        for (var check = 0; check < maximumChecks; check++)
        {
            var platform = AssertSinglePlatformState(state, app);
            var processingState = platform.MatchedBuild?.ProcessingState;
            if (string.Equals(processingState, "VALID", StringComparison.OrdinalIgnoreCase))
                return state;
            if (IsTerminalAppleBuildFailure(processingState))
            {
                throw new AppleBuildProcessingException(
                    $"App Store Connect marked build {state.VersionString} ({state.BuildNumber}) " +
                    $"as '{processingState}' for '{app.Name}'.",
                    state);
            }
            if (!string.IsNullOrWhiteSpace(buildUploadId))
            {
                var upload = _getAppleBuildUpload(CreateAppStoreConnectCredential(plan), buildUploadId!);
                if (upload is not null && IsTerminalAppleBuildFailure(upload.State))
                {
                    var issues = upload.Errors
                        .Select(static issue => FormatAppleBuildUploadIssue(issue))
                        .Where(static issue => !string.IsNullOrWhiteSpace(issue))
                        .ToArray();
                    var issueDetail = issues.Length == 0
                        ? string.Empty
                        : $" {string.Join(" ", issues)}";
                    throw new AppleBuildProcessingException(
                        $"App Store Connect rejected uploaded build {state.VersionString} ({state.BuildNumber}) " +
                        $"for '{app.Name}' in build-upload state '{upload.State}'.{issueDetail}",
                        state);
                }
            }
            if (check == maximumChecks - 1)
                break;

            _delay(TimeSpan.FromSeconds(plan.Automation.PollIntervalSeconds));
            state = ReadAppleReleaseState(plan, app);
        }

        throw new AppleBuildProcessingException(
            $"Timed out after {plan.Automation.ProcessingTimeoutSeconds} seconds waiting for " +
            $"App Store Connect build processing for '{app.Name}'.",
            state);
    }

    private static bool IsTerminalAppleBuildFailure(string? state)
        => string.Equals(state, "INVALID", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(state, "FAILED", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(state, "ERROR", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(state, "REJECTED", StringComparison.OrdinalIgnoreCase);

    private static string FormatAppleBuildUploadIssue(AppStoreConnectBuildUploadIssue issue)
    {
        var code = string.IsNullOrWhiteSpace(issue.Code) ? null : issue.Code!.Trim();
        var description = string.IsNullOrWhiteSpace(issue.Description) ? null : issue.Description!.Trim();
        return (code, description) switch
        {
            (not null, not null) => $"[{code}] {description}",
            (not null, null) => $"[{code}]",
            (null, not null) => description,
            _ => string.Empty
        };
    }

    private static AppStoreConnectPlatformReleaseState AssertSinglePlatformState(
        AppStoreConnectReleaseStateResult state,
        PowerForgeAppleAppReleaseTargetPlan app)
    {
        var platform = state.Platforms.SingleOrDefault(value => value.Platform == app.Platform);
        return platform ?? throw new InvalidOperationException($"App Store Connect did not return {app.Platform} state for '{app.Name}'.");
    }

    private PowerForgeAppleReleaseReceipt CompleteAppleReleaseReceipt(
        PowerForgeAppleReleasePlan plan,
        PowerForgeAppleAppReleaseResult[] results,
        PowerForgeAppleReleaseCleanupReceipt cleanup)
    {
        var resultByName = results.ToDictionary(static result => result.Plan.Name, StringComparer.OrdinalIgnoreCase);
        var remoteAction = plan.Action != PowerForgeAppleReleaseAction.Archive &&
                           plan.Action != PowerForgeAppleReleaseAction.Cleanup;
        foreach (var app in plan.Apps)
        {
            if (!resultByName.TryGetValue(app.Name, out var result))
                continue;
            if (remoteAction && result.Success && result.RemoteState is null)
            {
                try
                {
                    result.RemoteState = ReadAppleReleaseState(plan, app);
                }
                catch (Exception exception)
                {
                    result.Success = false;
                    result.ErrorMessage =
                        $"Unable to read the final App Store Connect state for '{app.Name}': {exception.Message}";
                }
            }
        }

        if (IsUploadAction(plan.Action) &&
            plan.Automation.CleanupAfterProcessing &&
            results.Length == plan.Apps.Length &&
            results.All(result => IsAppleBuildValid(result.RemoteState, result.Plan)))
        {
            try
            {
                cleanup = MergeCleanup(cleanup, _appleArtifactService.RemoveCurrentArtifacts(plan));
            }
            catch (Exception exception)
            {
                foreach (var result in results.Where(static result => result.Success))
                {
                    result.Success = false;
                    result.ErrorMessage =
                        $"Apple build is valid, but local artifact cleanup failed: {exception.Message}";
                }
            }
        }

        var targets = plan.Apps.Select(app =>
        {
            resultByName.TryGetValue(app.Name, out var result);
            var state = result?.RemoteState;
            var platform = state?.Platforms.SingleOrDefault(value => value.Platform == app.Platform);
            var build = platform?.MatchedBuild ?? platform?.SelectedBuild;
            var review = platform?.ReviewSubmissions.FirstOrDefault(static value => value.IsSubmitted == true) ??
                         platform?.ReviewSubmissions.FirstOrDefault();
            (string? MarketingVersion, string? BuildNumber) values = (null, null);
            if (plan.Action == PowerForgeAppleReleaseAction.Archive)
            {
                try
                {
                    var resolved = ResolveAppleDistributionValues(app, result?.VersionUpdate);
                    values = (resolved.MarketingVersion, resolved.BuildNumber);
                }
                catch
                {
                    values = (
                        result?.VersionUpdate?.After.MarketingVersion ?? app.MarketingVersion,
                        result?.VersionUpdate?.After.BuildNumber ?? app.BuildNumber);
                }
            }
            else if (plan.Action != PowerForgeAppleReleaseAction.Cleanup)
            {
                try
                {
                    var resolved = ResolveAppleDistributionValues(app, result?.VersionUpdate);
                    values = (resolved.MarketingVersion, resolved.BuildNumber);
                }
                catch (Exception exception)
                {
                    if (result is not null &&
                        string.IsNullOrWhiteSpace(result.ErrorMessage) &&
                        !result.SkippedSteps.Contains("preflight", StringComparer.OrdinalIgnoreCase))
                    {
                        result.Success = false;
                        result.ErrorMessage =
                            $"Unable to resolve release identity for receipt: {exception.Message}";
                    }
                }
            }
            var readiness = result?.Distribution?.Readiness;
            var betaGroupsConfigured = plan.TestFlightBetaGroupIds.Length > 0 ||
                                       plan.TestFlightBetaGroupNames.Length > 0;
            return new PowerForgeAppleReleaseTargetReceipt
            {
                Name = app.Name,
                ErrorMessage = result?.ErrorMessage,
                BundleId = app.BundleId,
                Platform = app.Platform,
                AppId = app.AppStoreConnectAppId,
                Version = state?.VersionString ?? values.MarketingVersion,
                Build = state?.BuildNumber ?? values.BuildNumber,
                BuildId = build?.Id,
                BuildProcessingState = build?.ProcessingState,
                BuildUploadId = result?.Upload?.BuildUploadId,
                DistributionVersionId = platform?.Version?.Id,
                DistributionState = platform?.Version?.AppStoreState ?? platform?.Version?.AppVersionState,
                BuildSelected = platform?.MatchedBuildSelected,
                TestFlightInternalState = platform?.BetaDetail?.InternalBuildState,
                TestFlightExternalState = platform?.BetaDetail?.ExternalBuildState,
                TestFlightReviewState = platform?.BetaReviewSubmission?.BetaReviewState,
                AppReviewSubmissionId = review?.Id,
                AppReviewState = review?.State,
                ReadinessChecked = readiness is not null,
                ReadyForSubmission = readiness?.IsReady,
                ScreenshotCount = readiness?.ScreenshotSets.Sum(static set => set.Count),
                ScreenshotDeliveryStates = readiness?.ScreenshotSets
                    .SelectMany(static set => set.AssetDeliveryStates)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                TestFlightBetaGroupsConfigured = betaGroupsConfigured,
                ArchiveCreated = result?.Archive?.Succeeded == true,
                ProjectGenerated = result?.ProjectGenerated == true,
                UploadPerformed = result?.Upload?.Succeeded == true,
                ResumedExistingBuild = result?.ResumedExistingBuild == true,
                SkippedSteps = result?.SkippedSteps ?? Array.Empty<string>(),
                NextActions = BuildAppleReceiptNextActions(platform?.NextActions, betaGroupsConfigured)
            };
        }).ToArray();
        var errors = results
            .Where(static result => !result.Success && !string.IsNullOrWhiteSpace(result.ErrorMessage))
            .Select(static result => result.ErrorMessage!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var receipt = new PowerForgeAppleReleaseReceipt
        {
            Action = plan.Action,
            CheckedAt = DateTimeOffset.UtcNow,
            Success = results.Length == plan.Apps.Length &&
                      results.All(static result => result.Success),
            ErrorMessage = errors.Length == 0 ? null : string.Join(" ", errors),
            ReceiptPath = FrameworkCompatibility.GetRelativePath(plan.ProjectRoot, plan.ReceiptPath).Replace('\\', '/'),
            Targets = targets,
            Cleanup = cleanup,
            NextActions = targets
                .SelectMany(target => target.NextActions.Select(action => $"{target.Name}: {action}"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };

        if (plan.Automation.WriteReceipt)
            WriteAppleReceipt(plan.ProjectRoot, plan.ReceiptPath, receipt);
        return receipt;
    }

    private static string[] BuildAppleReceiptNextActions(
        string[]? actions,
        bool betaGroupsConfigured)
    {
        const string submitBetaReview = "Submit the TestFlight build to Beta App Review.";
        const string configureBetaGroup =
            "External TestFlight is eligible; configure the intended beta group before explicitly requesting Beta App Review.";

        return (actions ?? Array.Empty<string>())
            .Select(action =>
                !betaGroupsConfigured &&
                string.Equals(action, submitBetaReview, StringComparison.OrdinalIgnoreCase)
                    ? configureBetaGroup
                    : action)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsAppleBuildValid(
        AppStoreConnectReleaseStateResult? state,
        PowerForgeAppleAppReleaseTargetPlan app)
    {
        var platform = state?.Platforms.SingleOrDefault(value => value.Platform == app.Platform);
        return string.Equals(platform?.MatchedBuild?.ProcessingState, "VALID", StringComparison.OrdinalIgnoreCase);
    }

    private static PowerForgeAppleReleaseCleanupReceipt MergeCleanup(
        PowerForgeAppleReleaseCleanupReceipt first,
        PowerForgeAppleReleaseCleanupReceipt second)
        => new()
        {
            RemovedPaths = first.RemovedPaths
                .Concat(second.RemovedPaths)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            ReclaimedBytes = first.ReclaimedBytes + second.ReclaimedBytes,
            FreeSpaceGB = second.FreeSpaceGB ?? first.FreeSpaceGB
        };

    private static void WriteAppleReceipt(
        string projectRoot,
        string path,
        PowerForgeAppleReleaseReceipt receipt)
    {
        EnsurePathWithinProjectRoot(projectRoot, path, "AppleApps.Automation.ReceiptPath");
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        var options = CreateJsonOptions();
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.WriteIndented = true;
        var payload = JsonSerializer.Serialize(receipt, options);
        var temporaryPath = Path.Combine(
            directory ?? projectRoot,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, payload);
            if (File.Exists(path))
                File.Replace(temporaryPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else
                File.Move(temporaryPath, path);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static AppStoreConnectReleaseStateResult GetAppleReleaseState(AppStoreConnectReleaseStateRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (request.Credential is null)
            throw new ArgumentException("Credential is required.", nameof(request));

        using var client = new AppStoreConnectClient(request.Credential);
        return new AppStoreConnectReleaseStateService(client).GetAsync(request).GetAwaiter().GetResult();
    }

    private static AppStoreConnectBuildUploadInfo? GetAppleBuildUpload(
        AppStoreConnectApiCredential credential,
        string buildUploadId)
    {
        using var client = new AppStoreConnectClient(credential);
        return client.GetBuildUploadAsync(buildUploadId).GetAwaiter().GetResult();
    }

    private sealed class AppleBuildProcessingException : InvalidOperationException
    {
        internal AppleBuildProcessingException(
            string message,
            AppStoreConnectReleaseStateResult state)
            : base(message)
        {
            State = state;
        }

        internal AppStoreConnectReleaseStateResult State { get; }
    }
}
