using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
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
        var savePlan = await PlanSaveAsync(CreateSaveRequest(request, resolved.ScriptRoot), cancellationToken).ConfigureAwait(false);

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
            ExistingVersion = savePlan.ExistingVersion,
            RequestedVersion = savePlan.RequestedVersion,
            MinimumVersion = savePlan.MinimumVersion,
            MaximumVersion = savePlan.MaximumVersion,
            VersionPolicy = savePlan.VersionPolicy,
            ExpectedPackageSha256 = savePlan.ExpectedPackageSha256
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
        var saveResult = await SaveAsync(CreateSaveRequest(request, resolved.ScriptRoot), cancellationToken).ConfigureAwait(false);

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
        var existingPackageVersion = TryReadInstalledPackageVersion(scriptPath);
        var existingComparableVersion = existingPackageVersion ?? existingVersion;
        var satisfiedExistingVersion = TryResolveSatisfiedExistingVersion(request, scriptPath, existingComparableVersion);
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
        var action = ResolvePlanAction(File.Exists(scriptPath), scriptPath, existingComparableVersion, versionInfo.Version, request.Force, RequiresPackageVerificationBeforeSkip(request));
        if (RequiresPlanPackageMetadata(action, versionInfo))
        {
            versionInfo = await EnrichVersionInfoWithPackageMetadataAsync(
                request.Repository,
                versionInfo,
                request.Credential,
                cancellationToken).ConfigureAwait(false);
        }
        if (RequiresPlanPackageVerification(action, request))
            await VerifySelectedPackageForPlanAsync(request, versionInfo.Version, cancellationToken).ConfigureAwait(false);

        var wouldWriteFiles = action is ManagedScriptSavePlanAction.Save or ManagedScriptSavePlanAction.Reinstall or ManagedScriptSavePlanAction.VerifyExisting;
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
        var existingPackageVersion = TryReadInstalledPackageVersion(scriptPath);
        var existingComparableVersion = existingPackageVersion ?? existingVersion;
        var satisfiedExistingVersion = TryResolveSatisfiedExistingVersion(request, scriptPath, existingComparableVersion);
        if (satisfiedExistingVersion is not null)
        {
            stopwatch.Stop();
            return CreateSkippedResult(request, satisfiedExistingVersion, destinationPath, scriptPath, existingVersion, stopwatch.Elapsed, TimeSpan.Zero);
        }

        var versionResolutionStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var versionInfo = await ResolveSelectedVersionInfoAsync(request, cancellationToken).ConfigureAwait(false);
        versionResolutionStopwatch.Stop();
        ThrowIfSelectedVersionInvalid(versionInfo);

        var existingSelectedVersion = !string.IsNullOrWhiteSpace(existingComparableVersion) &&
                                      ManagedModuleVersionComparer.Instance.Compare(existingComparableVersion!, versionInfo.Version) == 0 &&
                                      !request.Force &&
                                      ExistingScriptCanSkip(scriptPath);
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

            var extractionStopwatch = System.Diagnostics.Stopwatch.StartNew();
            stageRoot = Path.Combine(cacheDirectory, "script-stage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stageRoot);
            var stagedScriptPath = Path.Combine(stageRoot, Path.GetFileName(scriptPath));
            ExtractScriptPayload(download.PackagePath, request.Name, stagedScriptPath);
            var scriptInfo = _scriptFileInfoService.Read(stagedScriptPath);
            ThrowIfScriptMetadataIncomplete(scriptInfo, download.PackagePath);
            ThrowIfScriptVersionInvalid(scriptInfo);
            extractionStopwatch.Stop();

            if (existingSelectedVersion &&
                string.Equals(ComputeFileSha256(scriptPath), ComputeFileSha256(stagedScriptPath), StringComparison.OrdinalIgnoreCase))
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
                    extractionStopwatch.Elapsed,
                    download);
            }

            Directory.CreateDirectory(destinationPath);
            ReplaceScriptFile(stagedScriptPath, scriptPath, request.Force || existingSelectedVersion);
            WriteInstallRecord(scriptPath, request, versionInfo.Version);
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

    private static bool RequiresPlanPackageVerification(ManagedScriptSavePlanAction action, ManagedScriptSaveRequest request)
        => (action is ManagedScriptSavePlanAction.Save or ManagedScriptSavePlanAction.Reinstall or ManagedScriptSavePlanAction.VerifyExisting) &&
           RequiresPackageVerificationBeforeSkip(request);

    private async Task VerifySelectedPackageForPlanAsync(
        ManagedScriptSaveRequest request,
        string version,
        CancellationToken cancellationToken)
    {
        var cacheDirectory = Path.Combine(Path.GetTempPath(), "PowerForge", "managed-script-plan-cache", Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(cacheDirectory);
            var download = await _repositoryClient.DownloadPackageAsync(
                request.Repository,
                request.Name,
                version,
                cacheDirectory,
                request.Credential,
                cancellationToken).ConfigureAwait(false);
            ManagedModulePackageIntegrity.VerifyDownload(download, request.ExpectedPackageSha256);
            ManagedModuleTrustEvaluator.ThrowIfPackageRejected(request.Repository, download.Metadata, request.TrustPolicy);
        }
        finally
        {
            DeleteDirectoryQuietly(cacheDirectory);
        }
    }

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
            if (string.IsNullOrWhiteSpace(version))
                return null;

            var safeVersion = ManagedModulePackageIdentity.RequireSafeVersion(version, nameof(version));
            ValidateScriptVersion(safeVersion, nameof(version));
            return safeVersion;
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

    private static string? TryReadInstalledPackageVersion(string scriptPath)
    {
        var installRecordPath = ResolveInstallRecordPath(scriptPath);
        if (!File.Exists(installRecordPath))
            return null;

        try
        {
            var record = JsonSerializer.Deserialize<ManagedScriptInstallRecord>(File.ReadAllText(installRecordPath));
            var version = record?.Version;
            var scriptSha256 = record?.ScriptSha256;
            if (string.IsNullOrWhiteSpace(version) ||
                string.IsNullOrWhiteSpace(scriptSha256) ||
                !string.Equals(scriptSha256, ComputeFileSha256(scriptPath), StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var safeVersion = ManagedModulePackageIdentity.RequireSafeVersion(version!, nameof(ManagedScriptInstallRecord.Version));
            ValidateScriptVersion(safeVersion, nameof(ManagedScriptInstallRecord.Version));
            return safeVersion;
        }
        catch
        {
            return null;
        }
    }

    private static void WriteInstallRecord(string scriptPath, ManagedScriptSaveRequest request, string packageVersion)
    {
        var record = new ManagedScriptInstallRecord
        {
            Name = request.Name.Trim(),
            Version = packageVersion,
            ScriptSha256 = ComputeFileSha256(scriptPath),
            RepositoryName = request.Repository.Name,
            RepositorySource = request.Repository.Source
        };
        var json = JsonSerializer.Serialize(record);
        File.WriteAllText(ResolveInstallRecordPath(scriptPath), json);
    }

    private static string ResolveInstallRecordPath(string scriptPath)
        => scriptPath + ".powerforge.json";

    private static string ComputeFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
    }

    private ManagedScriptSaveResult CreateSkippedResult(
        ManagedScriptSaveRequest request,
        string version,
        string destinationPath,
        string scriptPath,
        string? existingVersion,
        TimeSpan elapsed,
        TimeSpan versionResolutionElapsed,
        TimeSpan? downloadElapsed = null,
        TimeSpan? extractionElapsed = null,
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
            ExtractionElapsed = extractionElapsed ?? TimeSpan.Zero,
            Download = download,
            ScriptInfo = string.IsNullOrWhiteSpace(existingVersion)
                ? null
                : _scriptFileInfoService.Read(scriptPath)
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

    private sealed class ManagedScriptInstallRecord
    {
        public string? Name { get; set; }
        public string? Version { get; set; }
        public string? ScriptSha256 { get; set; }
        public string? RepositoryName { get; set; }
        public string? RepositorySource { get; set; }
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
            _ => ManagedScriptInstallPlanAction.Install
        };

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
        var folderName = shellEdition == ManagedModuleShellEdition.Desktop ? "WindowsPowerShell" : "PowerShell";
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

    private static ManagedModuleShellEdition ResolveShellEdition(ManagedModuleShellEdition shellEdition)
    {
        if (shellEdition != ManagedModuleShellEdition.Auto)
            return shellEdition;

        return Environment.Version.Major <= 4
            ? ManagedModuleShellEdition.Desktop
            : ManagedModuleShellEdition.Core;
    }

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
            home = Environment.GetEnvironmentVariable("HOME") ?? ".";

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

    private static void ThrowIfLicenseAcceptanceRequired(ManagedModulePackageMetadata? metadata, bool acceptLicense)
    {
        if (metadata?.RequireLicenseAcceptance != true || acceptLicense)
            return;

        throw new InvalidOperationException(
            $"Package '{metadata.Id}' {metadata.Version} requires license acceptance. Use AcceptLicense to continue.");
    }

    private static void ThrowIfScriptVersionInvalid(ManagedScriptFileInfo info)
    {
        _ = ManagedModulePackageIdentity.RequireSafeVersion(info.Version, nameof(info.Version));
        ValidateScriptVersion(info.Version, nameof(info.Version));
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
