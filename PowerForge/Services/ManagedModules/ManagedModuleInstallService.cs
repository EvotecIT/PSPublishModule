namespace PowerForge;

/// <summary>
/// Installs PowerShell module packages using managed repository and archive operations.
/// </summary>
public sealed class ManagedModuleInstallService
{
    private readonly ILogger _logger;
    private readonly ManagedModuleRepositoryClient _repositoryClient;
    private readonly ManagedModuleArchiveExtractor _extractor;
    private readonly ManagedModuleReceiptStore _receiptStore;

    /// <summary>
    /// Creates a managed module install service.
    /// </summary>
    /// <param name="logger">Logger used for diagnostics.</param>
    /// <param name="repositoryClient">Optional repository client.</param>
    public ManagedModuleInstallService(ILogger logger, ManagedModuleRepositoryClient? repositoryClient = null)
    {
        _logger = logger ?? new NullLogger();
        _repositoryClient = repositoryClient ?? new ManagedModuleRepositoryClient(_logger);
        _extractor = new ManagedModuleArchiveExtractor();
        _receiptStore = new ManagedModuleReceiptStore();
    }

    /// <summary>
    /// Installs a module package into a versioned PowerShell module directory.
    /// </summary>
    /// <param name="request">Install request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Install result.</returns>
    public async Task<ManagedModuleInstallResult> InstallAsync(
        ManagedModuleInstallRequest request,
        CancellationToken cancellationToken = default)
        => await InstallAsync(request, new ManagedModuleInstallContext(), cancellationToken).ConfigureAwait(false);

    private async Task<ManagedModuleInstallResult> InstallAsync(
        ManagedModuleInstallRequest request,
        ManagedModuleInstallContext context,
        CancellationToken cancellationToken)
    {
        Validate(request);

        var version = await ResolveSelectedVersionAsync(request, cancellationToken).ConfigureAwait(false);
        var moduleRoot = ManagedModuleInstallRootResolver.Resolve(request.Scope, request.ShellEdition, request.ModuleRoot);
        var modulePath = Path.Combine(moduleRoot, request.Name.Trim(), version);
        using var dependencyScope = context.Enter(request.Name);

        var ownsCache = string.IsNullOrWhiteSpace(request.PackageCacheDirectory);
        var cacheDirectory = ownsCache
            ? Path.Combine(Path.GetTempPath(), "PowerForge.ManagedModules", Guid.NewGuid().ToString("N"))
            : Path.GetFullPath(request.PackageCacheDirectory!.Trim().Trim('"'));
        var stageRoot = Path.Combine(Path.GetTempPath(), "PowerForge.ManagedModules.Stage", Guid.NewGuid().ToString("N"));
        var stageModulePath = Path.Combine(stageRoot, request.Name.Trim(), version);

        try
        {
            using var installLock = ManagedModuleInstallLock.Acquire(moduleRoot, request.Name, cancellationToken);
            if (Directory.Exists(modulePath) && !request.Force)
            {
                _logger.Verbose($"Managed module install skipped existing version: {modulePath}");
                return new ManagedModuleInstallResult
                {
                    Name = request.Name.Trim(),
                    Version = version,
                    Status = ManagedModuleInstallStatus.AlreadyInstalled,
                    RepositoryName = request.Repository.Name,
                    ModuleRoot = moduleRoot,
                    ModulePath = modulePath
                };
            }

            var download = await _repositoryClient.DownloadPackageAsync(
                request.Repository,
                request.Name,
                version,
                cacheDirectory,
                request.Credential,
                cancellationToken).ConfigureAwait(false);
            var extraction = _extractor.ExtractPackage(download.PackagePath, stageModulePath);
            var finalParent = Path.GetDirectoryName(modulePath) ?? moduleRoot;
            Directory.CreateDirectory(finalParent);
            var dependencyResults = request.SkipDependencyCheck
                ? Array.Empty<ManagedModuleInstallResult>()
                : await InstallDependenciesAsync(request, download.Metadata, cacheDirectory, context, cancellationToken).ConfigureAwait(false);

            PromoteStagedModule(stageModulePath, modulePath);
            CleanupEmptyStage(stageRoot);

            var result = new ManagedModuleInstallResult
            {
                Name = request.Name.Trim(),
                Version = version,
                Status = ManagedModuleInstallStatus.Installed,
                RepositoryName = request.Repository.Name,
                ModuleRoot = moduleRoot,
                ModulePath = modulePath,
                Download = download,
                FileCount = extraction.FileCount,
                ExtractedBytes = extraction.BytesWritten,
                DependencyResults = dependencyResults
            };
            _receiptStore.WriteReceipt(request.Repository, result);
            return result;
        }
        finally
        {
            if (Directory.Exists(stageRoot))
                Directory.Delete(stageRoot, recursive: true);
            if (ownsCache && Directory.Exists(cacheDirectory))
                Directory.Delete(cacheDirectory, recursive: true);
        }
    }

    private async Task<string> ResolveSelectedVersionAsync(ManagedModuleInstallRequest request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.Version))
            return request.Version!.Trim();

        var range = ResolveVersionRange(request.VersionPolicy, request.MinimumVersion, request.MaximumVersion);
        var versions = await _repositoryClient.GetVersionsAsync(
            request.Repository,
            request.Name,
            request.IncludePrerelease || range.AllowsPrerelease,
            request.Credential,
            cancellationToken).ConfigureAwait(false);

        var latest = versions
            .Where(version => range.IsSatisfiedBy(version.Version))
            .LastOrDefault();
        if (latest is null)
            throw new InvalidOperationException($"No versions of '{request.Name}' satisfying range '{range}' were found in repository '{request.Repository.Name}'.");

        return latest.Version;
    }

    private async Task<IReadOnlyList<ManagedModuleInstallResult>> InstallDependenciesAsync(
        ManagedModuleInstallRequest request,
        ManagedModulePackageMetadata? metadata,
        string cacheDirectory,
        ManagedModuleInstallContext context,
        CancellationToken cancellationToken)
    {
        var dependencies = SelectDependencies(metadata).ToArray();
        if (dependencies.Length == 0)
            return Array.Empty<ManagedModuleInstallResult>();

        var results = new List<ManagedModuleInstallResult>();
        foreach (var dependency in dependencies)
        {
            var range = ManagedModuleVersionRange.Parse(dependency.VersionRange);
            var dependencyVersion = await ResolveDependencyVersionAsync(
                request,
                dependency.Id,
                range,
                cancellationToken).ConfigureAwait(false);

            var result = await InstallAsync(
                new ManagedModuleInstallRequest
                {
                    Repository = request.Repository,
                    Name = dependency.Id,
                    Version = dependencyVersion,
                    VersionPolicy = null,
                    IncludePrerelease = request.IncludePrerelease || range.AllowsPrerelease,
                    Scope = request.Scope,
                    ShellEdition = request.ShellEdition,
                    ModuleRoot = request.ModuleRoot,
                    PackageCacheDirectory = cacheDirectory,
                    Credential = request.Credential,
                    Force = false,
                    SkipDependencyCheck = false
                },
                context,
                cancellationToken).ConfigureAwait(false);

            results.Add(result);
        }

        return results;
    }

    private async Task<string> ResolveDependencyVersionAsync(
        ManagedModuleInstallRequest request,
        string dependencyName,
        ManagedModuleVersionRange range,
        CancellationToken cancellationToken)
    {
        if (range.ExactVersion is not null)
            return range.ExactVersion;

        var includePrerelease = request.IncludePrerelease || range.AllowsPrerelease;
        var versions = await _repositoryClient.GetVersionsAsync(
            request.Repository,
            dependencyName,
            includePrerelease,
            request.Credential,
            cancellationToken).ConfigureAwait(false);

        var selected = versions
            .Where(version => range.IsSatisfiedBy(version.Version))
            .LastOrDefault();
        if (selected is null)
            throw new InvalidOperationException($"No dependency version of '{dependencyName}' satisfies range '{range}' in repository '{request.Repository.Name}'.");

        return selected.Version;
    }

    private static IEnumerable<ManagedModuleDependencyInfo> SelectDependencies(ManagedModulePackageMetadata? metadata)
    {
        if (metadata?.Dependencies is null || metadata.Dependencies.Count == 0)
            return Array.Empty<ManagedModuleDependencyInfo>();

        return metadata.Dependencies
            .Where(static dependency => !string.IsNullOrWhiteSpace(dependency.Id))
            .GroupBy(static dependency => dependency.Id, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group
                .OrderBy(static dependency => string.IsNullOrWhiteSpace(dependency.TargetFramework) ? 0 : 1)
                .ThenBy(static dependency => dependency.VersionRange, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(static dependency => dependency.Id, StringComparer.OrdinalIgnoreCase);
    }

    private static void Validate(ManagedModuleInstallRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (request.Repository is null)
            throw new ArgumentException("Repository is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Module name is required.", nameof(request));
        if (!string.IsNullOrWhiteSpace(request.Version) &&
            (!string.IsNullOrWhiteSpace(request.MinimumVersion) ||
             !string.IsNullOrWhiteSpace(request.MaximumVersion) ||
             !string.IsNullOrWhiteSpace(request.VersionPolicy)))
            throw new ArgumentException("Version cannot be combined with MinimumVersion, MaximumVersion, or VersionPolicy.", nameof(request));
        if (!string.IsNullOrWhiteSpace(request.VersionPolicy) &&
            (!string.IsNullOrWhiteSpace(request.MinimumVersion) || !string.IsNullOrWhiteSpace(request.MaximumVersion)))
            throw new ArgumentException("VersionPolicy cannot be combined with MinimumVersion or MaximumVersion.", nameof(request));
        if (request.Scope == ManagedModuleInstallScope.Custom && string.IsNullOrWhiteSpace(request.ModuleRoot))
            throw new ArgumentException("ModuleRoot is required when Scope is Custom.", nameof(request));
    }

    private static ManagedModuleVersionRange ResolveVersionRange(string? versionPolicy, string? minimumVersion, string? maximumVersion)
        => string.IsNullOrWhiteSpace(versionPolicy)
            ? ManagedModuleVersionRange.FromBounds(minimumVersion, maximumVersion)
            : ManagedModuleVersionRange.Parse(versionPolicy);

    private static void CleanupEmptyStage(string stageRoot)
    {
        if (!Directory.Exists(stageRoot))
            return;

        foreach (var directory in Directory.EnumerateDirectories(stageRoot, "*", SearchOption.AllDirectories)
                     .OrderByDescending(static path => path.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }
    }

    private static void PromoteStagedModule(string stageModulePath, string modulePath)
    {
        var backupPath = default(string);
        try
        {
            if (Directory.Exists(modulePath))
            {
                backupPath = Path.Combine(Path.GetTempPath(), "PowerForge.ManagedModules.Backup", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                Directory.Move(modulePath, backupPath);
            }

            Directory.Move(stageModulePath, modulePath);
            if (backupPath is not null && Directory.Exists(backupPath))
                Directory.Delete(backupPath, recursive: true);
        }
        catch
        {
            RestoreBackup(modulePath, backupPath);
            throw;
        }
    }

    private static void RestoreBackup(string modulePath, string? backupPath)
    {
        if (backupPath is null || !Directory.Exists(backupPath))
            return;

        if (Directory.Exists(modulePath))
            Directory.Delete(modulePath, recursive: true);

        Directory.Move(backupPath, modulePath);
    }
}
