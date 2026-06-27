namespace PowerForge;

/// <summary>
/// Updates installed PowerShell modules using managed repository and install services.
/// </summary>
public sealed class ManagedModuleUpdateService
{
    private readonly ILogger _logger;
    private readonly ManagedModuleRepositoryClient _repositoryClient;
    private readonly ManagedModuleInstallService _installService;
    private readonly ManagedModuleReceiptStore _receiptStore;

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
        _receiptStore = new ManagedModuleReceiptStore();
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
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var moduleRoot = ManagedModuleInstallRootResolver.Resolve(request.Scope, request.ShellEdition, request.ModuleRoot);
        var targetVersion = await ResolveSelectedVersionAsync(request, cancellationToken).ConfigureAwait(false);
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
                ModulePath = Path.Combine(moduleRoot, request.Name.Trim(), currentVersion),
                Elapsed = stopwatch.Elapsed
            };
        }

        var install = await _installService.InstallAsync(
            new ManagedModuleInstallRequest
            {
                Repository = request.Repository,
                Name = request.Name,
                Version = targetVersion,
                MinimumVersion = null,
                MaximumVersion = null,
                VersionPolicy = null,
                IncludePrerelease = request.IncludePrerelease,
                Scope = request.Scope,
                ShellEdition = request.ShellEdition,
                ModuleRoot = request.ModuleRoot,
                PackageCacheDirectory = request.PackageCacheDirectory,
                Credential = request.Credential,
                Force = true,
                AllowClobber = request.AllowClobber,
                AcceptLicense = request.AcceptLicense,
                SkipDependencyCheck = request.SkipDependencyCheck
            },
            cancellationToken).ConfigureAwait(false);
        if (install.Status == ManagedModuleInstallStatus.Installed)
            _receiptStore.WriteReceipt(request.Repository, install, currentVersion, "Update");

        return new ManagedModuleUpdateResult
        {
            Name = request.Name.Trim(),
            TargetVersion = targetVersion,
            PreviousVersion = currentVersion,
            Status = currentVersion is null ? ManagedModuleUpdateStatus.InstalledMissing : ManagedModuleUpdateStatus.Updated,
            RepositoryName = request.Repository.Name,
            ModuleRoot = moduleRoot,
            ModulePath = modulePath,
            Elapsed = stopwatch.Elapsed,
            InstallResult = install,
            Receipt = install.Receipt,
            ReceiptPath = install.ReceiptPath
        };
    }

    private async Task<string> ResolveSelectedVersionAsync(ManagedModuleUpdateRequest request, CancellationToken cancellationToken)
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
}
