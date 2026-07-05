using System.IO.Compression;
using System.Runtime.InteropServices;

namespace PowerForge;

/// <summary>
/// Saves PowerShell script resources from managed NuGet/local repositories.
/// </summary>
public sealed class ManagedScriptResourceService
{
    private readonly ILogger _logger;
    private readonly ManagedModuleRepositoryClient _repositoryClient;
    private readonly ManagedScriptFileInfoService _scriptFileInfoService;

    /// <summary>
    /// Creates a script resource service.
    /// </summary>
    public ManagedScriptResourceService(
        ILogger logger,
        ManagedModuleRepositoryClient? repositoryClient = null,
        ManagedScriptFileInfoService? scriptFileInfoService = null)
    {
        _logger = logger ?? new NullLogger();
        _repositoryClient = repositoryClient ?? new ManagedModuleRepositoryClient(_logger);
        _scriptFileInfoService = scriptFileInfoService ?? new ManagedScriptFileInfoService();
    }

    /// <summary>
    /// Creates a non-mutating plan for installing a script resource.
    /// </summary>
    public async Task<ManagedScriptInstallPlan> PlanInstallAsync(
        ManagedScriptInstallRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateInstall(request);
        var resolved = ResolveScriptInstallRoot(request);
        var saveRequest = CreateSaveRequest(request, resolved.ScriptRoot);
        ManagedModuleTrustEvaluator.ThrowIfRepositoryRejected(saveRequest.Repository, saveRequest.TrustPolicy);
        var satisfiedPlan = TryCreateSatisfiedExistingInstallPlan(request, resolved, saveRequest);
        if (satisfiedPlan is not null)
            return satisfiedPlan;

        var savePlan = await PlanSaveAsync(saveRequest, cancellationToken).ConfigureAwait(false);

        return new ManagedScriptInstallPlan
        {
            Name = savePlan.Name,
            Version = savePlan.Version,
            Action = MapInstallAction(savePlan.Action),
            RepositoryName = savePlan.RepositoryName,
            RepositorySource = savePlan.RepositorySource,
            Scope = resolved.Scope,
            ShellEdition = resolved.ShellEdition,
            ScriptRoot = savePlan.DestinationPath,
            ScriptPath = savePlan.ScriptPath,
            WouldWriteFiles = savePlan.WouldWriteFiles,
            WouldVerifyPackage = savePlan.WouldVerifyPackage,
            ExistingVersion = savePlan.ExistingVersion,
            RequestedVersion = savePlan.RequestedVersion,
            MinimumVersion = savePlan.MinimumVersion,
            MaximumVersion = savePlan.MaximumVersion,
            VersionPolicy = savePlan.VersionPolicy,
            ExpectedPackageSha256 = savePlan.ExpectedPackageSha256,
            LicenseAcceptanceRequired = savePlan.LicenseAcceptanceRequired,
            BlockReason = savePlan.BlockReason
        };
    }

    /// <summary>
    /// Installs a script resource package into the requested script scope.
    /// </summary>
    public async Task<ManagedScriptInstallResult> InstallAsync(
        ManagedScriptInstallRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateInstall(request);
        var resolved = ResolveScriptInstallRoot(request);
        var saveRequest = CreateSaveRequest(request, resolved.ScriptRoot);
        ManagedModuleTrustEvaluator.ThrowIfRepositoryRejected(saveRequest.Repository, saveRequest.TrustPolicy);
        var satisfiedResult = TryCreateSatisfiedExistingInstallResult(request, resolved, saveRequest);
        if (satisfiedResult is not null)
            return satisfiedResult;

        var saveResult = await SaveAsync(saveRequest, cancellationToken).ConfigureAwait(false);

        return new ManagedScriptInstallResult
        {
            Name = saveResult.Name,
            Version = saveResult.Version,
            Status = saveResult.Status == ManagedScriptSaveStatus.SkippedExisting
                ? ManagedScriptInstallStatus.SkippedExisting
                : ManagedScriptInstallStatus.Installed,
            RepositoryName = saveResult.RepositoryName,
            RepositorySource = saveResult.RepositorySource,
            Scope = resolved.Scope,
            ShellEdition = resolved.ShellEdition,
            ScriptRoot = saveResult.DestinationPath,
            ScriptPath = saveResult.ScriptPath,
            RequestedVersion = saveResult.RequestedVersion,
            MinimumVersion = saveResult.MinimumVersion,
            MaximumVersion = saveResult.MaximumVersion,
            VersionPolicy = saveResult.VersionPolicy,
            ExpectedPackageSha256 = saveResult.ExpectedPackageSha256,
            Elapsed = saveResult.Elapsed,
            VersionResolutionElapsed = saveResult.VersionResolutionElapsed,
            DownloadElapsed = saveResult.DownloadElapsed,
            ExtractionElapsed = saveResult.ExtractionElapsed,
            Download = saveResult.Download,
            ScriptInfo = saveResult.ScriptInfo
        };
    }

    /// <summary>
    /// Creates a non-mutating plan for uninstalling a script resource.
    /// </summary>
    public ManagedScriptUninstallPlan PlanUninstall(ManagedScriptUninstallRequest request)
    {
        ValidateUninstall(request);
        var resolved = ResolveScriptInstallRoot(request);
        var scriptPath = ResolveInstalledScriptPath(resolved.ScriptRoot, request.Name);
        var scriptInfo = TryReadExistingInfo(scriptPath, request.Force, allowUnreadableMetadata: !string.IsNullOrWhiteSpace(request.Version));
        var action = ResolveUninstallAction(scriptPath, scriptInfo?.Version, request.Version);

        return new ManagedScriptUninstallPlan
        {
            Name = request.Name.Trim(),
            Action = action,
            Scope = resolved.Scope,
            ShellEdition = resolved.ShellEdition,
            ScriptRoot = resolved.ScriptRoot,
            ScriptPath = scriptPath,
            WouldRemoveFile = action == ManagedScriptUninstallPlanAction.Remove,
            ExistingVersion = scriptInfo?.Version,
            RequestedVersion = request.Version,
            SkipReason = ResolveUninstallSkipReason(action, scriptInfo?.Version, request.Version)
        };
    }

    /// <summary>
    /// Uninstalls a script resource from the requested script scope.
    /// </summary>
    public ManagedScriptUninstallResult Uninstall(ManagedScriptUninstallRequest request)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var plan = PlanUninstall(request);
        ManagedScriptFileInfo? scriptInfo = null;
        var finalAction = plan.Action;
        var finalExistingVersion = plan.ExistingVersion;
        var finalSkipReason = plan.SkipReason;
        if (File.Exists(plan.ScriptPath))
        {
            try
            {
                scriptInfo = _scriptFileInfoService.Read(plan.ScriptPath);
                finalExistingVersion = scriptInfo.Version;
            }
            catch when (request.Force || !string.IsNullOrWhiteSpace(request.Version))
            {
                scriptInfo = null;
                finalExistingVersion = null;
            }
        }
        else if (plan.Action == ManagedScriptUninstallPlanAction.Remove)
        {
            finalAction = ManagedScriptUninstallPlanAction.SkipMissing;
            finalExistingVersion = null;
            finalSkipReason = ResolveUninstallSkipReason(finalAction, finalExistingVersion, request.Version);
        }

        if (finalAction == ManagedScriptUninstallPlanAction.Remove)
        {
            finalAction = ResolveFinalUninstallAction(plan.Action, finalExistingVersion, request.Version);
            finalSkipReason = ResolveUninstallSkipReason(finalAction, finalExistingVersion, request.Version);
        }

        if (finalAction == ManagedScriptUninstallPlanAction.Remove)
            File.Delete(plan.ScriptPath);

        stopwatch.Stop();
        return new ManagedScriptUninstallResult
        {
            Name = plan.Name,
            Status = MapUninstallStatus(finalAction),
            Scope = plan.Scope,
            ShellEdition = plan.ShellEdition,
            ScriptRoot = plan.ScriptRoot,
            ScriptPath = plan.ScriptPath,
            ExistingVersion = finalExistingVersion,
            RequestedVersion = plan.RequestedVersion,
            SkipReason = finalSkipReason,
            Elapsed = stopwatch.Elapsed,
            ScriptInfo = scriptInfo
        };
    }

    /// <summary>
    /// Creates a non-mutating plan for saving a script resource.
    /// </summary>
    public async Task<ManagedScriptSavePlan> PlanSaveAsync(
        ManagedScriptSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        ManagedModuleTrustEvaluator.ThrowIfRepositoryRejected(request.Repository, request.TrustPolicy);

        var destinationPath = ResolveDestinationPath(request.DestinationPath);
        var scriptPath = ResolveScriptPath(destinationPath, request.Name);
        var existingVersion = TryReadExistingVersion(scriptPath, allowInvalidMetadata: request.Force);
        var satisfiedExistingVersion = TryResolveSatisfiedExistingVersion(request, scriptPath, existingVersion);
        if (satisfiedExistingVersion is not null)
        {
            return new ManagedScriptSavePlan
            {
                Name = request.Name.Trim(),
                Version = satisfiedExistingVersion,
                Action = ManagedScriptSavePlanAction.SkipExisting,
                RepositoryName = request.Repository.Name,
                RepositorySource = request.Repository.Source,
                DestinationPath = destinationPath,
                ScriptPath = scriptPath,
                ExistingVersion = existingVersion,
                WouldWriteFiles = false,
                WouldVerifyPackage = false,
                RequestedVersion = request.Version,
                MinimumVersion = request.MinimumVersion,
                MaximumVersion = request.MaximumVersion,
                VersionPolicy = request.VersionPolicy,
                ExpectedPackageSha256 = ManagedModulePackageIntegrity.NormalizeSha256(request.ExpectedPackageSha256)
            };
        }

        var versionInfo = await ResolveSelectedVersionInfoAsync(request, cancellationToken).ConfigureAwait(false);
        ThrowIfSelectedVersionInvalid(versionInfo);
        var action = ResolvePlanAction(File.Exists(scriptPath), scriptPath, existingVersion, versionInfo.Version, request.Force, RequiresPackageVerificationBeforeSkip(request));
        if (RequiresPlanPackageMetadata(action, versionInfo))
        {
            versionInfo = await EnrichVersionInfoWithPackageMetadataAsync(
                request.Repository,
                versionInfo,
                request.Credential,
                cancellationToken).ConfigureAwait(false);
        }

        var wouldWriteFiles = action is ManagedScriptSavePlanAction.Save or ManagedScriptSavePlanAction.Reinstall;
        var wouldVerifyPackage = action is ManagedScriptSavePlanAction.VerifyExisting;

        return new ManagedScriptSavePlan
        {
            Name = request.Name.Trim(),
            Version = versionInfo.Version,
            Action = action,
            RepositoryName = request.Repository.Name,
            RepositorySource = request.Repository.Source,
            DestinationPath = destinationPath,
            ScriptPath = scriptPath,
            ExistingVersion = existingVersion,
            WouldWriteFiles = wouldWriteFiles,
            WouldVerifyPackage = wouldVerifyPackage,
            RequestedVersion = request.Version,
            MinimumVersion = request.MinimumVersion,
            MaximumVersion = request.MaximumVersion,
            VersionPolicy = request.VersionPolicy,
            ExpectedPackageSha256 = ManagedModulePackageIntegrity.NormalizeSha256(request.ExpectedPackageSha256),
            LicenseAcceptanceRequired = versionInfo.RequireLicenseAcceptance,
            BlockReason = ResolvePlanBlockReason(action, scriptPath, existingVersion, versionInfo.Version)
        };
    }

    /// <summary>
    /// Saves a script resource package to the requested destination directory.
    /// </summary>
    public async Task<ManagedScriptSaveResult> SaveAsync(
        ManagedScriptSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        ManagedModuleTrustEvaluator.ThrowIfRepositoryRejected(request.Repository, request.TrustPolicy);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var destinationPath = ResolveDestinationPath(request.DestinationPath);
        var scriptPath = ResolveScriptPath(destinationPath, request.Name);
        var existingVersion = TryReadExistingVersion(scriptPath, allowInvalidMetadata: request.Force);
        var satisfiedExistingVersion = TryResolveSatisfiedExistingVersion(request, scriptPath, existingVersion);
        if (satisfiedExistingVersion is not null)
        {
            stopwatch.Stop();
            return CreateSkippedResult(request, satisfiedExistingVersion, destinationPath, scriptPath, existingVersion, stopwatch.Elapsed, TimeSpan.Zero);
        }

        var versionResolutionStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var versionInfo = await ResolveSelectedVersionInfoAsync(request, cancellationToken).ConfigureAwait(false);
        versionResolutionStopwatch.Stop();
        ThrowIfSelectedVersionInvalid(versionInfo);

        var existingSelectedVersion = !string.IsNullOrWhiteSpace(existingVersion) &&
                                      ManagedModuleVersionComparer.Instance.Compare(existingVersion!, versionInfo.Version) == 0 &&
                                      ExistingScriptCanSkip(scriptPath) &&
                                      !request.Force;
        if (existingSelectedVersion && !RequiresPackageVerificationBeforeSkip(request))
        {
            stopwatch.Stop();
            return CreateSkippedResult(request, versionInfo.Version, destinationPath, scriptPath, existingVersion, stopwatch.Elapsed, versionResolutionStopwatch.Elapsed);
        }

        if (!existingSelectedVersion && File.Exists(scriptPath) && !request.Force)
            throw new InvalidOperationException($"Script '{scriptPath}' already exists with version '{existingVersion ?? "unknown"}'. Use Force to replace it.");

        var ownsCache = string.IsNullOrWhiteSpace(request.PackageCacheDirectory);
        var cacheDirectory = ownsCache
            ? Path.Combine(Path.GetTempPath(), "PowerForge", "managed-script-cache", Guid.NewGuid().ToString("N"))
            : Path.GetFullPath(request.PackageCacheDirectory!.Trim().Trim('"'));

        string? stageRoot = null;
        try
        {
            Directory.CreateDirectory(cacheDirectory);

            var downloadStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var download = await _repositoryClient.DownloadPackageAsync(
                request.Repository,
                request.Name,
                versionInfo.Version,
                cacheDirectory,
                request.Credential,
                cancellationToken).ConfigureAwait(false);
            downloadStopwatch.Stop();

            ManagedModulePackageIntegrity.VerifyDownload(download, request.ExpectedPackageSha256);
            ManagedModuleTrustEvaluator.ThrowIfPackageRejected(request.Repository, download.Metadata, request.TrustPolicy);
            ThrowIfLicenseAcceptanceRequired(download.Metadata, request.AcceptLicense);

            if (existingSelectedVersion)
            {
                stopwatch.Stop();
                return CreateSkippedResult(
                    request,
                    versionInfo.Version,
                    destinationPath,
                    scriptPath,
                    existingVersion,
                    stopwatch.Elapsed,
                    versionResolutionStopwatch.Elapsed,
                    downloadStopwatch.Elapsed,
                    download);
            }

            var extractionStopwatch = System.Diagnostics.Stopwatch.StartNew();
            stageRoot = Path.Combine(cacheDirectory, "script-stage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stageRoot);
            var stagedScriptPath = Path.Combine(stageRoot, Path.GetFileName(scriptPath));
            ExtractScriptPayload(download.PackagePath, request.Name, stagedScriptPath);
            var scriptInfo = _scriptFileInfoService.Read(stagedScriptPath);
            ThrowIfScriptMetadataIncomplete(scriptInfo, download.PackagePath);
            ThrowIfScriptVersionDisagrees(scriptInfo, versionInfo.Version, download.PackagePath);
            extractionStopwatch.Stop();

            Directory.CreateDirectory(destinationPath);
            ReplaceScriptFile(stagedScriptPath, scriptPath, request.Force);
            scriptInfo.Path = scriptPath;
            stopwatch.Stop();

            return new ManagedScriptSaveResult
            {
                Name = request.Name.Trim(),
                Version = versionInfo.Version,
                Status = ManagedScriptSaveStatus.Saved,
                RepositoryName = request.Repository.Name,
                RepositorySource = request.Repository.Source,
                DestinationPath = destinationPath,
                ScriptPath = scriptPath,
                RequestedVersion = request.Version,
                MinimumVersion = request.MinimumVersion,
                MaximumVersion = request.MaximumVersion,
                VersionPolicy = request.VersionPolicy,
                ExpectedPackageSha256 = ManagedModulePackageIntegrity.NormalizeSha256(request.ExpectedPackageSha256),
                Elapsed = stopwatch.Elapsed,
                VersionResolutionElapsed = versionResolutionStopwatch.Elapsed,
                DownloadElapsed = downloadStopwatch.Elapsed,
                ExtractionElapsed = extractionStopwatch.Elapsed,
                Download = download,
                ScriptInfo = scriptInfo
            };
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(stageRoot))
                DeleteDirectoryQuietly(stageRoot!);
            if (ownsCache)
                DeleteDirectoryQuietly(cacheDirectory);
        }
    }

    private async Task<ManagedModuleVersionInfo> ResolveSelectedVersionInfoAsync(
        ManagedScriptSaveRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.Version))
        {
            var exactVersion = request.Version!.Trim();
            var versions = await _repositoryClient.GetVersionsAsync(
                request.Repository,
                request.Name,
                request.IncludePrerelease || ManagedModuleVersionComparer.IsPrerelease(exactVersion),
                request.Credential,
                cancellationToken).ConfigureAwait(false);

            var exactMatch = versions.FirstOrDefault(version =>
                ManagedModuleVersionComparer.Instance.Compare(version.Version, exactVersion) == 0);
            if (exactMatch is not null)
                return exactMatch;

            throw new InvalidOperationException(
                $"Version '{exactVersion}' of script '{request.Name}' was not found in repository '{request.Repository.Name}'.");
        }

        var range = ResolveVersionRange(request.VersionPolicy, request.MinimumVersion, request.MaximumVersion);
        if (range.IsUnbounded)
        {
            var latestVersion = await _repositoryClient.GetLatestVersionAsync(
                request.Repository,
                request.Name,
                request.IncludePrerelease,
                request.Credential,
                cancellationToken).ConfigureAwait(false);
            if (latestVersion is null)
                throw new InvalidOperationException($"No versions of script '{request.Name}' were found in repository '{request.Repository.Name}'.");

            return latestVersion;
        }

        var versionsInRange = await _repositoryClient.GetVersionsAsync(
            request.Repository,
            request.Name,
            request.IncludePrerelease || range.AllowsPrerelease,
            request.Credential,
            cancellationToken).ConfigureAwait(false);
        var latest = versionsInRange
            .Where(static version => version.Listed)
            .Where(version => range.IsSatisfiedBy(version.Version))
            .LastOrDefault();
        if (latest is null)
            throw new InvalidOperationException($"No versions of script '{request.Name}' satisfying range '{range}' were found in repository '{request.Repository.Name}'.");

        return latest;
    }

    private async Task<ManagedModuleVersionInfo> EnrichVersionInfoWithPackageMetadataAsync(
        ManagedModuleRepository repository,
        ManagedModuleVersionInfo versionInfo,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        if (versionInfo.RequireLicenseAcceptance || !string.IsNullOrWhiteSpace(versionInfo.License))
            return versionInfo;

        var metadata = await _repositoryClient.GetPackageMetadataAsync(
            repository,
            versionInfo.Name,
            versionInfo.Version,
            credential,
            cancellationToken).ConfigureAwait(false);

        return metadata is null
            ? versionInfo
            : CopyVersionInfoWithPackageMetadata(versionInfo, metadata);
    }

    private static ManagedModuleVersionInfo CopyVersionInfoWithPackageMetadata(
        ManagedModuleVersionInfo versionInfo,
        ManagedModulePackageMetadata metadata)
        => new()
        {
            Name = versionInfo.Name,
            Version = versionInfo.Version,
            RepositoryName = versionInfo.RepositoryName,
            RepositorySource = versionInfo.RepositorySource,
            PackageSource = versionInfo.PackageSource,
            IsPrerelease = versionInfo.IsPrerelease,
            Listed = versionInfo.Listed,
            License = metadata.License,
            RequireLicenseAcceptance = metadata.RequireLicenseAcceptance,
            Dependencies = metadata.Dependencies
        };

    private static void ExtractScriptPayload(string packagePath, string packageId, string destinationPath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var scriptEntry = SelectScriptEntry(archive, packageId, packagePath);
        using var source = scriptEntry.Open();
        using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        source.CopyTo(destination);
    }

    private static ZipArchiveEntry SelectScriptEntry(ZipArchive archive, string packageId, string packagePath)
    {
        var expectedName = packageId.Trim() + ".ps1";
        var candidates = archive.Entries
            .Where(static entry => !string.IsNullOrWhiteSpace(entry.Name))
            .Select(entry => new { Entry = entry, Path = NormalizePackagePath(entry.FullName) })
            .Where(item => IsSafePackagePath(item.Path) && item.Path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var preferred = candidates.FirstOrDefault(item => item.Path.Equals(expectedName, StringComparison.OrdinalIgnoreCase)) ??
                        candidates.FirstOrDefault(item => Path.GetFileName(item.Path).Equals(expectedName, StringComparison.OrdinalIgnoreCase));
        if (preferred is not null)
            return preferred.Entry;

        if (candidates.Length == 0)
            throw new InvalidOperationException($"Package '{packagePath}' does not contain a PowerShell script resource.");

        throw new InvalidOperationException($"Package '{packagePath}' does not contain expected script payload '{expectedName}'.");
    }

    private ManagedScriptSavePlanAction ResolvePlanAction(
        bool scriptExists,
        string scriptPath,
        string? existingVersion,
        string selectedVersion,
        bool force,
        bool requiresPackageVerification)
    {
        if (scriptExists)
        {
            if (force)
                return ManagedScriptSavePlanAction.Reinstall;
            if (!string.IsNullOrWhiteSpace(existingVersion) &&
                ExistingScriptCanSkip(scriptPath) &&
                ManagedModuleVersionComparer.Instance.Compare(existingVersion!, selectedVersion) == 0)
            {
                return requiresPackageVerification
                    ? ManagedScriptSavePlanAction.VerifyExisting
                    : ManagedScriptSavePlanAction.SkipExisting;
            }

            return ManagedScriptSavePlanAction.BlockedExisting;
        }

        return ManagedScriptSavePlanAction.Save;
    }

    private static string? ResolvePlanBlockReason(
        ManagedScriptSavePlanAction action,
        string scriptPath,
        string? existingVersion,
        string selectedVersion)
        => action == ManagedScriptSavePlanAction.BlockedExisting
            ? $"Script '{scriptPath}' already exists with version '{existingVersion ?? "unknown"}'. Use Force to replace it with version '{selectedVersion}'."
            : null;

    private static bool RequiresPlanPackageMetadata(ManagedScriptSavePlanAction action, ManagedModuleVersionInfo versionInfo)
        => (action is ManagedScriptSavePlanAction.Save or ManagedScriptSavePlanAction.Reinstall or ManagedScriptSavePlanAction.VerifyExisting) &&
           !versionInfo.RequireLicenseAcceptance &&
           string.IsNullOrWhiteSpace(versionInfo.License);

    private string? TryResolveSatisfiedExistingVersion(ManagedScriptSaveRequest request, string scriptPath, string? existingVersion)
    {
        if (request.Force ||
            RequiresPackageVerificationBeforeSkip(request) ||
            !HasConstrainedVersionRequest(request) ||
            string.IsNullOrWhiteSpace(existingVersion))
        {
            return null;
        }

        return ExistingScriptCanSkip(scriptPath) && IsExistingVersionSatisfied(request, existingVersion!)
            ? existingVersion
            : null;
    }

    private static bool HasConstrainedVersionRequest(ManagedScriptSaveRequest request)
        => !string.IsNullOrWhiteSpace(request.Version);

    private static bool IsExistingVersionSatisfied(ManagedScriptSaveRequest request, string existingVersion)
    {
        if (!string.IsNullOrWhiteSpace(request.Version))
            return ManagedModuleVersionComparer.Instance.Compare(existingVersion, request.Version!.Trim()) == 0;

        var range = ResolveVersionRange(request.VersionPolicy, request.MinimumVersion, request.MaximumVersion);
        if (range.IsUnbounded)
            return false;
        if (ManagedModuleVersionComparer.IsPrerelease(existingVersion) &&
            !request.IncludePrerelease &&
            !range.AllowsPrerelease)
        {
            return false;
        }

        return range.IsSatisfiedBy(existingVersion);
    }

    private string? TryReadExistingVersion(string scriptPath, bool allowInvalidMetadata = false)
    {
        if (!File.Exists(scriptPath))
            return null;

        try
        {
            var version = _scriptFileInfoService.Read(scriptPath).Version;
            return string.IsNullOrWhiteSpace(version)
                ? null
                : ManagedModulePackageIdentity.RequireSafeVersion(version, nameof(version));
        }
        catch (ArgumentException)
        {
            if (allowInvalidMetadata)
                return null;

            throw;
        }
        catch (InvalidOperationException exception)
        {
            if (exception.Message.IndexOf("not a valid version", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (allowInvalidMetadata)
                    return null;

                throw;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private ManagedScriptFileInfo? TryReadExistingInfo(string scriptPath, bool force, bool allowUnreadableMetadata = false)
    {
        if (!File.Exists(scriptPath))
            return null;

        try
        {
            return _scriptFileInfoService.Read(scriptPath);
        }
        catch when (force || allowUnreadableMetadata)
        {
            return null;
        }
    }

    private static ManagedScriptSaveResult CreateSkippedResult(
        ManagedScriptSaveRequest request,
        string version,
        string destinationPath,
        string scriptPath,
        string? existingVersion,
        TimeSpan elapsed,
        TimeSpan versionResolutionElapsed,
        TimeSpan? downloadElapsed = null,
        ManagedModuleDownloadResult? download = null)
        => new()
        {
            Name = request.Name.Trim(),
            Version = version,
            Status = ManagedScriptSaveStatus.SkippedExisting,
            RepositoryName = request.Repository.Name,
            RepositorySource = request.Repository.Source,
            DestinationPath = destinationPath,
            ScriptPath = scriptPath,
            RequestedVersion = request.Version,
            MinimumVersion = request.MinimumVersion,
            MaximumVersion = request.MaximumVersion,
            VersionPolicy = request.VersionPolicy,
            ExpectedPackageSha256 = ManagedModulePackageIntegrity.NormalizeSha256(request.ExpectedPackageSha256),
            Elapsed = elapsed,
            VersionResolutionElapsed = versionResolutionElapsed,
            DownloadElapsed = downloadElapsed ?? TimeSpan.Zero,
            Download = download,
            ScriptInfo = string.IsNullOrWhiteSpace(existingVersion)
                ? null
                : new ManagedScriptFileInfo { Path = scriptPath, Version = existingVersion! }
        };

    private static bool RequiresPackageVerificationBeforeSkip(ManagedScriptSaveRequest request)
        => !string.IsNullOrWhiteSpace(request.ExpectedPackageSha256) ||
           ManagedModuleTrustEvaluator.HasAllowedAuthorPolicy(request.TrustPolicy);

    private bool ExistingScriptCanSkip(string scriptPath)
    {
        var info = _scriptFileInfoService.Read(scriptPath);
        ThrowIfScriptMetadataIncomplete(info, scriptPath);
        return true;
    }

    private static ManagedScriptSaveRequest CreateSaveRequest(ManagedScriptInstallRequest request, string destinationPath)
        => new()
        {
            Repository = request.Repository,
            Name = request.Name,
            DestinationPath = destinationPath,
            Version = request.Version,
            MinimumVersion = request.MinimumVersion,
            MaximumVersion = request.MaximumVersion,
            VersionPolicy = request.VersionPolicy,
            IncludePrerelease = request.IncludePrerelease,
            PackageCacheDirectory = request.PackageCacheDirectory,
            ExpectedPackageSha256 = request.ExpectedPackageSha256,
            TrustPolicy = request.TrustPolicy,
            Credential = request.Credential,
            Force = request.Force,
            AcceptLicense = request.AcceptLicense
        };

    private static ManagedScriptInstallPlanAction MapInstallAction(ManagedScriptSavePlanAction action)
        => action switch
        {
            ManagedScriptSavePlanAction.SkipExisting => ManagedScriptInstallPlanAction.SkipExisting,
            ManagedScriptSavePlanAction.Reinstall => ManagedScriptInstallPlanAction.Reinstall,
            ManagedScriptSavePlanAction.VerifyExisting => ManagedScriptInstallPlanAction.VerifyExisting,
            ManagedScriptSavePlanAction.BlockedExisting => ManagedScriptInstallPlanAction.BlockedExisting,
            _ => ManagedScriptInstallPlanAction.Install
        };

    private ManagedScriptInstallPlan? TryCreateSatisfiedExistingInstallPlan(
        ManagedScriptInstallRequest request,
        ResolvedScriptInstallRoot resolved,
        ManagedScriptSaveRequest saveRequest)
    {
        var existing = TryReadSatisfiedExistingInstall(request, resolved.ScriptRoot, saveRequest, out var scriptPath);
        if (existing is null)
            return null;

        return new ManagedScriptInstallPlan
        {
            Name = request.Name.Trim(),
            Version = existing.Version,
            Action = ManagedScriptInstallPlanAction.SkipExisting,
            RepositoryName = request.Repository.Name,
            RepositorySource = request.Repository.Source,
            Scope = resolved.Scope,
            ShellEdition = resolved.ShellEdition,
            ScriptRoot = resolved.ScriptRoot,
            ScriptPath = scriptPath,
            WouldWriteFiles = false,
            WouldVerifyPackage = false,
            ExistingVersion = existing.Version,
            RequestedVersion = request.Version,
            MinimumVersion = request.MinimumVersion,
            MaximumVersion = request.MaximumVersion,
            VersionPolicy = request.VersionPolicy,
            ExpectedPackageSha256 = ManagedModulePackageIntegrity.NormalizeSha256(request.ExpectedPackageSha256)
        };
    }

    private ManagedScriptInstallResult? TryCreateSatisfiedExistingInstallResult(
        ManagedScriptInstallRequest request,
        ResolvedScriptInstallRoot resolved,
        ManagedScriptSaveRequest saveRequest)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var existing = TryReadSatisfiedExistingInstall(request, resolved.ScriptRoot, saveRequest, out var scriptPath);
        if (existing is null)
            return null;

        stopwatch.Stop();
        return new ManagedScriptInstallResult
        {
            Name = request.Name.Trim(),
            Version = existing.Version,
            Status = ManagedScriptInstallStatus.SkippedExisting,
            RepositoryName = request.Repository.Name,
            RepositorySource = request.Repository.Source,
            Scope = resolved.Scope,
            ShellEdition = resolved.ShellEdition,
            ScriptRoot = resolved.ScriptRoot,
            ScriptPath = scriptPath,
            RequestedVersion = request.Version,
            MinimumVersion = request.MinimumVersion,
            MaximumVersion = request.MaximumVersion,
            VersionPolicy = request.VersionPolicy,
            ExpectedPackageSha256 = ManagedModulePackageIntegrity.NormalizeSha256(request.ExpectedPackageSha256),
            Elapsed = stopwatch.Elapsed,
            ScriptInfo = existing
        };
    }

    private ManagedScriptFileInfo? TryReadSatisfiedExistingInstall(
        ManagedScriptInstallRequest request,
        string scriptRoot,
        ManagedScriptSaveRequest saveRequest,
        out string scriptPath)
    {
        scriptPath = ResolveScriptPath(scriptRoot, request.Name);
        if (request.Force ||
            RequiresPackageVerificationBeforeSkip(saveRequest) ||
            !HasConstrainedVersionRequest(request))
        {
            return null;
        }

        var existingVersion = TryReadExistingVersion(scriptPath);
        if (string.IsNullOrWhiteSpace(existingVersion) || !IsExistingVersionSatisfied(request, existingVersion!))
            return null;

        return ReadExistingScriptInfoForSkip(scriptPath);
    }

    private ManagedScriptFileInfo ReadExistingScriptInfoForSkip(string scriptPath)
    {
        var info = _scriptFileInfoService.Read(scriptPath);
        ThrowIfScriptMetadataIncomplete(info, scriptPath);
        return info;
    }

    private static bool HasConstrainedVersionRequest(ManagedScriptInstallRequest request)
        => !string.IsNullOrWhiteSpace(request.Version) ||
           !string.IsNullOrWhiteSpace(request.MinimumVersion) ||
           !string.IsNullOrWhiteSpace(request.MaximumVersion) ||
           !string.IsNullOrWhiteSpace(request.VersionPolicy);

    private static bool IsExistingVersionSatisfied(ManagedScriptInstallRequest request, string existingVersion)
    {
        if (!string.IsNullOrWhiteSpace(request.Version))
            return ManagedModuleVersionComparer.Instance.Compare(existingVersion, request.Version!.Trim()) == 0;

        var range = ResolveVersionRange(request.VersionPolicy, request.MinimumVersion, request.MaximumVersion);
        if (ManagedModuleVersionComparer.IsPrerelease(existingVersion) &&
            !request.IncludePrerelease &&
            !range.AllowsPrerelease)
        {
            return false;
        }

        return !range.IsUnbounded && range.IsSatisfiedBy(existingVersion);
    }

    private static ResolvedScriptInstallRoot ResolveScriptInstallRoot(ManagedScriptInstallRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ScriptRoot))
        {
            return new ResolvedScriptInstallRoot(
                Path.GetFullPath(request.ScriptRoot!.Trim().Trim('"')),
                ManagedScriptInstallScope.Custom,
                ResolveShellEdition(request.ShellEdition));
        }

        var shellEdition = ResolveShellEdition(request.ShellEdition);
        var folderName = ResolveScriptShellFolderName(shellEdition, RuntimeInformation.IsOSPlatform(OSPlatform.Windows));
        var scope = request.Scope;
        var scriptRoot = scope switch
        {
            ManagedScriptInstallScope.CurrentUser => ResolveCurrentUserScriptRoot(folderName),
            ManagedScriptInstallScope.AllUsers => ResolveAllUsersScriptRoot(folderName),
            ManagedScriptInstallScope.Custom => throw new ArgumentException("ScriptRoot is required when Scope is Custom.", nameof(request)),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Scope, "Unsupported script install scope.")
        };

        return new ResolvedScriptInstallRoot(scriptRoot, scope, shellEdition);
    }

    private static ResolvedScriptInstallRoot ResolveScriptInstallRoot(ManagedScriptUninstallRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ScriptRoot))
        {
            return new ResolvedScriptInstallRoot(
                Path.GetFullPath(request.ScriptRoot!.Trim().Trim('"')),
                ManagedScriptInstallScope.Custom,
                ResolveShellEdition(request.ShellEdition));
        }

        var shellEdition = ResolveShellEdition(request.ShellEdition);
        var folderName = ResolveScriptShellFolderName(shellEdition, RuntimeInformation.IsOSPlatform(OSPlatform.Windows));
        var scope = request.Scope;
        var scriptRoot = scope switch
        {
            ManagedScriptInstallScope.CurrentUser => ResolveCurrentUserScriptRoot(folderName),
            ManagedScriptInstallScope.AllUsers => ResolveAllUsersScriptRoot(folderName),
            ManagedScriptInstallScope.Custom => throw new ArgumentException("ScriptRoot is required when Scope is Custom.", nameof(request)),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Scope, "Unsupported script install scope.")
        };

        return new ResolvedScriptInstallRoot(scriptRoot, scope, shellEdition);
    }

    private static ManagedScriptUninstallPlanAction ResolveUninstallAction(string scriptPath, string? existingVersion, string? requestedVersion)
    {
        if (!File.Exists(scriptPath))
            return ManagedScriptUninstallPlanAction.SkipMissing;

        if (!string.IsNullOrWhiteSpace(requestedVersion) &&
            (string.IsNullOrWhiteSpace(existingVersion) ||
             !IsValidScriptVersion(existingVersion) ||
             !ScriptVersionsMatch(existingVersion!, requestedVersion!)))
        {
            return ManagedScriptUninstallPlanAction.SkipVersionMismatch;
        }

        return ManagedScriptUninstallPlanAction.Remove;
    }

    internal static ManagedScriptUninstallPlanAction ResolveFinalUninstallAction(
        ManagedScriptUninstallPlanAction plannedAction,
        string? currentVersion,
        string? requestedVersion)
    {
        if (plannedAction != ManagedScriptUninstallPlanAction.Remove ||
            string.IsNullOrWhiteSpace(requestedVersion))
        {
            return plannedAction;
        }

        if (string.IsNullOrWhiteSpace(currentVersion) ||
            !IsValidScriptVersion(currentVersion) ||
            !ScriptVersionsMatch(currentVersion!, requestedVersion!))
        {
            return ManagedScriptUninstallPlanAction.SkipVersionMismatch;
        }

        return ManagedScriptUninstallPlanAction.Remove;
    }

    private static string? ResolveUninstallSkipReason(
        ManagedScriptUninstallPlanAction action,
        string? existingVersion,
        string? requestedVersion)
        => action switch
        {
            ManagedScriptUninstallPlanAction.SkipMissing => "Script is not installed in the selected script root.",
            ManagedScriptUninstallPlanAction.SkipVersionMismatch => string.IsNullOrWhiteSpace(existingVersion)
                ? $"Installed script metadata could not be read, so requested version '{requestedVersion}' was not removed."
                : $"Installed script version '{existingVersion}' does not match requested version '{requestedVersion}'.",
            _ => null
        };

    private static bool ScriptVersionsMatch(string existingVersion, string requestedVersion)
    {
        var existing = existingVersion.Trim();
        var requested = requestedVersion.Trim();
        if (!IsValidScriptVersion(existing) || !IsValidScriptVersion(requested))
            return false;

        return ManagedModuleVersionComparer.Instance.Compare(existing, requested) == 0;
    }

    private static ManagedScriptUninstallStatus MapUninstallStatus(ManagedScriptUninstallPlanAction action)
        => action switch
        {
            ManagedScriptUninstallPlanAction.Remove => ManagedScriptUninstallStatus.Removed,
            ManagedScriptUninstallPlanAction.SkipVersionMismatch => ManagedScriptUninstallStatus.SkippedVersionMismatch,
            _ => ManagedScriptUninstallStatus.SkippedMissing
        };

    private static ManagedModuleShellEdition ResolveShellEdition(ManagedModuleShellEdition shellEdition)
    {
        if (shellEdition != ManagedModuleShellEdition.Auto)
            return shellEdition;

        return Environment.Version.Major <= 4
            ? ManagedModuleShellEdition.Desktop
            : ManagedModuleShellEdition.Core;
    }

    internal static string ResolveScriptShellFolderName(ManagedModuleShellEdition shellEdition, bool isWindows)
        => isWindows && shellEdition == ManagedModuleShellEdition.Desktop
            ? "WindowsPowerShell"
            : "PowerShell";

    private static string ResolveCurrentUserScriptRoot(string folderName)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(documents))
                documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents");

            return Path.Combine(documents, folderName, "Scripts");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
            home = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrWhiteSpace(home))
            throw new InvalidOperationException("Unable to resolve the current user home directory for script installation.");

        return Path.Combine(home, ".local", "share", folderName.ToLowerInvariant(), "Scripts");
    }

    private static string ResolveAllUsersScriptRoot(string folderName)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (string.IsNullOrWhiteSpace(programFiles))
                programFiles = Environment.GetEnvironmentVariable("ProgramFiles") ?? @"C:\Program Files";

            return Path.Combine(programFiles, folderName, "Scripts");
        }

        return Path.Combine(Path.DirectorySeparatorChar.ToString(), "usr", "local", "share", folderName.ToLowerInvariant(), "Scripts");
    }

    private static string ResolveDestinationPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Destination path is required.", nameof(path));

        return Path.GetFullPath(path.Trim().Trim('"'));
    }

    private static string ResolveScriptPath(string destinationPath, string name)
        => Path.Combine(destinationPath, ManagedModulePackageIdentity.RequireSafeId(name.Trim(), nameof(name)) + ".ps1");

    private static string ResolveInstalledScriptPath(string destinationPath, string name)
    {
        var safeName = ManagedModulePackageIdentity.RequireSafeId(name.Trim(), nameof(name));
        var exactPath = Path.Combine(destinationPath, safeName + ".ps1");
        if (File.Exists(exactPath) || !Directory.Exists(destinationPath))
            return exactPath;

        var matches = Directory.EnumerateFiles(destinationPath, "*.ps1", SearchOption.TopDirectoryOnly)
            .Where(path => string.Equals(Path.GetFileNameWithoutExtension(path), safeName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length == 0)
            return exactPath;
        if (matches.Length == 1)
            return matches[0];

        throw new InvalidOperationException(
            $"Multiple scripts in '{destinationPath}' match '{safeName}' case-insensitively. Remove the ambiguity before uninstalling.");
    }

    private static void ThrowIfLicenseAcceptanceRequired(ManagedModulePackageMetadata? metadata, bool acceptLicense)
    {
        if (metadata?.RequireLicenseAcceptance != true || acceptLicense)
            return;

        throw new InvalidOperationException(
            $"Package '{metadata.Id}' {metadata.Version} requires license acceptance. Use AcceptLicense to continue.");
    }

    private static void ThrowIfScriptVersionDisagrees(ManagedScriptFileInfo info, string packageVersion, string packagePath)
    {
        _ = ManagedModulePackageIdentity.RequireSafeVersion(info.Version, nameof(info.Version));
        _ = ManagedModulePackageIdentity.RequireSafeVersion(packageVersion, nameof(packageVersion));
        ValidateScriptVersion(info.Version, nameof(info.Version));
        ValidateScriptVersion(packageVersion, nameof(packageVersion));

        if (ManagedModuleVersionComparer.Instance.Compare(info.Version, packageVersion) == 0)
            return;

        throw new InvalidOperationException(
            $"Package '{packagePath}' version '{packageVersion}' does not match script metadata version '{info.Version}'.");
    }

    private static void ThrowIfScriptMetadataIncomplete(ManagedScriptFileInfo info, string packagePath)
    {
        if (info.Guid != Guid.Empty &&
            !string.IsNullOrWhiteSpace(info.Author) &&
            !string.IsNullOrWhiteSpace(info.Description))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Package '{packagePath}' script metadata is incomplete. PSScriptInfo GUID, AUTHOR, and comment-help DESCRIPTION are required.");
    }

    private static void ThrowIfSelectedVersionInvalid(ManagedModuleVersionInfo versionInfo)
    {
        _ = ManagedModulePackageIdentity.RequireSafeVersion(versionInfo.Version, nameof(versionInfo.Version));
        ValidateScriptVersion(versionInfo.Version, nameof(versionInfo.Version));
    }

    private static ManagedModuleVersionRange ResolveVersionRange(string? versionPolicy, string? minimumVersion, string? maximumVersion)
    {
        var range = string.IsNullOrWhiteSpace(versionPolicy)
            ? ManagedModuleVersionRange.FromBounds(minimumVersion, maximumVersion)
            : ManagedModuleVersionRange.Parse(versionPolicy);
        ValidateVersionRangeVersions(range);
        return range;
    }

    private static void ValidateVersionRangeVersions(ManagedModuleVersionRange range)
    {
        if (!string.IsNullOrWhiteSpace(range.ExactVersion))
        {
            _ = ManagedModulePackageIdentity.RequireSafeVersion(range.ExactVersion!, nameof(range.ExactVersion));
            ValidateScriptVersion(range.ExactVersion, nameof(range.ExactVersion));
        }

        if (!string.IsNullOrWhiteSpace(range.MinimumVersion))
        {
            _ = ManagedModulePackageIdentity.RequireSafeVersion(range.MinimumVersion!, nameof(range.MinimumVersion));
            ValidateScriptVersion(range.MinimumVersion, nameof(range.MinimumVersion));
        }

        if (!string.IsNullOrWhiteSpace(range.MaximumVersion))
        {
            _ = ManagedModulePackageIdentity.RequireSafeVersion(range.MaximumVersion!, nameof(range.MaximumVersion));
            ValidateScriptVersion(range.MaximumVersion, nameof(range.MaximumVersion));
        }
    }

    private static void ReplaceScriptFile(string stagedScriptPath, string scriptPath, bool force)
    {
        var destinationDirectory = Path.GetDirectoryName(scriptPath);
        if (string.IsNullOrWhiteSpace(destinationDirectory))
            throw new InvalidOperationException($"Unable to resolve destination directory for script '{scriptPath}'.");

        var tempPath = Path.Combine(
            destinationDirectory,
            "." + Path.GetFileName(scriptPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        var backupPath = tempPath + ".backup";
        try
        {
            File.Copy(stagedScriptPath, tempPath, overwrite: false);
            if (force && File.Exists(scriptPath))
            {
                File.Replace(tempPath, scriptPath, backupPath, ignoreMetadataErrors: true);
                DeleteFileQuietly(backupPath);
                return;
            }

            File.Move(tempPath, scriptPath);
        }
        finally
        {
            DeleteFileQuietly(tempPath);
            DeleteFileQuietly(backupPath);
        }
    }

    private static bool RequiresExactVersionNormalization(string version)
    {
        var prereleaseIndex = version.IndexOf('-');
        var core = prereleaseIndex >= 0
            ? version.Substring(0, prereleaseIndex)
            : version;
        return core.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries).Length < 3;
    }

    private static string NormalizePackagePath(string path)
        => path.Replace('\\', '/').Trim('/');

    private static bool IsSafePackagePath(string normalizedPath)
    {
        if (string.IsNullOrWhiteSpace(normalizedPath) ||
            normalizedPath.StartsWith("/", StringComparison.Ordinal) ||
            normalizedPath.Contains(":", StringComparison.Ordinal))
        {
            return false;
        }

        var parts = normalizedPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 && parts.All(static part => part != "." && part != "..");
    }

    private static void Validate(ManagedScriptSaveRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (request.Repository is null)
            throw new ArgumentException("Repository is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Script name is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.DestinationPath))
            throw new ArgumentException("Destination path is required.", nameof(request));
        ValidateVersionSelectors(request.Version, request.MinimumVersion, request.MaximumVersion, request.VersionPolicy);

        _ = ManagedModulePackageIdentity.RequireSafeId(request.Name.Trim(), nameof(request.Name));
        _ = ManagedModulePackageIntegrity.NormalizeSha256(request.ExpectedPackageSha256);
    }

    private static void ValidateVersionSelectors(
        string? version,
        string? minimumVersion,
        string? maximumVersion,
        string? versionPolicy)
    {
        if (!string.IsNullOrWhiteSpace(version) &&
            (!string.IsNullOrWhiteSpace(minimumVersion) ||
             !string.IsNullOrWhiteSpace(maximumVersion) ||
             !string.IsNullOrWhiteSpace(versionPolicy)))
        {
            throw new ArgumentException("Version cannot be combined with MinimumVersion, MaximumVersion, or VersionPolicy.");
        }

        if (!string.IsNullOrWhiteSpace(versionPolicy) &&
            (!string.IsNullOrWhiteSpace(minimumVersion) || !string.IsNullOrWhiteSpace(maximumVersion)))
        {
            throw new ArgumentException("VersionPolicy cannot be combined with MinimumVersion or MaximumVersion.");
        }

        if (!string.IsNullOrWhiteSpace(version))
        {
            _ = ManagedModulePackageIdentity.RequireSafeVersion(version!, nameof(version));
            ValidateScriptVersion(version!, nameof(version));
        }

        if (!string.IsNullOrWhiteSpace(minimumVersion))
        {
            _ = ManagedModulePackageIdentity.RequireSafeVersion(minimumVersion!, nameof(minimumVersion));
            ValidateScriptVersion(minimumVersion!, nameof(minimumVersion));
        }

        if (!string.IsNullOrWhiteSpace(maximumVersion))
        {
            _ = ManagedModulePackageIdentity.RequireSafeVersion(maximumVersion!, nameof(maximumVersion));
            ValidateScriptVersion(maximumVersion!, nameof(maximumVersion));
        }

        ValidateVersionPolicy(versionPolicy);
    }

    private static void ValidateVersionPolicy(string? versionPolicy)
    {
        if (string.IsNullOrWhiteSpace(versionPolicy))
            return;

        var trimmed = versionPolicy!.Trim();
        if (trimmed.StartsWith("=", StringComparison.Ordinal))
        {
            ValidateVersionPolicyOperand(trimmed.Substring(1), nameof(versionPolicy));
            return;
        }

        if (trimmed.StartsWith(">", StringComparison.Ordinal) ||
            trimmed.StartsWith("<", StringComparison.Ordinal))
        {
            foreach (var rawToken in trimmed.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var token = rawToken.Trim();
                var operand = token.StartsWith(">=", StringComparison.Ordinal) || token.StartsWith("<=", StringComparison.Ordinal)
                    ? token.Substring(2)
                    : token.StartsWith(">", StringComparison.Ordinal) || token.StartsWith("<", StringComparison.Ordinal)
                        ? token.Substring(1)
                        : null;

                if (string.IsNullOrWhiteSpace(operand))
                    throw new ArgumentException($"VersionPolicy '{versionPolicy}' is not a valid version policy.", nameof(versionPolicy));

                ValidateVersionPolicyOperand(operand, nameof(versionPolicy));
            }

            _ = ResolveVersionRange(versionPolicy, null, null);
            return;
        }

        var range = ResolveVersionRange(versionPolicy, null, null);
        if (range.ExactVersion is not null)
            ValidateVersionPolicyOperand(range.ExactVersion, nameof(versionPolicy));
        if (range.MinimumVersion is not null)
            ValidateVersionPolicyOperand(range.MinimumVersion, nameof(versionPolicy));
        if (range.MaximumVersion is not null)
            ValidateVersionPolicyOperand(range.MaximumVersion, nameof(versionPolicy));
    }

    private static void ValidateVersionPolicyOperand(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Version policy contains an empty version operand.", parameterName);

        var trimmed = value!.Trim();
        _ = ManagedModulePackageIdentity.RequireSafeVersion(trimmed, parameterName);
        ValidateScriptVersion(trimmed, parameterName);
    }

    private static void ValidateScriptVersion(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value!.TrimStart().StartsWith("+", StringComparison.Ordinal) ||
            !ModuleStateVersion.TryParse(value!.Trim(), out _))
        {
            throw new ArgumentException($"Script version '{value}' is not a valid version.", parameterName);
        }
    }

    private static void DeleteFileQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // best effort cleanup
        }
    }

    private static void ValidateInstall(ManagedScriptInstallRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (request.Repository is null)
            throw new ArgumentException("Repository is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Script name is required.", nameof(request));
        ValidateVersionSelectors(request.Version, request.MinimumVersion, request.MaximumVersion, request.VersionPolicy);

        _ = ManagedModulePackageIdentity.RequireSafeId(request.Name.Trim(), nameof(request.Name));
        _ = ManagedModulePackageIntegrity.NormalizeSha256(request.ExpectedPackageSha256);
    }

    private static void ValidateUninstall(ManagedScriptUninstallRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Script name is required.", nameof(request));
        if (!string.IsNullOrWhiteSpace(request.Version) &&
            !IsValidScriptVersion(request.Version))
        {
            throw new ArgumentException($"Invalid script version '{request.Version}'.", nameof(request));
        }

        _ = ManagedModulePackageIdentity.RequireSafeId(request.Name.Trim(), nameof(request.Name));
    }

    private static bool IsValidScriptVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return false;

        var trimmed = version!.Trim();
        if (HasWhitespaceAroundSemVerSeparator(trimmed))
            return false;

        try
        {
            _ = ManagedModulePackageIdentity.RequireSafeVersion(trimmed, nameof(version));
        }
        catch (ArgumentException)
        {
            return false;
        }

        var plusIndex = trimmed.IndexOf('+');
        var versionWithoutBuild = plusIndex >= 0 ? trimmed.Substring(0, plusIndex) : trimmed;
        var build = plusIndex >= 0 ? trimmed.Substring(plusIndex + 1) : null;
        if (plusIndex >= 0 &&
            (string.IsNullOrWhiteSpace(build) || !HasValidSemVerIdentifiers(build!)))
        {
            return false;
        }

        if (!ModuleStateVersion.TryParse(versionWithoutBuild, out _))
            return false;

        var prereleaseIndex = versionWithoutBuild.IndexOf('-');
        if (prereleaseIndex < 0)
            return true;

        var prerelease = versionWithoutBuild.Substring(prereleaseIndex + 1);
        return HasValidSemVerIdentifiers(prerelease);
    }

    private static bool HasWhitespaceAroundSemVerSeparator(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character != '+' && character != '-')
                continue;

            if (index > 0 && char.IsWhiteSpace(value[index - 1]))
                return true;
            if (index + 1 < value.Length && char.IsWhiteSpace(value[index + 1]))
                return true;
        }

        return false;
    }

    private static bool HasValidSemVerIdentifiers(string value)
    {
        var identifiers = value.Split('.');
        return identifiers.Length > 0 &&
               identifiers.All(static identifier =>
                   identifier.Length > 0 &&
                   identifier.All(static character =>
                       (character >= 'A' && character <= 'Z') ||
                       (character >= 'a' && character <= 'z') ||
                       (character >= '0' && character <= '9') ||
                       character == '-'));
    }

    private static void DeleteDirectoryQuietly(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best effort cleanup
        }
    }

    private sealed class ResolvedScriptInstallRoot
    {
        public ResolvedScriptInstallRoot(
            string scriptRoot,
            ManagedScriptInstallScope scope,
            ManagedModuleShellEdition shellEdition)
        {
            ScriptRoot = scriptRoot;
            Scope = scope;
            ShellEdition = shellEdition;
        }

        public string ScriptRoot { get; }

        public ManagedScriptInstallScope Scope { get; }

        public ManagedModuleShellEdition ShellEdition { get; }
    }
}
