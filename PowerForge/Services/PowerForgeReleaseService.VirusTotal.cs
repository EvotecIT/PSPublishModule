namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    internal static bool ShouldPublishVirusTotalMonitor(
        PowerForgeReleaseSpec spec,
        PowerForgeReleaseRequest request,
        bool explicitAppleAction,
        bool runModule,
        bool runPackages,
        bool runTools,
        bool publishUnifiedGitHub)
    {
        if (spec.VirusTotal is not { Enabled: true } ||
            request.PlanOnly ||
            request.ValidateOnly ||
            explicitAppleAction)
        {
            return false;
        }

        if (publishUnifiedGitHub && (runModule || runPackages || runTools))
            return true;

        if (runModule && spec.Module is not null)
        {
            var packagePublishingRequested =
                !request.ModuleOnly &&
                ((request.PublishNuget ?? spec.Packages?.PublishNuget) == true ||
                 (request.PublishProjectGitHub ?? spec.Packages?.PublishGitHub) == true);
            if (ResolveModuleRunMode(spec.Module, request, packagePublishingRequested) == ConfigurationGateMode.Publish)
                return true;
        }

        if (runPackages &&
            ((request.PublishNuget ?? spec.Packages?.PublishNuget) == true ||
             (request.PublishProjectGitHub ?? spec.Packages?.PublishGitHub) == true))
        {
            return true;
        }

        if (runTools &&
            spec.Winget is { Enabled: true } winget &&
            (request.SubmitWinget ?? (winget.Submit || winget.Submission?.Enabled == true)))
        {
            return true;
        }

        return runTools && spec.Tools is not null &&
               (request.PublishToolGitHub ?? spec.Tools.GitHub.Publish);
    }

    internal static bool ShouldCaptureVirusTotalModuleArtifactProvenance(
        PowerForgeReleaseSpec spec,
        PowerForgeReleaseRequest request,
        bool runModule)
    {
        if (!runModule ||
            request.PlanOnly ||
            request.ValidateOnly ||
            spec.Module is null ||
            spec.VirusTotal is not { Enabled: true } options ||
            !(options.ArtifactKinds ?? Array.Empty<VirusTotalArtifactKind>())
                .Contains(VirusTotalArtifactKind.PowerShellModule))
        {
            return false;
        }

        var packagePublishingRequested =
            !request.ModuleOnly &&
            ((request.PublishNuget ?? spec.Packages?.PublishNuget) == true ||
             (request.PublishProjectGitHub ?? spec.Packages?.PublishGitHub) == true);
        return request.CaptureModuleArtifactProvenance ||
               ResolveModuleRunMode(spec.Module, request, packagePublishingRequested) == ConfigurationGateMode.Publish;
    }

    private static void ValidateVirusTotalConfiguration(PowerForgeVirusTotalOptions? options)
    {
        if (options is not null)
            VirusTotalReleaseArtifactSelector.ValidateConfiguration(options);
    }

    private static string? ResolveVirusTotalApiKeyForExecution(
        PowerForgeVirusTotalOptions? options,
        string configDirectory,
        bool planOrValidation)
    {
        if (options is not { Enabled: true } || planOrValidation)
            return null;

        var apiKey = ResolveSecret(
            options.ApiKey,
            options.ApiKeyFilePath,
            options.ApiKeyEnvName,
            configDirectory);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "VirusTotal Monitor publishing is enabled, but the configured API key source did not produce a value.");
        }

        if (apiKey!.IndexOf('\r') >= 0 || apiKey.IndexOf('\n') >= 0)
        {
            throw new InvalidOperationException(
                "VirusTotal Monitor API key must be a single-line secret.");
        }

        return apiKey;
    }

    private bool TryPublishVirusTotalMonitor(
        PowerForgeReleaseSpec spec,
        PowerForgeReleaseRequest request,
        string configDirectory,
        PowerForgeReleaseResult result,
        string? sharedReleaseVersion,
        string? apiKey)
    {
        var options = spec.VirusTotal;
        if (options is not { Enabled: true })
            return true;

        request.CancellationToken.ThrowIfCancellationRequested();
        string? project = null;
        string? version = null;
        var receiptWritable = false;
        var resumeReceiptSafeToReplace = false;
        var receiptWriteFailed = false;
        FileStream? receiptLock = null;
        VirusTotalMonitorArtifactReceipt[] resumeReceipts = Array.Empty<VirusTotalMonitorArtifactReceipt>();

        string PersistReceipt(VirusTotalMonitorPublishResult publishResult)
        {
            try
            {
                return WriteVirusTotalReceipt(
                    options,
                    configDirectory,
                    project!,
                    version!,
                    publishResult);
            }
            catch
            {
                receiptWriteFailed = true;
                throw;
            }
        }

        try
        {
            version = ResolveVirusTotalReleaseVersion(result, sharedReleaseVersion) ?? "mixed";

            project = ResolveVirusTotalProjectName(spec, options, configDirectory);
            receiptLock = AcquireVirusTotalReceiptLock(options, configDirectory);
            EnsureVirusTotalReceiptWritable(options, configDirectory, project);
            receiptWritable = true;
            resumeReceipts = LoadVirusTotalResumeReceipts(
                options,
                configDirectory,
                project,
                version!);
            resumeReceiptSafeToReplace = true;
            var artifacts = VirusTotalReleaseArtifactSelector.Select(
                CollectVirusTotalReleaseAssetEntries(result),
                options,
                project,
                string.Equals(version, "mixed", StringComparison.Ordinal) ? null : version);
            if (artifacts.Length == 0)
            {
                result.VirusTotalMonitor = new VirusTotalMonitorPublishResult { Success = true };
                request.Progress?.PhaseCompleted(
                    PowerForgeReleaseProgressPhase.VirusTotal,
                    "Skipped because no configured final release artifacts matched");
                return true;
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException(
                    "VirusTotal Monitor publishing reached execution without its pre-resolved API key.");
            }

            var applicableResumeReceipts = ApplyVirusTotalResumeReceipts(resumeReceipts, artifacts);

            request.Progress?.PhaseStarted(
                PowerForgeReleaseProgressPhase.VirusTotal,
                artifacts.Length,
                $"Registering {artifacts.Length} final release artifact(s) with VirusTotal Monitor");

            var publishResult = _publishVirusTotalMonitor(
                new VirusTotalMonitorPublishRequest
                {
                    ApiKey = apiKey!,
                    Artifacts = artifacts,
                    ResumeReceipts = applicableResumeReceipts,
                    VerifySha256 = options.VerifySha256,
                    VerificationTimeout = TimeSpan.FromSeconds(options.VerificationTimeoutSeconds),
                    PollingInterval = TimeSpan.FromSeconds(options.PollingIntervalSeconds),
                    RequestTimeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds),
                    CheckpointAsync = (checkpoint, _) =>
                    {
                        result.VirusTotalMonitorReceiptPath = PersistReceipt(checkpoint);
                        return Task.CompletedTask;
                    }
                },
                request.CancellationToken);

            result.VirusTotalMonitor = publishResult;
            publishResult.ErrorMessage = VirusTotalMonitorPublisher.RedactApiKey(
                publishResult.ErrorMessage,
                apiKey);
            if (!receiptWriteFailed)
                result.VirusTotalMonitorReceiptPath = PersistReceipt(publishResult);
            if (!publishResult.Success)
            {
                var failureMessage = publishResult.ErrorMessage ?? "VirusTotal Monitor did not accept every selected artifact.";
                request.Progress?.PhaseFailed(PowerForgeReleaseProgressPhase.VirusTotal, failureMessage);
                _logger.Warn(
                    $"VirusTotal Monitor publishing did not complete: {failureMessage} " +
                    "The primary release remains successful because Monitor registration is an asynchronous post-release integration.");
                return true;
            }
            request.Progress?.PhaseCompleted(
                PowerForgeReleaseProgressPhase.VirusTotal,
                options.VerifySha256
                    ? $"Registered and hash-verified {publishResult.Artifacts.Length} artifact(s); Monitor analysis remains asynchronous"
                    : $"Registered {publishResult.Artifacts.Length} artifact(s); Monitor analysis remains asynchronous");
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var errorMessage = VirusTotalMonitorPublisher.RedactApiKey(exception.Message, apiKey);
            request.Progress?.PhaseFailed(PowerForgeReleaseProgressPhase.VirusTotal, errorMessage);
            var retainedArtifacts = result.VirusTotalMonitor?.Artifacts is { Length: > 0 } acceptedArtifacts
                ? acceptedArtifacts
                : resumeReceipts;
            result.VirusTotalMonitor = new VirusTotalMonitorPublishResult
            {
                Success = false,
                ErrorMessage = errorMessage,
                Artifacts = retainedArtifacts
            };
            if (receiptWritable &&
                resumeReceiptSafeToReplace &&
                !receiptWriteFailed &&
                !string.IsNullOrWhiteSpace(project) &&
                !string.IsNullOrWhiteSpace(version))
            {
                try
                {
                    result.VirusTotalMonitorReceiptPath = PersistReceipt(result.VirusTotalMonitor);
                }
                catch (Exception receiptException) when (receiptException is not OperationCanceledException)
                {
                    _logger.Warn(
                        $"VirusTotal Monitor failure receipt could not be persisted: {receiptException.Message}");
                }
            }
            _logger.Warn(
                $"VirusTotal Monitor publishing did not run: {errorMessage} " +
                "The primary release remains successful because Monitor registration is an asynchronous post-release integration.");
            return true;
        }
        finally
        {
            receiptLock?.Dispose();
        }
    }

    internal static string? ResolveVirusTotalReleaseVersion(
        PowerForgeReleaseResult result,
        string? sharedReleaseVersion)
        => NormalizeReleaseVersion(sharedReleaseVersion) ??
           ResolveModuleReleaseVersion(result.ModulePlan) ??
           ResolveUniqueAssetVersion(result.ReleaseAssetEntries ?? Array.Empty<PowerForgeReleaseAssetEntry>());

    private static string ResolveVirusTotalProjectName(
        PowerForgeReleaseSpec spec,
        PowerForgeVirusTotalOptions options,
        string configDirectory)
    {
        var value = FirstNonEmpty(
            options.ProjectName,
            spec.GitHub?.Repository,
            spec.Packages?.GitHubRepositoryName,
            spec.Module?.ModuleName,
            spec.Tools?.GitHub.Repository,
            Path.GetFileName(configDirectory));
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("VirusTotal Monitor publishing could not resolve a project name.");

        var normalized = value!.Trim().Replace('\\', '/').TrimEnd('/');
        var slash = normalized.LastIndexOf('/');
        if (slash >= 0)
            normalized = normalized.Substring(slash + 1);
        if (normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring(0, normalized.Length - 4);
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException("VirusTotal Monitor publishing resolved an empty project name.");
        VirusTotalReleaseArtifactSelector.ValidatePathSegment(normalized, nameof(options.ProjectName));
        return normalized;
    }

}
