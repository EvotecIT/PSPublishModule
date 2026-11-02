using NuGet.Packaging;
using NuGet.Packaging.Signing;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>Stages and validates signed NuGet assets without changing their package contents.</summary>
public sealed partial class NuGetTrustedPublishingService
{
    /// <summary>Release asset name used by the trusted publisher.</summary>
    public const string ManifestFileName = "nuget-release-manifest.json";
    private readonly DotNetNuGetClient _nuget;
    private readonly DotNetRepositoryReleaseService _release;
    private readonly System.Net.Http.HttpClient _http;
    private readonly ILogger _logger;

    /// <summary>Creates the shared release service. The optional HTTP client remains caller-owned.</summary>
    public NuGetTrustedPublishingService(ILogger logger, System.Net.Http.HttpClient? httpClient = null)
        : this(logger, new DotNetNuGetClient(), new DotNetRepositoryReleaseService(logger), httpClient ?? RegistryClient)
    {
    }

    internal NuGetTrustedPublishingService(ILogger logger, DotNetNuGetClient nuget,
        DotNetRepositoryReleaseService release, System.Net.Http.HttpClient httpClient)
    {
        _logger = logger;
        _nuget = nuget;
        _release = release;
        _http = httpClient;
    }

    /// <summary>Verifies signed package outputs and writes a manifest beside the packages for draft-release upload.</summary>
    public async Task<string> StageAsync(DotNetRepositoryReleaseResult release, CancellationToken cancellationToken = default)
    {
        if (!release.Success) throw new InvalidOperationException("Staging requires a successful signed build.");
        ValidateCommit(release.SourceCommitSha);
        var paths = release.Projects.Where(p => p.IsPackable).SelectMany(p => p.Packages.Concat(p.SymbolPackages))
            .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length == 0) throw new InvalidOperationException("No NuGet packages were built.");
        var directory = Path.GetDirectoryName(paths[0])!;
        if (paths.Any(p => !string.Equals(Path.GetDirectoryName(p), directory, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("NuGet staging requires all packages in one OutputPath directory.");
        var assets = new List<NuGetReleaseAsset>();
        foreach (var path in paths)
        {
            EnsurePackageFile(path);
            await VerifyAsync(path, Array.Empty<string>(), cancellationToken).ConfigureAwait(false);
            assets.Add(await ReadAssetAsync(path, cancellationToken).ConfigureAwait(false));
        }
        var manifest = new NuGetReleaseManifest { SourceCommitSha = release.SourceCommitSha!, Assets = assets.ToArray() };
        ValidateManifest(manifest);
        var manifestPath = Path.Combine(directory, ManifestFileName);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, NuGetReleaseJsonContext.Default.NuGetReleaseManifest) + Environment.NewLine);
        return manifestPath;
    }

    /// <summary>Checks manifest identity, exact asset hashes and author signatures against an independent signer allow-list.</summary>
    public async Task<NuGetReleaseManifest> VerifyAsync(string manifestPath, string sourceCommitSha,
        string[] authorFingerprints, CancellationToken cancellationToken = default)
    {
        if (authorFingerprints is null || authorFingerprints.Length == 0 || authorFingerprints.Any(p => !IsSha256(p)))
            throw new InvalidOperationException("At least one SHA-256 author certificate fingerprint is required.");
        var path = Path.GetFullPath(manifestPath);
        var manifest = JsonSerializer.Deserialize(File.ReadAllText(path), NuGetReleaseJsonContext.Default.NuGetReleaseManifest)
            ?? throw new InvalidOperationException("Empty NuGet release manifest.");
        ValidateManifest(manifest);
        ValidateCommit(sourceCommitSha);
        if (!string.Equals(manifest.SourceCommitSha, sourceCommitSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Manifest source commit does not match the qualified release tag.");
        var directory = Path.GetDirectoryName(path)!;
        var declared = new HashSet<string>(manifest.Assets.Select(a => a.FileName), StringComparer.OrdinalIgnoreCase);
        if (Directory.EnumerateFiles(directory).Any(p => IsPackage(p) && !declared.Contains(Path.GetFileName(p))))
            throw new InvalidOperationException("Downloaded package assets are missing from the manifest.");
        foreach (var asset in manifest.Assets)
        {
            var package = Path.Combine(directory, asset.FileName);
            EnsurePackageFile(package);
            if (!string.Equals(HashFile(package), asset.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"SHA256 mismatch for '{asset.FileName}'.");
            if (!authorFingerprints.Contains(asset.AuthorCertificateFingerprint, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Manifest signer is not allowed for '{asset.FileName}'.");
            await VerifyAsync(package, new[] { asset.AuthorCertificateFingerprint }, cancellationToken).ConfigureAwait(false);
            var actual = await ReadAssetAsync(package, cancellationToken).ConfigureAwait(false);
            RequireIdentity(asset, actual);
        }
        return manifest;
    }

    private async Task VerifyAsync(string packagePath, string[] fingerprints, CancellationToken cancellationToken)
    {
        var verification = await _nuget.VerifyPackageAsync(packagePath, true, fingerprints, cancellationToken).ConfigureAwait(false);
        if (!verification.Succeeded)
            throw new InvalidOperationException($"NuGet signature verification failed for '{Path.GetFileName(packagePath)}': {verification.StdErr}");
    }

    internal static async Task<NuGetReleaseAsset> ReadAssetAsync(string path, CancellationToken cancellationToken)
    {
        using var reader = new PackageArchiveReader(path);
        var identity = reader.GetIdentity();
        var signature = await reader.GetPrimarySignatureAsync(cancellationToken).ConfigureAwait(false);
        if (signature is not AuthorPrimarySignature || signature.SignerInfo.Certificate is null)
            throw new InvalidOperationException($"Package '{Path.GetFileName(path)}' requires an author signature.");
        if (signature.Timestamps.Count == 0)
            throw new InvalidOperationException($"Package '{Path.GetFileName(path)}' requires a timestamped author signature.");
        using var sha = SHA256.Create();
        return new NuGetReleaseAsset
        {
            FileName = Path.GetFileName(path), Id = identity.Id, Version = identity.Version.ToNormalizedString(),
            Sha256 = HashFile(path),
            AuthorCertificateFingerprint = Hex(sha.ComputeHash(signature.SignerInfo.Certificate.RawData))
        };
    }

    private static void RequireIdentity(NuGetReleaseAsset expected, NuGetReleaseAsset actual)
    {
        if (!string.Equals(expected.Id, actual.Id, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(expected.Version, actual.Version, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(expected.AuthorCertificateFingerprint, actual.AuthorCertificateFingerprint, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Package identity or author signer conflicts with the manifest for '{expected.FileName}'.");
    }

    private static void ValidateManifest(NuGetReleaseManifest manifest)
    {
        if (manifest.SchemaVersion != 1 || manifest.Assets is null || manifest.Assets.Length == 0)
            throw new InvalidOperationException("Unsupported or empty NuGet release manifest.");
        ValidateCommit(manifest.SourceCommitSha);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in manifest.Assets)
        {
            if (asset is null || string.IsNullOrWhiteSpace(asset.FileName) || !IsPackage(asset.FileName) ||
                !Regex.IsMatch(asset.FileName, @"^[A-Za-z0-9][A-Za-z0-9._+-]*$", RegexOptions.CultureInvariant) ||
                !files.Add(asset.FileName) || !IsSha256(asset.Sha256) || !IsSha256(asset.AuthorCertificateFingerprint) ||
                !Regex.IsMatch(asset.Id ?? "", @"^[A-Za-z0-9_.-]{1,100}$", RegexOptions.CultureInvariant) ||
                !PackageVersionUtility.TryNormalizeExact(asset.Version, out var version) || version != asset.Version ||
                !identities.Add(asset.Id + "/" + asset.Version + "/" + Path.GetExtension(asset.FileName).ToLowerInvariant()))
                throw new InvalidOperationException("Invalid or duplicate NuGet manifest asset.");
        }
        foreach (var symbol in manifest.Assets.Where(a => a.FileName.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase)))
            if (!manifest.Assets.Any(a => a.FileName.Equals(Path.ChangeExtension(symbol.FileName, ".nupkg"), StringComparison.OrdinalIgnoreCase) &&
                a.Id.Equals(symbol.Id, StringComparison.OrdinalIgnoreCase) && a.Version == symbol.Version &&
                a.AuthorCertificateFingerprint.Equals(symbol.AuthorCertificateFingerprint, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("A symbol package requires its matching signed primary package.");
    }

    private static bool IsPackage(string path) => path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase);
    private static bool IsSha256(string? value) => value is not null && Regex.IsMatch(value, "^[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant);
    private static void ValidateCommit(string? value)
    {
        if (value is null || !Regex.IsMatch(value, "^[a-fA-F0-9]{40}$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("A full source commit SHA is required for NuGet staging and publication.");
    }
    private static void EnsurePackageFile(string path)
    {
        if (!IsPackage(path) || !File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException($"Missing or linked NuGet package '{path}'.");
    }
    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Hex(sha.ComputeHash(stream));
    }
    private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", string.Empty);
}
