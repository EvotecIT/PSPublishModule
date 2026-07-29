using System.IO.Compression;
using System.Xml.Linq;

namespace PowerForge;

/// <summary>
/// Restores exact public NuGet package bytes before a verified GitHub release recovery.
/// </summary>
internal sealed class PublishedNuGetAssetRecoveryService
{
    private readonly ILogger _logger;
    private readonly NuGetV3PackageDownloader _downloader;

    internal PublishedNuGetAssetRecoveryService(
        ILogger logger,
        NuGetV3PackageDownloader? downloader = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _downloader = downloader ?? new NuGetV3PackageDownloader();
    }

    internal string[] Restore(
        string serviceIndexUrl,
        string expectedVersion,
        IEnumerable<string> releaseAssetPaths,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(serviceIndexUrl))
            throw new InvalidOperationException(
                "Verified GitHub recovery requires Packages.PublishSource to restore exact published NuGet bytes.");
        if (string.IsNullOrWhiteSpace(expectedVersion))
            throw new InvalidOperationException(
                "Verified GitHub recovery requires an exact release version to restore published NuGet bytes.");
        if (releaseAssetPaths is null)
            throw new ArgumentNullException(nameof(releaseAssetPaths));

        var packagePaths = releaseAssetPaths
            .Where(path =>
                !string.IsNullOrWhiteSpace(path) &&
                string.Equals(Path.GetExtension(path), ".nupkg", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var restored = new List<string>(packagePaths.Length);

        foreach (var packagePath in packagePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(packagePath))
                throw new FileNotFoundException(
                    $"The rebuilt NuGet asset required for recovery was not found: {packagePath}",
                    packagePath);

            var localIdentity = ReadIdentity(packagePath);
            ValidateIdentity(localIdentity, expectedVersion, packagePath, "rebuilt");

            var temporaryPath = packagePath + ".published-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                _downloader.DownloadPackageAsync(
                        serviceIndexUrl,
                        localIdentity.Id,
                        expectedVersion,
                        temporaryPath,
                        new PrivateGalleryIndexOptions(),
                        cancellationToken)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();

                var publishedIdentity = ReadIdentity(temporaryPath);
                ValidateIdentity(publishedIdentity, expectedVersion, temporaryPath, "published");
                if (!string.Equals(
                        publishedIdentity.Id,
                        localIdentity.Id,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Published NuGet recovery returned package '{publishedIdentity.Id}', expected '{localIdentity.Id}'.");
                }

                File.Replace(temporaryPath, packagePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                restored.Add(packagePath);
                _logger.Info(
                    $"Restored exact published NuGet bytes for GitHub recovery: {localIdentity.Id} {expectedVersion}");
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
                catch
                {
                    // The protected source asset has already been replaced or the recovery failed.
                    // Temporary-file cleanup is best effort and must not hide the primary error.
                }
            }
        }

        return restored.ToArray();
    }

    private static NuGetPackageIdentity ReadIdentity(string packagePath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            var nuspec = archive.Entries.FirstOrDefault(entry =>
                entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
            if (nuspec is null)
                throw new InvalidOperationException("The package does not contain a nuspec.");

            using var stream = nuspec.Open();
            var document = XDocument.Load(stream);
            var metadata = document.Root?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "metadata", StringComparison.OrdinalIgnoreCase));
            var id = metadata?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "id", StringComparison.OrdinalIgnoreCase))?.Value;
            var version = metadata?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "version", StringComparison.OrdinalIgnoreCase))?.Value;
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version))
                throw new InvalidOperationException("The package nuspec does not contain an id and version.");

            return new NuGetPackageIdentity(id!.Trim(), version!.Trim());
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Unable to read NuGet package identity from '{packagePath}'.",
                exception);
        }
    }

    private static void ValidateIdentity(
        NuGetPackageIdentity identity,
        string expectedVersion,
        string packagePath,
        string source)
    {
        if (!string.Equals(identity.Version, expectedVersion, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The {source} NuGet asset '{packagePath}' has version '{identity.Version}', expected '{expectedVersion}'.");
        }
    }

    private sealed class NuGetPackageIdentity
    {
        internal NuGetPackageIdentity(string id, string version)
        {
            Id = id;
            Version = version;
        }

        internal string Id { get; }

        internal string Version { get; }
    }
}
