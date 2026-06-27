using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace PowerForge;

public sealed partial class ManagedModuleRepositoryClient
{
    /// <summary>
    /// Publishes an existing package to a local folder or NuGet-compatible publish endpoint.
    /// </summary>
    /// <param name="repository">Repository to publish to.</param>
    /// <param name="packagePath">Package path to publish.</param>
    /// <param name="credential">Repository credential or API key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Package publish result.</returns>
    public async Task<ManagedModulePackagePublishResult> PublishPackageAsync(
        ManagedModuleRepository repository,
        string packagePath,
        RepositoryCredential? credential = null,
        CancellationToken cancellationToken = default)
    {
        if (repository is null)
            throw new ArgumentNullException(nameof(repository));
        if (string.IsNullOrWhiteSpace(packagePath))
            throw new ArgumentException("Package path is required.", nameof(packagePath));

        return repository.Kind switch
        {
            ManagedModuleRepositoryKind.LocalFolder => PublishLocalPackage(repository, packagePath),
            ManagedModuleRepositoryKind.NuGetV3 => await PublishNuGetPackageAsync(repository, packagePath, credential, cancellationToken).ConfigureAwait(false),
            _ => throw new NotSupportedException($"Repository kind '{repository.Kind}' is not supported.")
        };
    }

    private async Task<string> ResolvePackagePublishAddressAsync(
        ManagedModuleRepository repository,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        if (_packagePublishAddressCache.TryGetValue(repository.Source, out var cached))
            return cached;

        if (!repository.Source.EndsWith("index.json", StringComparison.OrdinalIgnoreCase))
        {
            _packagePublishAddressCache[repository.Source] = repository.Source;
            return repository.Source;
        }

        using var request = CreateRequest(HttpMethod.Get, new Uri(repository.Source), credential, "application/json");
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"NuGet service index query failed ({(int)response.StatusCode} {response.ReasonPhrase}) for '{repository.Source}'.");

        using var stream = await ReadContentStreamAsync(response.Content, cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("resources", out var resources) || resources.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Repository '{repository.Name}' service index did not include a resources array.");

        foreach (var resource in resources.EnumerateArray())
        {
            if (!IsPackagePublishResource(resource))
                continue;

            var id = resource.TryGetProperty("@id", out var idElement) ? idElement.GetString() : null;
            if (!string.IsNullOrWhiteSpace(id))
            {
                _packagePublishAddressCache[repository.Source] = id!;
                return id!;
            }
        }

        throw new InvalidOperationException($"Repository '{repository.Name}' service index did not expose PackagePublish.");
    }

    private async Task<ManagedModulePackagePublishResult> PublishNuGetPackageAsync(
        ManagedModuleRepository repository,
        string packagePath,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        var package = Path.GetFullPath(packagePath.Trim().Trim('"'));
        if (!File.Exists(package))
            throw new FileNotFoundException($"Package file was not found: {package}", package);

        var publishAddress = await ResolvePackagePublishAddressAsync(repository, credential, cancellationToken).ConfigureAwait(false);
        using var request = CreateRequest(HttpMethod.Put, new Uri(publishAddress), credential, "application/json");
        using var stream = File.OpenRead(package);
        request.Content = new StreamContent(stream);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Managed module package publish failed ({(int)response.StatusCode} {response.ReasonPhrase}) for '{package}'.");

        return new ManagedModulePackagePublishResult
        {
            PackagePath = package,
            PublishSource = publishAddress,
            StatusCode = (int)response.StatusCode,
            Published = true
        };
    }

    private static ManagedModulePackagePublishResult PublishLocalPackage(ManagedModuleRepository repository, string packagePath)
    {
        var package = Path.GetFullPath(packagePath.Trim().Trim('"'));
        if (!File.Exists(package))
            throw new FileNotFoundException($"Package file was not found: {package}", package);

        var destinationDirectory = ResolveLocalFolder(repository.Source);
        Directory.CreateDirectory(destinationDirectory);
        var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(package));
        if (!string.Equals(package, destinationPath, StringComparison.OrdinalIgnoreCase))
            File.Copy(package, destinationPath, overwrite: true);

        return new ManagedModulePackagePublishResult
        {
            PackagePath = package,
            PublishSource = destinationPath,
            Published = true
        };
    }

    private static bool IsPackagePublishResource(JsonElement resource)
    {
        if (!resource.TryGetProperty("@type", out var typeElement))
            return false;

        if (typeElement.ValueKind == JsonValueKind.String)
            return IsPackagePublishType(typeElement.GetString());

        return typeElement.ValueKind == JsonValueKind.Array &&
               typeElement.EnumerateArray().Any(type => type.ValueKind == JsonValueKind.String && IsPackagePublishType(type.GetString()));
    }

    private static bool IsPackagePublishType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return false;

        return type!.IndexOf("PackagePublish", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
