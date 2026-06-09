using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace PowerForge;

/// <summary>
/// Shared host service for verifying published GitHub, NuGet, and PowerShell repository targets.
/// </summary>
public sealed class PublishVerificationHostService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly PowerShellRepositoryResolver _powerShellRepositoryResolver;
    private readonly ModuleManifestMetadataReader _moduleManifestMetadataReader;
    private readonly bool _ownsHttpClient;

    /// <summary>
    /// Creates a new verification host service with a default <see cref="HttpClient"/>.
    /// </summary>
    public PublishVerificationHostService()
        : this(
            new HttpClient(new HttpClientHandler {
                AllowAutoRedirect = true
            }) {
                Timeout = TimeSpan.FromSeconds(20)
            },
            new PowerShellRepositoryResolver(),
            new ModuleManifestMetadataReader(),
            ownsHttpClient: true)
    {
    }

    /// <summary>
    /// Creates a new verification host service using the provided HTTP client and repository resolver.
    /// </summary>
    public PublishVerificationHostService(
        HttpClient httpClient,
        PowerShellRepositoryResolver powerShellRepositoryResolver)
        : this(httpClient, powerShellRepositoryResolver, new ModuleManifestMetadataReader(), ownsHttpClient: false)
    {
    }

    internal PublishVerificationHostService(
        HttpClient httpClient,
        PowerShellRepositoryResolver powerShellRepositoryResolver,
        ModuleManifestMetadataReader moduleManifestMetadataReader)
        : this(httpClient, powerShellRepositoryResolver, moduleManifestMetadataReader, ownsHttpClient: false)
    {
    }

    internal PublishVerificationHostService(
        HttpClient httpClient,
        PowerShellRepositoryResolver powerShellRepositoryResolver,
        ModuleManifestMetadataReader moduleManifestMetadataReader,
        bool ownsHttpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _powerShellRepositoryResolver = powerShellRepositoryResolver ?? throw new ArgumentNullException(nameof(powerShellRepositoryResolver));
        _moduleManifestMetadataReader = moduleManifestMetadataReader ?? throw new ArgumentNullException(nameof(moduleManifestMetadataReader));
        _ownsHttpClient = ownsHttpClient;

        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PowerForgeStudio/0.1");
        }
    }

    /// <summary>
    /// Verifies a previously published target.
    /// </summary>
    public Task<PublishVerificationResult> VerifyAsync(PublishVerificationRequest request, CancellationToken cancellationToken = default)
    {
        FrameworkCompatibility.NotNull(request, nameof(request));

        return request.TargetKind switch
        {
            "GitHub" => VerifyGitHubAsync(request, cancellationToken),
            "NuGet" => VerifyNuGetAsync(request, cancellationToken),
            "PowerShellRepository" => VerifyPowerShellRepositoryAsync(request, cancellationToken),
            _ => Task.FromResult(new PublishVerificationResult {
                Status = PublishVerificationStatus.Skipped,
                Summary = $"Verification is not implemented for {request.TargetKind} targets yet."
            })
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task<PublishVerificationResult> VerifyGitHubAsync(PublishVerificationRequest request, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(request.Destination, UriKind.Absolute, out var uri))
        {
            return Failed("GitHub destination URL was not recorded.");
        }

        var response = await SendProbeAsync(uri, cancellationToken).ConfigureAwait(false);
        if (!response.Succeeded)
        {
            return Failed("GitHub release probe did not return a success status.");
        }

        var statusCode = response.StatusCode.GetValueOrDefault();
        return Verified($"GitHub release probe succeeded with {(int)statusCode} {statusCode}.");
    }

    private async Task<PublishVerificationResult> VerifyNuGetAsync(PublishVerificationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SourcePath) || !File.Exists(request.SourcePath))
        {
            return Failed("NuGet package path is missing or no longer exists locally.");
        }

        if (string.IsNullOrWhiteSpace(request.Destination))
        {
            return Skipped("NuGet destination URL was not recorded, so remote verification was skipped.");
        }

        var sourcePath = request.SourcePath!;
        var identity = TryReadPackageIdentity(sourcePath);
        if (identity is null)
        {
            return Failed("NuGet package identity could not be read from the .nupkg.");
        }

        var destination = request.Destination!;
        var probeUri = await ResolveNuGetPackageProbeUriAsync(destination, identity, cancellationToken).ConfigureAwait(false);
        if (probeUri is null)
        {
            return Skipped($"PowerForgeStudio could not derive a probeable package endpoint from {request.Destination}.");
        }

        var response = await SendProbeAsync(probeUri, cancellationToken).ConfigureAwait(false);
        if (!response.Succeeded)
        {
            return Failed($"Package probe failed for {identity.Id} {identity.Version} against {probeUri.Host}.");
        }

        return Verified($"Package probe succeeded for {identity.Id} {identity.Version} against {probeUri.Host}.");
    }

    private async Task<PublishVerificationResult> VerifyPowerShellRepositoryAsync(PublishVerificationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SourcePath) || !Directory.Exists(request.SourcePath))
        {
            return Failed("Module package path is missing or no longer exists locally.");
        }

        var manifestPath = Directory.EnumerateFiles(request.SourcePath, "*.psd1", SearchOption.AllDirectories)
            .FirstOrDefault(path => !path.Contains($"{Path.DirectorySeparatorChar}en-US{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            return Failed("Module manifest was not found for PSGallery verification.");
        }

        ModuleManifestMetadata metadata;
        try
        {
            metadata = _moduleManifestMetadataReader.Read(manifestPath);
        }
        catch
        {
            return Failed("Module manifest could not be read for PSGallery verification.");
        }

        var moduleVersion = string.IsNullOrWhiteSpace(metadata.PreRelease)
            ? metadata.ModuleVersion
            : $"{metadata.ModuleVersion}-{metadata.PreRelease}";
        var destination = request.Destination ?? "PSGallery";
        if (destination.Equals("PSGallery", StringComparison.OrdinalIgnoreCase))
        {
            var url = new Uri($"https://www.powershellgallery.com/packages/{metadata.ModuleName}/{moduleVersion}");
            var galleryResponse = await SendProbeAsync(url, cancellationToken).ConfigureAwait(false);
            if (!galleryResponse.Succeeded)
            {
                return Failed($"PSGallery probe failed for {metadata.ModuleName} {moduleVersion}.");
            }

            return Verified($"PSGallery probe succeeded for {metadata.ModuleName} {moduleVersion}.");
        }

        var resolvedRepository = await _powerShellRepositoryResolver.ResolveAsync(request.RootPath, destination, cancellationToken).ConfigureAwait(false);
        if (resolvedRepository is null)
        {
            return Failed($"PowerShell repository '{destination}' could not be resolved to a probeable endpoint.");
        }

        var probeUri = await ResolveNuGetPackageProbeUriAsync(
            resolvedRepository.SourceUri ?? resolvedRepository.PublishUri ?? destination,
            new NuGetPackageIdentity(metadata.ModuleName, moduleVersion),
            cancellationToken).ConfigureAwait(false);
        if (probeUri is null)
        {
            return Skipped($"PowerForgeStudio could not derive a probeable package endpoint from {resolvedRepository.DisplaySource}.");
        }

        var probeResponse = await SendProbeAsync(probeUri, cancellationToken).ConfigureAwait(false);
        if (!probeResponse.Succeeded)
        {
            return Failed($"Repository probe failed for {metadata.ModuleName} {moduleVersion} against {probeUri.Host}.");
        }

        return Verified($"Repository probe succeeded for {metadata.ModuleName} {moduleVersion} against {probeUri.Host}.");
    }

    private async Task<Uri?> ResolveNuGetPackageProbeUriAsync(string destination, NuGetPackageIdentity identity, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(destination, UriKind.Absolute, out var destinationUri))
        {
            return null;
        }

        if (destinationUri.Host.Contains("nuget.org", StringComparison.OrdinalIgnoreCase))
        {
            return BuildFlatContainerPackageUri(new Uri("https://api.nuget.org/v3-flatcontainer/"), identity);
        }

        if (destinationUri.AbsolutePath.Contains("/v3-flatcontainer", StringComparison.OrdinalIgnoreCase) ||
            destinationUri.AbsolutePath.Contains("/flatcontainer", StringComparison.OrdinalIgnoreCase))
        {
            return BuildFlatContainerPackageUri(destinationUri, identity);
        }

        if (destinationUri.AbsolutePath.EndsWith("/index.json", StringComparison.OrdinalIgnoreCase) ||
            destinationUri.AbsolutePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            var packageBaseUri = await ResolvePackageBaseAddressAsync(destinationUri, cancellationToken).ConfigureAwait(false);
            return packageBaseUri is null ? null : BuildFlatContainerPackageUri(packageBaseUri, identity);
        }

        return null;
    }

    private async Task<Uri?> ResolvePackageBaseAddressAsync(Uri serviceIndexUri, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, serviceIndexUri);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode >= 400)
            {
                return null;
            }

            using var stream = await FrameworkCompatibility.ReadAsStreamAsync(response.Content, cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!document.RootElement.TryGetProperty("resources", out var resources) || resources.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var resource in resources.EnumerateArray())
            {
                if (!resource.TryGetProperty("@type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var type = typeElement.GetString();
                if (string.IsNullOrWhiteSpace(type) ||
                    !type!.StartsWith("PackageBaseAddress/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!resource.TryGetProperty("@id", out var idElement) || idElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var id = idElement.GetString();
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                if (Uri.TryCreate(serviceIndexUri, id, out var resolved))
                {
                    return resolved;
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private async Task<ProbeResponse> SendProbeAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var headRequest = new HttpRequestMessage(HttpMethod.Head, uri);
        try
        {
            using var headResponse = await _httpClient.SendAsync(headRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if ((int)headResponse.StatusCode < 400)
            {
                return new ProbeResponse(true, headResponse.StatusCode);
            }
        }
        catch
        {
            // Fall back to GET.
        }

        using var getRequest = new HttpRequestMessage(HttpMethod.Get, uri);
        try
        {
            using var getResponse = await _httpClient.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            return (int)getResponse.StatusCode < 400
                ? new ProbeResponse(true, getResponse.StatusCode)
                : ProbeResponse.Failed;
        }
        catch
        {
            return ProbeResponse.Failed;
        }
    }

    private static Uri BuildFlatContainerPackageUri(Uri baseUri, NuGetPackageIdentity identity)
    {
        var builder = new StringBuilder(baseUri.AbsoluteUri.TrimEnd('/'));
        builder.Append('/');
        builder.Append(Uri.EscapeDataString(identity.Id.ToLowerInvariant()));
        builder.Append('/');
        builder.Append(Uri.EscapeDataString(identity.Version.ToLowerInvariant()));
        builder.Append('/');
        builder.Append(Uri.EscapeDataString(identity.Id.ToLowerInvariant()));
        builder.Append('.');
        builder.Append(Uri.EscapeDataString(identity.Version.ToLowerInvariant()));
        builder.Append(".nupkg");
        return new Uri(builder.ToString(), UriKind.Absolute);
    }

    private static NuGetPackageIdentity? TryReadPackageIdentity(string packagePath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            var nuspecEntry = archive.Entries.FirstOrDefault(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
            if (nuspecEntry is null)
            {
                return null;
            }

            using var stream = nuspecEntry.Open();
            using var reader = new StreamReader(stream);
            var xml = System.Xml.Linq.XDocument.Load(reader);
            var metadata = xml.Root?.Elements().FirstOrDefault(element => element.Name.LocalName.Equals("metadata", StringComparison.OrdinalIgnoreCase));
            var id = metadata?.Elements().FirstOrDefault(element => element.Name.LocalName.Equals("id", StringComparison.OrdinalIgnoreCase))?.Value;
            var version = metadata?.Elements().FirstOrDefault(element => element.Name.LocalName.Equals("version", StringComparison.OrdinalIgnoreCase))?.Value;
            var packageId = id;
            var packageVersion = version;
            return string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(packageVersion)
                ? null
                : new NuGetPackageIdentity(packageId!.Trim(), packageVersion!.Trim());
        }
        catch
        {
            return null;
        }
    }

    private static PublishVerificationResult Verified(string summary)
        => new() { Status = PublishVerificationStatus.Verified, Summary = summary };

    private static PublishVerificationResult Failed(string summary)
        => new() { Status = PublishVerificationStatus.Failed, Summary = summary };

    private static PublishVerificationResult Skipped(string summary)
        => new() { Status = PublishVerificationStatus.Skipped, Summary = summary };

    private sealed class NuGetPackageIdentity
    {
        public NuGetPackageIdentity(string id, string version)
        {
            Id = id;
            Version = version;
        }

        public string Id { get; }

        public string Version { get; }
    }

    private struct ProbeResponse
    {
        public ProbeResponse(bool succeeded, HttpStatusCode? statusCode)
        {
            Succeeded = succeeded;
            StatusCode = statusCode;
        }

        public bool Succeeded { get; }

        public HttpStatusCode? StatusCode { get; }

        public static ProbeResponse Failed => new(false, null);
    }
}
