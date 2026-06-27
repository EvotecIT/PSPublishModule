namespace PowerForge;

/// <summary>
/// Packages and publishes PowerShell modules through managed repository operations.
/// </summary>
public sealed class ManagedModulePublishService
{
    private readonly ManagedModulePackService _packService;
    private readonly ManagedModuleRepositoryClient _repositoryClient;

    /// <summary>
    /// Creates a managed module publish service.
    /// </summary>
    /// <param name="logger">Logger used for diagnostics.</param>
    /// <param name="repositoryClient">Optional repository client.</param>
    public ManagedModulePublishService(ILogger logger, ManagedModuleRepositoryClient? repositoryClient = null)
    {
        _packService = new ManagedModulePackService();
        _repositoryClient = repositoryClient ?? new ManagedModuleRepositoryClient(logger ?? new NullLogger());
    }

    /// <summary>
    /// Packages a module and publishes the package to the requested repository.
    /// </summary>
    /// <param name="request">Publish request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Publish result.</returns>
    public async Task<ManagedModulePublishResult> PublishAsync(
        ManagedModulePublishRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);

        var outputDirectory = ResolveOutputDirectory(request);
        var package = _packService.Pack(new ManagedModulePackRequest
        {
            ModulePath = request.ModulePath,
            ManifestPath = request.ManifestPath,
            Name = request.Name,
            Version = request.Version,
            OutputDirectory = outputDirectory,
            Authors = request.Authors,
            Description = request.Description,
            ProjectUrl = request.ProjectUrl,
            Tags = request.Tags,
            Force = request.Force
        });

        var publish = await _repositoryClient.PublishPackageAsync(
            request.Repository,
            package.PackagePath,
            request.Credential,
            request.Force,
            cancellationToken).ConfigureAwait(false);

        return new ManagedModulePublishResult
        {
            Name = package.Name,
            Version = package.Version,
            PackagePath = package.PackagePath,
            FileCount = package.FileCount,
            PackageBytes = package.PackageBytes,
            RepositoryName = request.Repository.Name,
            RepositorySource = request.Repository.Source,
            PublishSource = publish.PublishSource,
            StatusCode = publish.StatusCode,
            Published = publish.Published,
            Duplicate = publish.Duplicate,
            Message = publish.Message
        };
    }

    private static string ResolveOutputDirectory(ManagedModulePublishRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.OutputDirectory))
            return Path.GetFullPath(request.OutputDirectory!.Trim().Trim('"'));
        return Path.Combine(Path.GetTempPath(), "PowerForge.ManagedModules.Publish", Guid.NewGuid().ToString("N"));
    }

    private static void Validate(ManagedModulePublishRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (request.Repository is null)
            throw new ArgumentException("Repository is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.ModulePath))
            throw new ArgumentException("ModulePath is required.", nameof(request));
    }
}
