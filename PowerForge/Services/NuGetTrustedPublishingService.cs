using System.Text.Json;
using static PowerForge.NuGetReleaseVerifier;

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
            .Select(Path.GetFullPath).Distinct(StringComparer.Ordinal).ToArray();
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
    public Task<NuGetReleaseManifest> VerifyAsync(string manifestPath, string sourceCommitSha,
        string[] authorFingerprints, CancellationToken cancellationToken = default)
        => _nuget.VerifyReleaseAsync(manifestPath, sourceCommitSha, authorFingerprints, cancellationToken);

    private async Task VerifyAsync(string packagePath, string[] fingerprints, CancellationToken cancellationToken)
    {
        var verification = await _nuget.VerifyPackageAsync(packagePath, true, fingerprints, cancellationToken).ConfigureAwait(false);
        if (!verification.Succeeded)
            throw new InvalidOperationException($"NuGet signature verification failed for '{Path.GetFileName(packagePath)}': {verification.StdErr}");
    }

}
