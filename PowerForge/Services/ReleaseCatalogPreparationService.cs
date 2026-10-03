using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>Prepares catalog files without rebuilding, signing, reserving versions, or publishing.</summary>
internal sealed class ReleaseCatalogPreparationService
{
    private readonly Func<string, DotNetPublishMsiPackageMetadata> _readMsi;
    private readonly Func<string, bool> _verifySignature;

    public ReleaseCatalogPreparationService(
        Func<string, DotNetPublishMsiPackageMetadata>? readMsi = null,
        Func<string, bool>? verifySignature = null)
    {
        _readMsi = readMsi ?? new MsiPackageMetadataReader().Read;
        _verifySignature = verifySignature ?? (path => DotNetPublishReleaseArtifactVerifier.VerifyAuthenticode(path).IsValid);
    }

    /// <summary>Verifies selected downloaded MSIs against release checksums, then writes WinGet and Store inputs.</summary>
    public ReleaseCatalogPreparationResult Prepare(
        PowerForgeReleaseSpec spec, string manifestPath, string checksumsPath, string assetRoot, string outputRoot)
    {
        var winget = spec.Winget ?? throw new InvalidOperationException("The release config must define Winget metadata.");
        if (!winget.Enabled || winget.Packages.Length != 1)
            throw new InvalidOperationException("Catalog preparation requires one enabled Winget package per configuration.");
        var package = winget.Packages[0];
        ValidatePackage(package);
        var root = Path.GetFullPath(assetRoot);
        var output = Path.GetFullPath(outputRoot);
        if (Directory.Exists(output) || File.Exists(output))
            throw new InvalidOperationException("Catalog output already exists. Choose a new output directory.");
        var checksums = ReadChecksums(checksumsPath);
        VerifyChecksum(manifestPath, checksums);
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        if (manifest.RootElement.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidOperationException("Unsupported release manifest schema version.");
        var assets = JsonSerializer.Deserialize(manifest.RootElement.GetProperty("assetEntries").GetRawText(),
            ReleaseCatalogJsonContext.Default.PowerForgeReleaseAssetEntryArray)
            ?? throw new InvalidOperationException("The release manifest has no asset entries.");
        var entries = new List<WingetManifestInstallerEntry>();
        var desktopPackages = new List<StoreSubmissionDesktopPackage>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var installer in package.Installers)
        {
            if (installer.Category != PowerForgeReleaseAssetCategory.Installer ||
                !string.Equals(installer.InstallerType, "msi", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Catalog preparation currently supports MSI installer selectors.");
            var original = PowerForgeReleaseService.ResolveWingetInstallerAsset(installer, package, assets);
            // Published manifests retain build-machine paths. Only a safe basename is used under the explicit download root.
            var fileName = FileName(original.RelativeStagePath ?? original.StagedPath ?? original.Path);
            if (!names.Add(fileName))
                throw new InvalidOperationException("Catalog installer selectors resolve duplicate filenames.");
            var localPath = Path.Combine(root, fileName);
            VerifyChecksum(localPath, checksums);
            if (!_verifySignature(localPath))
                throw new InvalidOperationException($"Installer Authenticode verification failed: {fileName}");
            var metadata = _readMsi(localPath);
            ValidateMetadata(metadata, original.Version, package);
            var localAsset = new PowerForgeReleaseAssetEntry
            {
                Path = localPath, StagedPath = localPath, Category = original.Category,
                Target = original.Target, Runtime = original.Runtime, Framework = original.Framework,
                Version = original.Version, Style = original.Style
            };
            var entry = PowerForgeReleaseService.ResolveWingetInstallerEntry(installer, winget, package,
                new[] { localAsset }, Array.Empty<PowerForgeToolGitHubReleaseResult>());
            if (!Uri.TryCreate(entry.InstallerUrl, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) ||
                entry.InstallerUrl.Contains("{"))
                throw new InvalidOperationException("Catalog installer URLs must be absolute HTTPS URLs with all template tokens resolved.");
            if (entry.Architecture is not ("x64" or "arm64" or "x86"))
                throw new InvalidOperationException("Unsupported catalog installer architecture.");
            if (!string.Equals(metadata.Architecture, entry.Architecture, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("MSI architecture does not match the catalog installer selector.");
            entry.MsiMetadata = metadata;
            entry.Scope = metadata.Scope;
            entries.Add(entry);
            desktopPackages.Add(new StoreSubmissionDesktopPackage
            {
                PackageUrl = entry.InstallerUrl,
                Languages = new[] { package.PackageLocale ?? winget.PackageLocale ?? "en-US" },
                Architectures = new[] { entry.Architecture == "arm64" ? "Arm64" : entry.Architecture.ToUpperInvariant() },
                IsSilentInstall = false, InstallerParameters = "/qn /norestart", PackageType = "msi"
            });
        }
        if (entries.Count == 0)
            throw new InvalidOperationException("The Winget package must select at least one installer.");
        var version = PowerForgeReleaseService.ResolveWingetPackageVersion(package, entries);
        var locale = package.PackageLocale ?? winget.PackageLocale ?? "en-US";
        if (!PowerForgeReleaseService.IsSafeWingetManifestPathSegment(version) ||
            !Regex.IsMatch(locale, "^[A-Za-z0-9-]+$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Invalid catalog version or locale path segment.");
        var directory = Path.Combine(output, "Winget", package.PackageIdentifier, version);
        var rendered = new Dictionary<string, string>
        {
            [Path.Combine(directory, package.PackageIdentifier + ".installer.yaml")] = WingetManifestWriter.Build(winget, package, version, entries),
            [Path.Combine(directory, package.PackageIdentifier + ".yaml")] = WingetManifestWriter.BuildVersion(winget, package, version),
            [Path.Combine(directory, package.PackageIdentifier + ".locale." + locale + ".yaml")] = WingetManifestWriter.BuildDefaultLocale(winget, package, version)
        };
        var desktopPath = Path.Combine(output, "desktop-packages.json");
        var desktopJson = JsonSerializer.Serialize(desktopPackages.ToArray(), ReleaseCatalogJsonContext.Default.StoreSubmissionDesktopPackageArray);
        // Complete validation and rendering before exposing any submission files.
        Directory.CreateDirectory(directory);
        foreach (var file in rendered)
            File.WriteAllText(file.Key, file.Value, new UTF8Encoding(false));
        File.WriteAllText(desktopPath, desktopJson, new UTF8Encoding(false));
        return new ReleaseCatalogPreparationResult
        {
            WingetManifestPaths = rendered.Keys.ToArray(), DesktopPackagesPath = desktopPath,
            Installers = entries.Select(entry => entry.MsiMetadata!).ToArray()
        };
    }

    private static void ValidatePackage(PowerForgeReleaseWingetPackage package)
    {
        if (!PowerForgeReleaseService.IsValidWingetPackageIdentifier(package.PackageIdentifier) ||
            string.IsNullOrWhiteSpace(package.Publisher) || string.IsNullOrWhiteSpace(package.PackageName) ||
            string.IsNullOrWhiteSpace(package.License) || string.IsNullOrWhiteSpace(package.ShortDescription))
            throw new InvalidOperationException("Complete Winget package identity and description are required.");
    }

    private static void ValidateMetadata(DotNetPublishMsiPackageMetadata metadata, string? version, PowerForgeReleaseWingetPackage package)
    {
        if (!string.IsNullOrEmpty(metadata.ReadError) || !Guid.TryParse(metadata.ProductCode, out _) ||
            !Guid.TryParse(metadata.UpgradeCode, out _) || string.IsNullOrWhiteSpace(metadata.Manufacturer) ||
            !string.Equals(metadata.ProductName, package.PackageName, StringComparison.Ordinal) ||
            !string.Equals(metadata.ProductVersion, version, StringComparison.Ordinal))
            throw new InvalidOperationException("MSI identity or version does not match the catalog configuration and release manifest.");
    }

    private static Dictionary<string, string> ReadChecksums(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadAllLines(path).Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            var match = Regex.Match(line, "^([A-Fa-f0-9]{64}) [ *](.+)$", RegexOptions.CultureInvariant);
            if (!match.Success)
                throw new InvalidOperationException("Malformed release checksum line.");
            var name = FileName(match.Groups[2].Value);
            if (result.ContainsKey(name))
                throw new InvalidOperationException("Ambiguous duplicate release checksum filename: " + name);
            result.Add(name, match.Groups[1].Value);
        }
        return result;
    }

    private static void VerifyChecksum(string path, IReadOnlyDictionary<string, string> checksums)
    {
        if (!checksums.TryGetValue(Path.GetFileName(path), out var expected) ||
            !string.Equals(expected, DotNetPublishReleaseArtifactVerifier.ComputeSha256(path), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Missing or mismatched release checksum: " + Path.GetFileName(path));
    }

    private static string FileName(string value)
    {
        var name = value.Replace('\\', '/').Split('/').Last();
        if (!PowerForgeReleaseService.IsSafeWingetManifestPathSegment(name) || name is "." or "..")
            throw new InvalidOperationException("Unsafe release asset filename.");
        return name;
    }
}
