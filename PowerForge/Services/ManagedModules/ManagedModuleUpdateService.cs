namespace PowerForge;

/// <summary>
/// Updates installed PowerShell modules using managed repository and install services.
/// </summary>
public sealed class ManagedModuleUpdateService
{
    private readonly ILogger _logger;
    private readonly ManagedModuleRepositoryClient _repositoryClient;
    private readonly ManagedModuleInstallService _installService;

    /// <summary>
    /// Creates a managed module update service.
    /// </summary>
    /// <param name="logger">Logger used for diagnostics.</param>
    /// <param name="repositoryClient">Optional repository client.</param>
    /// <param name="installService">Optional install service.</param>
    public ManagedModuleUpdateService(
        ILogger logger,
        ManagedModuleRepositoryClient? repositoryClient = null,
        ManagedModuleInstallService? installService = null)
    {
        _logger = logger ?? new NullLogger();
        _repositoryClient = repositoryClient ?? new ManagedModuleRepositoryClient(_logger);
        _installService = installService ?? new ManagedModuleInstallService(_logger, _repositoryClient);
    }

    /// <summary>
    /// Updates a module in the selected scope.
    /// </summary>
    /// <param name="request">Update request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Update result.</returns>
    public async Task<ManagedModuleUpdateResult> UpdateAsync(
        ManagedModuleUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);

        var moduleRoot = ManagedModuleInstallRootResolver.Resolve(request.Scope, request.ShellEdition, request.ModuleRoot);
        var targetVersion = string.IsNullOrWhiteSpace(request.Version)
            ? await ResolveLatestVersionAsync(request, cancellationToken).ConfigureAwait(false)
            : request.Version!.Trim();
        var installedVersions = GetInstalledVersions(moduleRoot, request.Name);
        var currentVersion = installedVersions.LastOrDefault();
        var modulePath = Path.Combine(moduleRoot, request.Name.Trim(), targetVersion);

        if (currentVersion is not null &&
            !request.Force &&
            ManagedModuleVersionComparer.Instance.Compare(currentVersion, targetVersion) >= 0)
        {
            _logger.Verbose($"Managed module update skipped '{request.Name}' because {currentVersion} satisfies target {targetVersion}.");
            return new ManagedModuleUpdateResult
            {
                Name = request.Name.Trim(),
                TargetVersion = targetVersion,
                PreviousVersion = currentVersion,
                Status = ManagedModuleUpdateStatus.UpToDate,
                RepositoryName = request.Repository.Name,
                ModuleRoot = moduleRoot,
                ModulePath = Path.Combine(moduleRoot, request.Name.Trim(), currentVersion)
            };
        }

        var install = await _installService.InstallAsync(
            new ManagedModuleInstallRequest
            {
                Repository = request.Repository,
                Name = request.Name,
                Version = targetVersion,
                IncludePrerelease = request.IncludePrerelease,
                Scope = request.Scope,
                ShellEdition = request.ShellEdition,
                ModuleRoot = request.ModuleRoot,
                PackageCacheDirectory = request.PackageCacheDirectory,
                Credential = request.Credential,
                Force = true
            },
            cancellationToken).ConfigureAwait(false);

        return new ManagedModuleUpdateResult
        {
            Name = request.Name.Trim(),
            TargetVersion = targetVersion,
            PreviousVersion = currentVersion,
            Status = currentVersion is null ? ManagedModuleUpdateStatus.InstalledMissing : ManagedModuleUpdateStatus.Updated,
            RepositoryName = request.Repository.Name,
            ModuleRoot = moduleRoot,
            ModulePath = modulePath,
            InstallResult = install
        };
    }

    private async Task<string> ResolveLatestVersionAsync(ManagedModuleUpdateRequest request, CancellationToken cancellationToken)
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

    private static IReadOnlyList<string> GetInstalledVersions(string moduleRoot, string moduleName)
    {
        var moduleFolder = Path.Combine(moduleRoot, moduleName.Trim());
        if (!Directory.Exists(moduleFolder))
            return Array.Empty<string>();

        return Directory.EnumerateDirectories(moduleFolder)
            .Select(Path.GetFileName)
            .Where(static version => !string.IsNullOrWhiteSpace(version))
            .Select(static version => version!)
            .OrderBy(static version => version, ManagedModuleVersionComparer.Instance)
            .ToArray();
    }

    private static void Validate(ManagedModuleUpdateRequest request)
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
}
