namespace PowerForge;

/// <summary>
/// Packages and publishes PowerShell modules through managed repository operations.
/// </summary>
public sealed class ManagedModulePublishService
{
    private readonly ManagedModulePackService _packService;
    private readonly ManagedModuleRepositoryClient _repositoryClient;
    private readonly ManagedModulePackageReader _packageReader;

    /// <summary>
    /// Creates a managed module publish service.
    /// </summary>
    /// <param name="logger">Logger used for diagnostics.</param>
    /// <param name="repositoryClient">Optional repository client.</param>
    public ManagedModulePublishService(ILogger logger, ManagedModuleRepositoryClient? repositoryClient = null)
    {
        _packService = new ManagedModulePackService();
        _repositoryClient = repositoryClient ?? new ManagedModuleRepositoryClient(logger ?? new NullLogger());
        _packageReader = new ManagedModulePackageReader();
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
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

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
            SkipModuleManifestValidate = request.SkipModuleManifestValidate,
            Force = request.Force
        });
        var metadata = _packageReader.ReadMetadata(package.PackagePath);
        await VerifyDependenciesAsync(request, metadata, cancellationToken).ConfigureAwait(false);
        var publishRepository = request.PublishRepository ?? request.Repository;

        var publish = await _repositoryClient.PublishPackageAsync(
            publishRepository,
            package.PackagePath,
            request.PublishCredential ?? request.Credential,
            request.Force,
            cancellationToken).ConfigureAwait(false);

        return new ManagedModulePublishResult
        {
            Name = package.Name,
            Version = package.Version,
            PackagePath = package.PackagePath,
            FileCount = package.FileCount,
            PackageBytes = package.PackageBytes,
            Elapsed = stopwatch.Elapsed,
            RepositoryName = publishRepository.Name,
            RepositorySource = publishRepository.Source,
            PublishSource = publish.PublishSource,
            StatusCode = publish.StatusCode,
            Published = publish.Published,
            Duplicate = publish.Duplicate,
            Message = publish.Message
        };
    }

    private async Task VerifyDependenciesAsync(
        ManagedModulePublishRequest request,
        ManagedModulePackageMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (request.SkipDependenciesCheck || metadata.ManifestDependencies.Count == 0)
            return;

        var externalDependencies = metadata.ManifestExternalModuleDependencies.Count == 0
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : metadata.ManifestExternalModuleDependencies
                .Where(static dependency => !string.IsNullOrWhiteSpace(dependency))
                .Select(static dependency => dependency.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        var dependencyRepository = ResolveDependencyCheckRepository(request);
        if (dependencyRepository is null)
            return;

        foreach (var dependency in metadata.ManifestDependencies)
        {
            if (externalDependencies.Contains(dependency.Id))
                continue;

            var range = ManagedModuleVersionRange.Parse(dependency.VersionRange);
            if (!await RepositoryHasDependencyAsync(request, dependencyRepository, dependency, range, cancellationToken).ConfigureAwait(false))
                missing.Add(FormatDependency(dependency, range));
        }

        if (missing.Count == 0)
            return;

        throw new InvalidOperationException(
            $"Required module dependency check failed for repository '{request.Repository.Name}'. Missing or incompatible: {string.Join(", ", missing)}. Use SkipDependenciesCheck to bypass this check.");
    }

    private static string FormatDependency(ManagedModuleDependencyInfo dependency, ManagedModuleVersionRange range)
        => range == ManagedModuleVersionRange.Any
            ? dependency.Id
            : $"{dependency.Id} {range}";

    private static ManagedModuleRepository? ResolveDependencyCheckRepository(ManagedModulePublishRequest request)
    {
        if (request.PublishRepository is not null)
            return request.Repository;

        var source = request.Repository.Source.Trim().TrimEnd('/');
        if (source.EndsWith("/package", StringComparison.OrdinalIgnoreCase))
        {
            var readSource = source.Substring(0, source.Length - "/package".Length);
            return string.IsNullOrWhiteSpace(readSource)
                ? request.Repository
                : new ManagedModuleRepository(
                    request.Repository.Name,
                    readSource,
                    ManagedModuleRepositoryKind.NuGetV2,
                    request.Repository.Trusted);
        }

        if (request.Repository.Kind == ManagedModuleRepositoryKind.NuGetV3 &&
            Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
            (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) &&
            !source.EndsWith("index.json", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return request.Repository;
    }

    private async Task<bool> RepositoryHasDependencyAsync(
        ManagedModulePublishRequest request,
        ManagedModuleRepository dependencyRepository,
        ManagedModuleDependencyInfo dependency,
        ManagedModuleVersionRange range,
        CancellationToken cancellationToken)
    {
        try
        {
            var versions = await _repositoryClient.GetVersionsAsync(
                dependencyRepository,
                dependency.Id,
                range.AllowsPrerelease,
                request.Credential,
                cancellationToken).ConfigureAwait(false);

            var matches = versions.Where(version => range.IsSatisfiedBy(version.Version));
            if (range.ExactVersion is null)
                matches = matches.Where(static version => version.Listed);

            return matches.Any();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string ResolveOutputDirectory(ManagedModulePublishRequest request)
    {
        var publishRepository = request.PublishRepository ?? request.Repository;
        if (!string.IsNullOrWhiteSpace(request.OutputDirectory))
        {
            var outputDirectory = Path.GetFullPath(request.OutputDirectory!.Trim().Trim('"'));
            if (IsSameLocalDirectory(outputDirectory, publishRepository.Source))
                return Path.Combine(Path.GetTempPath(), "PowerForge.ManagedModules.Publish", Guid.NewGuid().ToString("N"));

            return outputDirectory;
        }

        return Path.Combine(Path.GetTempPath(), "PowerForge.ManagedModules.Publish", Guid.NewGuid().ToString("N"));
    }

    private static bool IsSameLocalDirectory(string outputDirectory, string repositorySource)
    {
        if (string.IsNullOrWhiteSpace(repositorySource))
            return false;

        try
        {
            var repositoryDirectory = Path.GetFullPath(repositorySource.Trim().Trim('"'));
            return string.Equals(
                outputDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                repositoryDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
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
