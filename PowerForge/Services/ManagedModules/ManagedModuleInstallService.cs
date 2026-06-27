namespace PowerForge;

/// <summary>
/// Installs PowerShell module packages using managed repository and archive operations.
/// </summary>
public sealed class ManagedModuleInstallService
{
    private readonly ILogger _logger;
    private readonly ManagedModuleRepositoryClient _repositoryClient;
    private readonly ManagedModuleArchiveExtractor _extractor;

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
    {
        Validate(request);

        var version = string.IsNullOrWhiteSpace(request.Version)
            ? await ResolveLatestVersionAsync(request, cancellationToken).ConfigureAwait(false)
            : request.Version!.Trim();
        var moduleRoot = ManagedModuleInstallRootResolver.Resolve(request.Scope, request.ShellEdition, request.ModuleRoot);
        var modulePath = Path.Combine(moduleRoot, request.Name.Trim(), version);

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

        var ownsCache = string.IsNullOrWhiteSpace(request.PackageCacheDirectory);
        var cacheDirectory = ownsCache
            ? Path.Combine(Path.GetTempPath(), "PowerForge.ManagedModules", Guid.NewGuid().ToString("N"))
            : Path.GetFullPath(request.PackageCacheDirectory!.Trim().Trim('"'));
        var stageRoot = Path.Combine(Path.GetTempPath(), "PowerForge.ManagedModules.Stage", Guid.NewGuid().ToString("N"));
        var stageModulePath = Path.Combine(stageRoot, request.Name.Trim(), version);

        try
        {
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

            if (Directory.Exists(modulePath))
                Directory.Delete(modulePath, recursive: true);

            Directory.Move(stageModulePath, modulePath);
            CleanupEmptyStage(stageRoot);

            return new ManagedModuleInstallResult
            {
                Name = request.Name.Trim(),
                Version = version,
                Status = ManagedModuleInstallStatus.Installed,
                RepositoryName = request.Repository.Name,
                ModuleRoot = moduleRoot,
                ModulePath = modulePath,
                Download = download,
                FileCount = extraction.FileCount,
                ExtractedBytes = extraction.BytesWritten
            };
        }
        finally
        {
            if (Directory.Exists(stageRoot))
                Directory.Delete(stageRoot, recursive: true);
            if (ownsCache && Directory.Exists(cacheDirectory))
                Directory.Delete(cacheDirectory, recursive: true);
        }
    }

    private async Task<string> ResolveLatestVersionAsync(ManagedModuleInstallRequest request, CancellationToken cancellationToken)
    {
        var versions = await _repositoryClient.GetVersionsAsync(
            request.Repository,
            request.Name,
            request.IncludePrerelease,
            request.Credential,
            cancellationToken).ConfigureAwait(false);

        var latest = versions.LastOrDefault();
        if (latest is null)
            throw new InvalidOperationException($"No versions of '{request.Name}' were found in repository '{request.Repository.Name}'.");

        return latest.Version;
    }

    private static void Validate(ManagedModuleInstallRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (request.Repository is null)
            throw new ArgumentException("Repository is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Module name is required.", nameof(request));
        if (request.Scope == ManagedModuleInstallScope.Custom && string.IsNullOrWhiteSpace(request.ModuleRoot))
            throw new ArgumentException("ModuleRoot is required when Scope is Custom.", nameof(request));
    }

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
}
