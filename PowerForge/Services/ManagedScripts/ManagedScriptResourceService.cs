using System.IO.Compression;

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
            ThrowIfScriptVersionInvalid(scriptInfo);
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
}
