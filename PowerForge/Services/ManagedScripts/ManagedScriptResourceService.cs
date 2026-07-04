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

        var versionInfo = await ResolveSelectedVersionInfoAsync(request, cancellationToken).ConfigureAwait(false);
        var destinationPath = ResolveDestinationPath(request.DestinationPath);
        var scriptPath = ResolveScriptPath(destinationPath, request.Name);
        var existingVersion = TryReadExistingVersion(scriptPath);
        var action = ResolvePlanAction(existingVersion, versionInfo.Version, request.Force);

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
            WouldWriteFiles = action != ManagedScriptSavePlanAction.SkipExisting,
            RequestedVersion = request.Version,
            MinimumVersion = request.MinimumVersion,
            MaximumVersion = request.MaximumVersion,
            VersionPolicy = request.VersionPolicy,
            ExpectedPackageSha256 = ManagedModulePackageIntegrity.NormalizeSha256(request.ExpectedPackageSha256)
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
        var versionResolutionStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var versionInfo = await ResolveSelectedVersionInfoAsync(request, cancellationToken).ConfigureAwait(false);
        versionResolutionStopwatch.Stop();

        var destinationPath = ResolveDestinationPath(request.DestinationPath);
        var scriptPath = ResolveScriptPath(destinationPath, request.Name);
        var existingVersion = TryReadExistingVersion(scriptPath);
        var existingSelectedVersion = !string.IsNullOrWhiteSpace(existingVersion) &&
                                      ManagedModuleVersionComparer.Instance.Compare(existingVersion!, versionInfo.Version) == 0 &&
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
            ThrowIfScriptVersionDisagrees(scriptInfo, versionInfo.Version, download.PackagePath);
            extractionStopwatch.Stop();

            Directory.CreateDirectory(destinationPath);
            File.Copy(stagedScriptPath, scriptPath, overwrite: request.Force);
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

        if (candidates.Length == 1)
            return candidates[0].Entry;

        if (candidates.Length == 0)
            throw new InvalidOperationException($"Package '{packagePath}' does not contain a PowerShell script resource.");

        throw new InvalidOperationException($"Package '{packagePath}' contains multiple script files and no '{expectedName}' payload.");
    }

    private static ManagedScriptSavePlanAction ResolvePlanAction(string? existingVersion, string selectedVersion, bool force)
    {
        if (force && !string.IsNullOrWhiteSpace(existingVersion))
            return ManagedScriptSavePlanAction.Reinstall;
        if (!string.IsNullOrWhiteSpace(existingVersion) &&
            ManagedModuleVersionComparer.Instance.Compare(existingVersion!, selectedVersion) == 0)
            return ManagedScriptSavePlanAction.SkipExisting;

        return ManagedScriptSavePlanAction.Save;
    }

    private string? TryReadExistingVersion(string scriptPath)
    {
        if (!File.Exists(scriptPath))
            return null;

        try
        {
            return _scriptFileInfoService.Read(scriptPath).Version;
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

    private static void ThrowIfScriptVersionDisagrees(ManagedScriptFileInfo info, string packageVersion, string packagePath)
    {
        if (ManagedModuleVersionComparer.Instance.Compare(info.Version, packageVersion) == 0)
            return;

        throw new InvalidOperationException(
            $"Package '{packagePath}' version '{packageVersion}' does not match script metadata version '{info.Version}'.");
    }

    private static ManagedModuleVersionRange ResolveVersionRange(string? versionPolicy, string? minimumVersion, string? maximumVersion)
        => string.IsNullOrWhiteSpace(versionPolicy)
            ? ManagedModuleVersionRange.FromBounds(minimumVersion, maximumVersion)
            : ManagedModuleVersionRange.Parse(versionPolicy);

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
