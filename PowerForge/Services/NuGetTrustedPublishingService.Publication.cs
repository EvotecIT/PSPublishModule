using System.Net;
using System.Net.Http;

namespace PowerForge;

public sealed partial class NuGetTrustedPublishingService
{
    private const string NuGetSource = "https://api.nuget.org/v3/index.json";
    private static readonly HttpClient RegistryClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>
    /// Verifies and publishes downloaded release files to nuget.org, then reads back their author identities.
    /// Existing versions succeed only after independent identity and signer verification; repository signatures may change bytes.
    /// </summary>
    /// <param name="manifestPath">Downloaded release manifest beside its package assets.</param>
    /// <param name="sourceCommitSha">Commit qualified by the release workflow.</param>
    /// <param name="authorFingerprints">Independent author certificate SHA-256 allow-list.</param>
    /// <param name="publishApiKeyEnvName">Environment variable holding the short-lived NuGet/login key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task PublishAsync(string manifestPath, string sourceCommitSha, string[] authorFingerprints,
        string publishApiKeyEnvName = "NUGET_API_KEY", CancellationToken cancellationToken = default)
    {
        var manifest = await VerifyAsync(manifestPath, sourceCommitSha, authorFingerprints, cancellationToken).ConfigureAwait(false);
        var originalDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        var snapshotRelease = new DotNetRepositoryReleaseResult
        {
            Success = true, SourceCommitSha = sourceCommitSha,
            Projects = new List<DotNetRepositoryProjectResult> { new()
            {
                IsPackable = true,
                Packages = manifest.Assets.Where(a => a.FileName.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
                    .Select(a => Path.Combine(originalDirectory, a.FileName)).ToList(),
                SymbolPackages = manifest.Assets.Where(a => a.FileName.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
                    .Select(a => Path.Combine(originalDirectory, a.FileName)).ToList()
            } }
        };
        using var snapshot = ModulePackagePublicationSnapshot.Create(snapshotRelease);
        var directory = Path.GetDirectoryName(snapshot.Release.Projects[0].Packages[0])!;
        var snapshotManifest = Path.Combine(directory, ManifestFileName);
        File.WriteAllText(snapshotManifest, System.Text.Json.JsonSerializer.Serialize(manifest, NuGetReleaseJsonContext.Default.NuGetReleaseManifest));
        await VerifyAsync(snapshotManifest, sourceCommitSha, authorFingerprints, cancellationToken).ConfigureAwait(false);
        var scratch = Path.Combine(Path.GetTempPath(), "powerforge-nuget-readback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var primaries = manifest.Assets.Where(a => a.FileName.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)).ToArray();
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Check the entire release before the first upload, including conflicts in a later package.
            foreach (var asset in primaries)
                if (await VerifyPublishedAsync(asset, scratch, cancellationToken).ConfigureAwait(false)) existing.Add(asset.FileName);

            var missing = primaries.Where(a => !existing.Contains(a.FileName)).ToArray();
            var symbols = manifest.Assets.Where(a => a.FileName.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (missing.Length == 0 && symbols.Length == 0)
            {
                _logger.Success("All NuGet versions already have the manifest author identity; no upload needed.");
                return;
            }
            var feed = ProjectBuildPackageFeedResolver.Resolve(new ProjectBuildConfiguration
            {
                PublishApiKeyEnvName = publishApiKeyEnvName, PublishSource = NuGetSource
            }, directory, publishNuget: true, publishGitHub: false);
            if (string.IsNullOrWhiteSpace(feed.PublishApiKey))
                throw new InvalidOperationException($"No short-lived NuGet key found in '{publishApiKeyEnvName}'.");

            if (missing.Length > 0)
            {
                var checkpoint = new DotNetRepositoryReleaseResult { Success = true, SourceCommitSha = sourceCommitSha };
                foreach (var asset in missing)
                {
                    var project = new DotNetRepositoryProjectResult
                    {
                        ProjectName = asset.Id, PackageId = asset.Id, NewVersion = asset.Version, IsPackable = true
                    };
                    project.Packages.Add(Path.Combine(directory, asset.FileName));
                    // The existing publisher uploads a companion .snupkg from the same directory.
                    foreach (var symbol in symbols.Where(s => s.Id.Equals(asset.Id, StringComparison.OrdinalIgnoreCase) && s.Version == asset.Version))
                        project.SymbolPackages.Add(Path.Combine(directory, symbol.FileName));
                    checkpoint.Projects.Add(project);
                }
                var policy = new DotNetRepositoryReleaseSpec
                {
                    RootPath = directory, OutputPath = directory, PublishSource = NuGetSource,
                    PublishApiKey = feed.PublishApiKey, IncludeSymbols = symbols.Length > 0,
                    SkipDuplicate = true, PublishFailFast = true,
                    RemotePublishAttempted = snapshot.ValidateUnchanged
                };
                var publication = _release.PublishExistingPackages(policy, checkpoint, cancellationToken: cancellationToken);
                if (!publication.Success)
                    throw new InvalidOperationException(publication.ErrorMessage ?? "NuGet publication failed; inspect the registry before retrying.");
            }
            // A previous primary upload may have succeeded before its symbols failed. Resume those exact symbols.
            foreach (var symbol in symbols.Where(s => existing.Contains(Path.ChangeExtension(s.FileName, ".nupkg"))))
            {
                snapshot.ValidateUnchanged();
                var push = await _nuget.PushPackageAsync(new DotNetNuGetPushRequest(
                    Path.Combine(directory, symbol.FileName), feed.PublishApiKey!, NuGetSource, skipDuplicate: true), cancellationToken).ConfigureAwait(false);
                if (!push.Succeeded) throw new InvalidOperationException($"Symbol publication failed for '{symbol.FileName}'.");
            }
            foreach (var asset in missing)
            {
                var deadline = DateTime.UtcNow.AddMinutes(5);
                var delay = TimeSpan.FromSeconds(2);
                while (!await VerifyPublishedAsync(asset, scratch, cancellationToken).ConfigureAwait(false))
                {
                    if (DateTime.UtcNow >= deadline)
                        throw new InvalidOperationException($"NuGet accepted '{asset.Id} {asset.Version}', but registry readback is pending. Retry this release without rebuilding.");
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    delay = TimeSpan.FromSeconds(Math.Min(30, delay.TotalSeconds * 2));
                }
            }
            snapshot.ValidateUnchanged();
            _logger.Success("NuGet registry readback verified every package id, version and author signer.");
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    private async Task<bool> VerifyPublishedAsync(NuGetReleaseAsset asset, string scratch, CancellationToken cancellationToken)
    {
        var id = Uri.EscapeDataString(asset.Id.ToLowerInvariant());
        var version = Uri.EscapeDataString(asset.Version.ToLowerInvariant());
        var url = $"https://api.nuget.org/v3-flatcontainer/{id}/{version}/{id}.{version}.nupkg";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        response.EnsureSuccessStatusCode();
        var path = Path.Combine(scratch, asset.FileName);
        using (var output = File.Create(path))
            await response.Content.CopyToAsync(output).ConfigureAwait(false);
        await VerifyAsync(path, new[] { asset.AuthorCertificateFingerprint }, cancellationToken).ConfigureAwait(false);
        var actual = await ReadAssetAsync(path, cancellationToken).ConfigureAwait(false);
        RequireIdentity(asset, actual);
        // SHA256 deliberately is not compared: nuget.org adds a repository countersignature.
        return true;
    }
}
