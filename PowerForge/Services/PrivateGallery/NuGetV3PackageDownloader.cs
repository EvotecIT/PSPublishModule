using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace PowerForge;

/// <summary>
/// Downloads NuGet packages from a NuGet V3 feed.
/// </summary>
public sealed class NuGetV3PackageDownloader
{
    private readonly HttpMessageHandler? _httpHandler;
    private readonly Dictionary<string, string> _packageBaseAddressCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cacheLock = new();

    /// <summary>
    /// Creates a new NuGet V3 package downloader.
    /// </summary>
    /// <param name="httpHandler">Optional HTTP handler used by tests.</param>
    public NuGetV3PackageDownloader(HttpMessageHandler? httpHandler = null)
    {
        _httpHandler = httpHandler;
    }

    /// <summary>
    /// Downloads a package to the specified file.
    /// </summary>
    /// <param name="serviceIndexUrl">NuGet V3 service index URL.</param>
    /// <param name="packageId">Package id.</param>
    /// <param name="version">Package version.</param>
    /// <param name="destinationPath">Destination nupkg path.</param>
    /// <param name="options">Private gallery options carrying timeout and authentication.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task DownloadPackageAsync(
        string serviceIndexUrl,
        string packageId,
        string version,
        string destinationPath,
        PrivateGalleryIndexOptions options,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceIndexUrl)) throw new ArgumentException("Service index URL is required.", nameof(serviceIndexUrl));
        if (string.IsNullOrWhiteSpace(packageId)) throw new ArgumentException("Package id is required.", nameof(packageId));
        if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("Version is required.", nameof(version));
        if (string.IsNullOrWhiteSpace(destinationPath)) throw new ArgumentException("Destination path is required.", nameof(destinationPath));
        if (options is null) throw new ArgumentNullException(nameof(options));

        using var http = PrivateGalleryHttp.CreateClient(options.RequestTimeoutSeconds, _httpHandler);
        var packageBase = await ResolvePackageBaseAddressCachedAsync(http, serviceIndexUrl, options, cancellationToken).ConfigureAwait(false);
        var lowerId = packageId.Trim().ToLowerInvariant();
        var lowerVersion = version.Trim().ToLowerInvariant();
        var packageUri = new Uri(new Uri(EnsureTrailingSlash(packageBase)), $"{Uri.EscapeDataString(lowerId)}/{Uri.EscapeDataString(lowerVersion)}/{Uri.EscapeDataString(lowerId)}.{Uri.EscapeDataString(lowerVersion)}.nupkg");

        using var request = new HttpRequestMessage(HttpMethod.Get, packageUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        PrivateGalleryHttp.ApplyAuthentication(request, options.Token, options.AuthenticationKind);
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"NuGet package download failed ({(int)response.StatusCode} {response.ReasonPhrase}) for {packageId} {version}.");

        var destinationDirectory = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
            Directory.CreateDirectory(destinationDirectory);

        using var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var destination = File.Create(destinationPath);
        await source.CopyToAsync(destination, 81920, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> ResolvePackageBaseAddressCachedAsync(
        HttpClient http,
        string serviceIndexUrl,
        PrivateGalleryIndexOptions options,
        CancellationToken cancellationToken)
    {
        lock (_cacheLock)
        {
            if (_packageBaseAddressCache.TryGetValue(serviceIndexUrl, out var cached))
                return cached;
        }

        var resolved = await ResolvePackageBaseAddressUncachedAsync(http, serviceIndexUrl, options, cancellationToken).ConfigureAwait(false);
        lock (_cacheLock)
        {
            _packageBaseAddressCache[serviceIndexUrl] = resolved;
        }

        return resolved;
    }

    private static async Task<string> ResolvePackageBaseAddressUncachedAsync(
        HttpClient http,
        string serviceIndexUrl,
        PrivateGalleryIndexOptions options,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, serviceIndexUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        PrivateGalleryHttp.ApplyAuthentication(request, options.Token, options.AuthenticationKind);
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"NuGet service index query failed ({(int)response.StatusCode} {response.ReasonPhrase}) for '{serviceIndexUrl}'.");

        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty("resources", out var resources) ||
            resources.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("NuGet service index did not include a resources array.");

        foreach (var resource in resources.EnumerateArray())
        {
            var type = resource.TryGetProperty("@type", out var typeElement) ? typeElement.GetString() : null;
            if (type is null ||
                string.IsNullOrWhiteSpace(type) ||
                !type.StartsWith("PackageBaseAddress/", StringComparison.OrdinalIgnoreCase))
                continue;

            var id = resource.TryGetProperty("@id", out var idElement) ? idElement.GetString() : null;
            if (!string.IsNullOrWhiteSpace(id))
                return id!;
        }

        throw new InvalidOperationException("NuGet service index did not expose PackageBaseAddress.");
    }

    private static string EnsureTrailingSlash(string value)
        => value.EndsWith("/", StringComparison.Ordinal) ? value : value + "/";
}
