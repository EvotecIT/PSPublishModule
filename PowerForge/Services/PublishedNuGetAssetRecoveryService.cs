using System.IO.Compression;
using System.Xml.Linq;

namespace PowerForge;

/// <summary>
/// Restores exact public NuGet package bytes before a verified GitHub release recovery.
/// </summary>
internal sealed partial class PublishedNuGetAssetRecoveryService
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

        var normalizedAssetPaths = releaseAssetPaths
            .Where(path =>
                !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var symbolPackagePaths = normalizedAssetPaths
            .Where(path => string.Equals(
                Path.GetExtension(path),
                ".snupkg",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (symbolPackagePaths.Length > 0)
        {
            throw new InvalidOperationException(
                "Verified GitHub recovery cannot prove byte identity for published symbol packages. " +
                "Remove symbol assets from the release contract or resume from the original signed payload: " +
                string.Join(", ", symbolPackagePaths.Select(Path.GetFileName)));
        }

        var packagePaths = normalizedAssetPaths
            .Where(path => string.Equals(
                Path.GetExtension(path),
                ".nupkg",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var plans = new List<PackageRecoveryPlan>(packagePaths.Length);

        foreach (var packagePath in packagePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(packagePath))
                throw new FileNotFoundException(
                    $"The rebuilt NuGet asset required for recovery was not found: {packagePath}",
                    packagePath);

            var localIdentity = ReadIdentity(packagePath);
            ValidateIdentity(localIdentity, expectedVersion, packagePath, "rebuilt");
            var expectedReleaseZipName = $"{localIdentity.Id}.{expectedVersion}.zip";
            var releaseZipMatches = normalizedAssetPaths
                .Where(path => string.Equals(
                    Path.GetFileName(path),
                    expectedReleaseZipName,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (releaseZipMatches.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Verified GitHub recovery found multiple release ZIPs for package '{localIdentity.Id}': " +
                    string.Join(", ", releaseZipMatches));
            }

            plans.Add(new PackageRecoveryPlan(
                packagePath,
                localIdentity,
                releaseZipMatches.SingleOrDefault()));
        }

        var rewrites = new List<RecoveryFileRewrite>(plans.Count * 2);
        try
        {
            foreach (var plan in plans)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _downloader.DownloadPackageAsync(
                        serviceIndexUrl,
                        plan.Identity.Id,
                        expectedVersion,
                        plan.PublishedPackagePath,
                        new PrivateGalleryIndexOptions(),
                        cancellationToken)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();

                var publishedIdentity = ReadIdentity(plan.PublishedPackagePath);
                ValidateIdentity(publishedIdentity, expectedVersion, plan.PublishedPackagePath, "published");
                if (!string.Equals(
                        publishedIdentity.Id,
                        plan.Identity.Id,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Published NuGet recovery returned package '{publishedIdentity.Id}', expected '{plan.Identity.Id}'.");
                }

                rewrites.Add(new RecoveryFileRewrite(plan.PackagePath, plan.PublishedPackagePath));
            }

            var publishedPackagePaths = plans
                .Select(static plan => plan.PublishedPackagePath)
                .ToArray();
            foreach (var plan in plans.Where(static plan => !string.IsNullOrWhiteSpace(plan.ReleaseZipPath)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.IsNullOrWhiteSpace(plan.ReleaseZipPath))
                {
                    RewriteReleaseZipFromPublishedPackages(
                        publishedPackagePaths,
                        plan.PublishedPackagePath,
                        plan.ReleaseZipPath!,
                        plan.PublishedReleaseZipPath!,
                        cancellationToken);
                    rewrites.Add(new RecoveryFileRewrite(plan.ReleaseZipPath!, plan.PublishedReleaseZipPath!));
                }
            }

            RecoveryFileReplacementTransaction.Apply(rewrites, cancellationToken);
            foreach (var plan in plans)
            {
                _logger.Info(
                    $"Restored exact published NuGet bytes for GitHub recovery: {plan.Identity.Id} {expectedVersion}");
                if (!string.IsNullOrWhiteSpace(plan.ReleaseZipPath))
                    _logger.Info($"Restored published package payload in release ZIP: {Path.GetFileName(plan.ReleaseZipPath)}");
            }

            return plans
                .SelectMany(static plan => new[] { plan.PackagePath, plan.ReleaseZipPath })
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Select(static path => path!)
                .ToArray();
        }
        finally
        {
            foreach (var rewrite in rewrites)
            {
                TryDelete(rewrite.ReplacementPath);
                if (rewrite.DeleteBackupOnCleanup)
                    TryDelete(rewrite.BackupPath);
            }
            foreach (var plan in plans)
            {
                TryDelete(plan.PublishedPackagePath);
                TryDelete(plan.PublishedReleaseZipPath);
            }
        }
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

    private sealed class PackageRecoveryPlan
    {
        internal PackageRecoveryPlan(
            string packagePath,
            NuGetPackageIdentity identity,
            string? releaseZipPath)
        {
            PackagePath = packagePath;
            Identity = identity;
            ReleaseZipPath = releaseZipPath;
            PublishedPackagePath = packagePath + ".published-" + Guid.NewGuid().ToString("N") + ".tmp";
            PublishedReleaseZipPath = string.IsNullOrWhiteSpace(releaseZipPath)
                ? null
                : releaseZipPath + ".published-" + Guid.NewGuid().ToString("N") + ".tmp";
        }

        internal string PackagePath { get; }

        internal NuGetPackageIdentity Identity { get; }

        internal string? ReleaseZipPath { get; }

        internal string PublishedPackagePath { get; }

        internal string? PublishedReleaseZipPath { get; }
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Cleanup must not hide the recovery result.
        }
    }
}
