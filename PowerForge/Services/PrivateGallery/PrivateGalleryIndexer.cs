namespace PowerForge;

/// <summary>
/// Coordinates private gallery feed inventory and optional package inspection.
/// </summary>
public sealed class PrivateGalleryIndexer
{
    private readonly AzureArtifactsPrivateGalleryClient _azureArtifactsClient;
    private readonly NuGetV3PackageDownloader _packageDownloader;
    private readonly PowerShellModulePackageInspector _packageInspector;

    /// <summary>
    /// Creates a new private gallery indexer.
    /// </summary>
    public PrivateGalleryIndexer()
        : this(new AzureArtifactsPrivateGalleryClient(), new NuGetV3PackageDownloader(), new PowerShellModulePackageInspector())
    {
    }

    /// <summary>
    /// Creates a new private gallery indexer with explicit dependencies.
    /// </summary>
    /// <param name="azureArtifactsClient">Azure Artifacts inventory client.</param>
    /// <param name="packageDownloader">NuGet package downloader.</param>
    /// <param name="packageInspector">Package inspector.</param>
    public PrivateGalleryIndexer(
        AzureArtifactsPrivateGalleryClient azureArtifactsClient,
        NuGetV3PackageDownloader packageDownloader,
        PowerShellModulePackageInspector packageInspector)
    {
        _azureArtifactsClient = azureArtifactsClient ?? throw new ArgumentNullException(nameof(azureArtifactsClient));
        _packageDownloader = packageDownloader ?? throw new ArgumentNullException(nameof(packageDownloader));
        _packageInspector = packageInspector ?? throw new ArgumentNullException(nameof(packageInspector));
    }

    /// <summary>
    /// Indexes a private gallery feed.
    /// </summary>
    /// <param name="options">Indexing options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Indexing result.</returns>
    public async Task<PrivateGalleryIndexResult> IndexAsync(
        PrivateGalleryIndexOptions options,
        CancellationToken cancellationToken = default)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (options.Provider != PrivateGalleryIndexProvider.AzureArtifacts)
            throw new NotSupportedException($"Private gallery provider '{options.Provider}' is not supported.");

        var warnings = new List<string>();
        var endpoint = AzureArtifactsRepositoryEndpoints.Create(
            organization: options.Organization ?? string.Empty,
            project: options.Project,
            feed: options.Feed ?? string.Empty,
            repositoryName: options.RepositoryName);

        var packages = await _azureArtifactsClient.GetPackagesAsync(options, warnings, cancellationToken).ConfigureAwait(false);
        if (options.IncludeMetrics)
            await ApplyMetricsAsync(options, packages, warnings, cancellationToken).ConfigureAwait(false);

        if (options.IncludePackageContent)
            await InspectPackageContentAsync(options, endpoint.PSResourceGetUri, packages, warnings, cancellationToken).ConfigureAwait(false);

        foreach (var package in packages)
        {
            package.Module ??= package.Versions.FirstOrDefault(static version => version.Module is not null)?.Module;
            package.Warnings.AddRange(package.Module?.Warnings ?? Enumerable.Empty<string>());
        }

        var document = new PrivateGalleryDocument
        {
            Title = string.IsNullOrWhiteSpace(options.Title) ? "Private Gallery" : options.Title!.Trim(),
            GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
            Provider = options.Provider,
            Feed = new PrivateGalleryFeed
            {
                Organization = endpoint.Organization,
                Project = endpoint.Project,
                Name = endpoint.Feed,
                RepositoryName = endpoint.RepositoryName,
                NuGetServiceIndexUrl = endpoint.PSResourceGetUri
            },
            Packages = packages,
            Warnings = warnings
        };
        document.Summary = new PrivateGallerySummary
        {
            PackageCount = packages.Count,
            VersionCount = packages.Sum(static package => package.Versions.Count),
            CommandCount = packages.Sum(static package => package.Module?.Commands.Count ?? 0),
            DocumentCount = packages.Sum(static package => package.Module?.Documents.Count ?? 0),
            TotalDownloads = packages.Any(static package => package.Metrics?.DownloadCount is not null)
                ? packages.Sum(static package => package.Metrics?.DownloadCount ?? 0)
                : null
        };

        return new PrivateGalleryIndexResult
        {
            Document = document,
            Warnings = warnings
        };
    }

    private async Task InspectPackageContentAsync(
        PrivateGalleryIndexOptions options,
        string serviceIndexUrl,
        List<PrivateGalleryPackage> packages,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var tempRoot = ResolveTempRoot(options.TempDirectory);
        Directory.CreateDirectory(tempRoot);
        var maxVersions = options.MaxVersionsPerPackage > 0 ? options.MaxVersionsPerPackage : 1;

        foreach (var package in packages)
        {
            var versions = package.Versions.Count == 0 && !string.IsNullOrWhiteSpace(package.LatestVersion)
                ? new List<PrivateGalleryPackageVersion> { new() { Id = package.LatestVersion!, Version = package.LatestVersion!, IsLatest = true } }
                : package.Versions;

            foreach (var version in versions
                         .OrderByDescending(static item => item.IsLatest)
                         .ThenByDescending(static item => item.PublishedAtUtc)
                         .Take(maxVersions))
            {
                var packagePath = Path.Combine(tempRoot, MakeSafeFileName(package.Name + "." + version.Version + ".nupkg"));
                try
                {
                    await _packageDownloader.DownloadPackageAsync(
                        serviceIndexUrl,
                        package.Name,
                        version.NormalizedVersion ?? version.Version,
                        packagePath,
                        options,
                        cancellationToken).ConfigureAwait(false);

                    var module = _packageInspector.Inspect(packagePath);
                    version.Module = module;
                    package.Module ??= module;
                }
                catch (Exception ex)
                {
                    var message = $"Failed to inspect package '{package.Name}' version '{version.Version}': {ex.GetType().Name}: {ex.Message}";
                    warnings.Add(message);
                    package.Warnings.Add(message);
                }
                finally
                {
                    TryDelete(packagePath);
                }
            }
        }
    }

    private async Task ApplyMetricsAsync(
        PrivateGalleryIndexOptions options,
        List<PrivateGalleryPackage> packages,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            var packageMetrics = await _azureArtifactsClient
                .GetPackageMetricsAsync(options, packages.Select(static package => package.Id), cancellationToken)
                .ConfigureAwait(false);
            foreach (var package in packages)
            {
                if (packageMetrics.TryGetValue(package.Id, out var metrics))
                    package.Metrics = metrics;
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Azure Artifacts package metrics were skipped: {ex.GetType().Name}: {ex.Message}");
        }

        foreach (var package in packages)
        {
            try
            {
                var versionMetrics = await _azureArtifactsClient
                    .GetPackageVersionMetricsAsync(options, package.Id, package.Versions.Select(static version => version.Id), cancellationToken)
                    .ConfigureAwait(false);
                foreach (var version in package.Versions)
                {
                    if (versionMetrics.TryGetValue(version.Id, out var metrics))
                        version.Metrics = metrics;
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"Azure Artifacts version metrics were skipped for '{package.Name}': {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private static string ResolveTempRoot(string? configured)
        => string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetTempPath(), "PowerForge", "private-gallery", Guid.NewGuid().ToString("N"))
            : Path.GetFullPath(configured);

    private static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort temp cleanup.
        }
    }
}
