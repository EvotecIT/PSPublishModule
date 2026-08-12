using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

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

        if (publishUnifiedGitHub)
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
            var artifacts = VirusTotalReleaseArtifactSelector.Select(
                result.ReleaseAssetEntries ?? Array.Empty<PowerForgeReleaseAssetEntry>(),
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

            EnsureVirusTotalReceiptWritable(options, configDirectory);
            receiptWritable = true;
            ApplyVirusTotalResumeReceipt(options, configDirectory, project, version!, artifacts);
            resumeReceiptSafeToReplace = true;

            request.Progress?.PhaseStarted(
                PowerForgeReleaseProgressPhase.VirusTotal,
                artifacts.Length,
                $"Registering {artifacts.Length} final release artifact(s) with VirusTotal Monitor");

            var publishResult = _publishVirusTotalMonitor(
                new VirusTotalMonitorPublishRequest
                {
                    ApiKey = apiKey!,
                    Artifacts = artifacts,
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
            result.VirusTotalMonitor = new VirusTotalMonitorPublishResult
            {
                Success = false,
                ErrorMessage = errorMessage
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
    }

    internal static string? ResolveVirusTotalReleaseVersion(
        PowerForgeReleaseResult result,
        string? sharedReleaseVersion)
        => NormalizeReleaseVersion(sharedReleaseVersion) ??
           ResolveModuleReleaseVersion(result.ModulePlan) ??
           ResolveUniqueAssetVersion(result.ReleaseAssetEntries ?? Array.Empty<PowerForgeReleaseAssetEntry>());

    private static void ApplyVirusTotalResumeReceipt(
        PowerForgeVirusTotalOptions options,
        string configDirectory,
        string project,
        string version,
        VirusTotalMonitorArtifact[] artifacts)
    {
        var receiptPath = ResolveOutputPath(configDirectory, options.ReceiptPath!);
        if (!File.Exists(receiptPath))
            return;

        var serializerOptions = CreateVirusTotalReceiptSerializerOptions(writeIndented: false);
        var receipt = JsonSerializer.Deserialize<VirusTotalMonitorReceiptDocument>(
            File.ReadAllText(receiptPath),
            serializerOptions)
            ?? throw new InvalidDataException("VirusTotal Monitor resume receipt is empty.");
        if (receipt.SchemaVersion != 1 ||
            !string.Equals(receipt.Provider, "VirusTotal Monitor", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "VirusTotal Monitor resume receipt has an unsupported schema or provider.");
        }
        if (!string.Equals(receipt.Project, project, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(receipt.Version, version, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var completedGroups = receipt.Artifacts
            .Where(static item =>
                !string.IsNullOrWhiteSpace(item.DestinationPath) &&
                !string.IsNullOrWhiteSpace(item.MonitorId))
            .GroupBy(static item => item.DestinationPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var conflictingDestination = completedGroups.FirstOrDefault(static group =>
            group.Select(static item => item.MonitorId).Distinct(StringComparer.Ordinal).Skip(1).Any());
        if (conflictingDestination is not null)
        {
            throw new InvalidDataException(
                $"VirusTotal Monitor receipt maps destination '{conflictingDestination.Key}' to conflicting item ids.");
        }

        var completed = completedGroups.ToDictionary(
            static group => group.Key,
            static group => group.Last().MonitorId,
            StringComparer.OrdinalIgnoreCase);
        foreach (var artifact in artifacts)
        {
            if (completed.TryGetValue(artifact.DestinationPath, out var itemId))
                artifact.ExistingItemId = itemId;
        }
    }

    private static void EnsureVirusTotalReceiptWritable(
        PowerForgeVirusTotalOptions options,
        string configDirectory)
    {
        var receiptPath = ResolveOutputPath(configDirectory, options.ReceiptPath!);
        if (Directory.Exists(receiptPath))
        {
            throw new InvalidOperationException(
                $"VirusTotal receipt path points to an existing directory: '{receiptPath}'.");
        }
        var directory = Path.GetDirectoryName(receiptPath)
            ?? throw new InvalidOperationException("VirusTotal receipt path has no parent directory.");
        Directory.CreateDirectory(directory);
        if (File.Exists(receiptPath))
        {
            using var receipt = new FileStream(
                receiptPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.WriteThrough);
            receipt.Flush(flushToDisk: true);
        }

        var probePath = Path.Combine(directory, $".{Path.GetFileName(receiptPath)}.{Guid.NewGuid():N}.probe");
        try
        {
            using var stream = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.WriteThrough);
            stream.WriteByte(0);
            stream.Flush(flushToDisk: true);
        }
        finally
        {
            try { File.Delete(probePath); } catch { /* best effort */ }
        }
    }

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
        return normalized;
    }

    private static string WriteVirusTotalReceipt(
        PowerForgeVirusTotalOptions options,
        string configDirectory,
        string project,
        string version,
        VirusTotalMonitorPublishResult result)
    {
        var receiptPath = ResolveOutputPath(configDirectory, options.ReceiptPath!);
        var directory = Path.GetDirectoryName(receiptPath)
            ?? throw new InvalidOperationException("VirusTotal receipt path has no parent directory.");
        Directory.CreateDirectory(directory);
        var serializerOptions = CreateVirusTotalReceiptSerializerOptions(writeIndented: true);
        var json = JsonSerializer.Serialize(
            new VirusTotalMonitorReceiptDocument
            {
                Project = project,
                Version = version,
                HashVerificationRequested = options.VerifySha256,
                Success = result.Success,
                ErrorMessage = result.ErrorMessage,
                Artifacts = result.Artifacts
            },
            serializerOptions);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(receiptPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (File.Exists(receiptPath))
                File.Replace(temporaryPath, receiptPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else
                File.Move(temporaryPath, receiptPath);
        }
        finally
        {
            try { File.Delete(temporaryPath); } catch { /* best effort */ }
        }

        return receiptPath;
    }

    private static JsonSerializerOptions CreateVirusTotalReceiptSerializerOptions(bool writeIndented)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = writeIndented
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
