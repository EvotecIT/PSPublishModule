using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>Connects signed release preparation and the existing catalog submission owners.</summary>
internal sealed partial class CatalogUpdateService
{
    private const string ReceiptName = "catalog-update.json";
    private readonly ReleaseCatalogPreparationService _preparation;
    private readonly WingetSubmissionService _winget;
    private readonly StoreSubmissionService _store;
    private readonly Func<CatalogInstaller, bool, CancellationToken, Task> _verifyDownload;
    private readonly Func<string, CancellationToken, Task<string>> _readWingetStatus;

    public CatalogUpdateService(ReleaseCatalogPreparationService? preparation = null,
        WingetSubmissionService? winget = null, StoreSubmissionService? store = null,
        Func<CatalogInstaller, bool, CancellationToken, Task>? verifyDownload = null,
        Func<string, CancellationToken, Task<string>>? readWingetStatus = null)
    {
        _preparation = preparation ?? new ReleaseCatalogPreparationService();
        _winget = winget ?? new WingetSubmissionService();
        _store = store ?? new StoreSubmissionService();
        _verifyDownload = verifyDownload ?? VerifyDownloadAsync;
        _readWingetStatus = readWingetStatus ?? ReadWingetStatusAsync;
    }

    public async Task<CatalogUpdateReceipt> PrepareAsync(CatalogUpdateSpec profile, string profilePath,
        PowerForgeReleaseSpec release, string releaseConfigPath, string manifest, string checksums,
        string assetRoot, string output, string deliveryReleaseId, CancellationToken cancellationToken = default, string? resumeFrom = null)
    {
        RequireProfile(profile);
        output = Path.GetFullPath(output);
        if (Directory.Exists(output) || File.Exists(output))
            throw new InvalidOperationException("Prepared output exists. Use submit/status to resume, or prepare a new directory.");
        if (string.IsNullOrWhiteSpace(profile.StoreInstallerUrlTemplate) ||
            !profile.StoreInstallerUrlTemplate!.Contains("{releaseId}") || !profile.StoreInstallerUrlTemplate.Contains("{artifactKey}") ||
            !Regex.IsMatch(deliveryReleaseId ?? "", "^[a-zA-Z0-9][a-zA-Z0-9._-]{0,179}$"))
            throw new InvalidOperationException("An immutable Store URL template and safe delivery release ID are required.");
        var temporary = output + ".preparing-" + Guid.NewGuid().ToString("N");
        try
        {
            var prepared = _preparation.Prepare(release, manifest, checksums, assetRoot, temporary);
            var packages = JsonSerializer.Deserialize(File.ReadAllText(prepared.DesktopPackagesPath),
                ReleaseCatalogJsonContext.Default.StoreSubmissionDesktopPackageArray)!;
            for (var i = 0; i < prepared.Artifacts.Length; i++)
            {
                var artifact = prepared.Artifacts[i];
                var key = profile.StoreArtifactKeys.SingleOrDefault(pair =>
                    string.Equals(pair.Key, artifact.Architecture, StringComparison.OrdinalIgnoreCase)).Value;
                if (string.IsNullOrWhiteSpace(key) || !Regex.IsMatch(key, "^[a-zA-Z0-9][a-zA-Z0-9._-]{0,79}$"))
                    throw new InvalidOperationException("A unique safe Store artifact key is required for each installer architecture.");
                artifact.StoreUrl = profile.StoreInstallerUrlTemplate!
                    .Replace("{releaseId}", deliveryReleaseId).Replace("{artifactKey}", key);
                CatalogDownloadVerifier.RequireHttps(artifact.StoreUrl);
                packages[i].PackageUrl = artifact.StoreUrl;
                await _verifyDownload(artifact, true, cancellationToken).ConfigureAwait(false);
            }
            if (prepared.Artifacts.Select(artifact => artifact.StoreUrl).Distinct(StringComparer.Ordinal).Count() != prepared.Artifacts.Length)
                throw new InvalidOperationException("Store installer URLs must be distinct for each architecture.");
            File.WriteAllText(prepared.DesktopPackagesPath,
                JsonSerializer.Serialize(packages, ReleaseCatalogJsonContext.Default.StoreSubmissionDesktopPackageArray));
            var receipt = new CatalogUpdateReceipt
            {
                PackageIdentifier = prepared.PackageIdentifier, PackageVersion = prepared.PackageVersion,
                DeliveryReleaseId = deliveryReleaseId!, ProfileSha256 = Hash(profilePath),
                ReleaseConfigSha256 = Hash(releaseConfigPath), Artifacts = prepared.Artifacts,
                Files = prepared.WingetManifestPaths.Append(prepared.DesktopPackagesPath).ToDictionary(
                    path => FrameworkCompatibility.GetRelativePath(temporary, path).Replace('\\', '/'), Hash)
            };
            if (resumeFrom is not null)
            {
                var previous = Read(resumeFrom, profilePath, releaseConfigPath);
                RequireSameRelease(previous, receipt);
                receipt.Winget = previous.Winget;
                receipt.Store = previous.Store;
            }
            Save(temporary, receipt);
            Directory.Move(temporary, output);
            return receipt;
        }
        finally
        {
            // This uniquely named directory contains only files created by this preparation attempt.
            if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
        }
    }

    public CatalogUpdateReceipt Read(string output, string profilePath, string releaseConfigPath)
    {
        var receipt = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(output, ReceiptName)),
            ReleaseCatalogJsonContext.Default.CatalogUpdateReceipt)
            ?? throw new InvalidOperationException("Missing catalog update receipt.");
        if (receipt.SchemaVersion != 1 || receipt.ProfileSha256 != Hash(profilePath) ||
            receipt.ReleaseConfigSha256 != Hash(releaseConfigPath) || receipt.Files.Count != 4 || receipt.Artifacts.Length == 0)
            throw new InvalidOperationException("Catalog receipt/configuration differs from the prepared release. Prepare new output.");
        foreach (var file in receipt.Files)
            if (!string.Equals(Hash(ContainedFile(output, file.Key)), file.Value, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A prepared catalog input changed. Prepare new output.");
        foreach (var channel in new[] { receipt.Winget, receipt.Store })
            if (channel.State is not ("Prepared" or "Reserved" or "Attempting" or "Submitted"))
                throw new InvalidOperationException("Unknown catalog receipt channel state.");
        return receipt;
    }

    private static string ContainedFile(string output, string relative)
    {
        var root = Path.GetFullPath(output).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (Path.IsPathRooted(relative) || !path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Catalog input must be a regular file beneath the prepared directory.");
        return path;
    }

    private static void RequireProfile(CatalogUpdateSpec profile)
    {
        if (profile.SchemaVersion != 1 || string.IsNullOrWhiteSpace(profile.ReleaseConfigPath))
            throw new InvalidOperationException("Catalog profile requires schema version 1 and ReleaseConfigPath.");
    }

    private static string Hash(string path) => DotNetPublishReleaseArtifactVerifier.ComputeSha256(path);

    private static void Save(string output, CatalogUpdateReceipt receipt)
    {
        var target = Path.Combine(output, ReceiptName);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, receipt, ReleaseCatalogJsonContext.Default.CatalogUpdateReceipt);
                stream.Flush(true);
            }
            if (File.Exists(target)) File.Replace(temporary, target, null);
            else File.Move(temporary, target);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task VerifyDownloadAsync(CatalogInstaller artifact, bool store, CancellationToken cancellationToken)
    {
        using var verifier = new CatalogDownloadVerifier();
        await verifier.VerifyAsync(artifact, store, cancellationToken).ConfigureAwait(false);
    }
}
