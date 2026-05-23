using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PowerForge;

/// <summary>
/// Reads package inventory from an Azure Artifacts feed.
/// </summary>
public sealed class AzureArtifactsPrivateGalleryClient
{
    private const string ApiVersion = "7.1";
    private readonly HttpMessageHandler? _httpHandler;

    /// <summary>
    /// Creates a new Azure Artifacts private gallery client.
    /// </summary>
    /// <param name="httpHandler">Optional HTTP handler used by tests.</param>
    public AzureArtifactsPrivateGalleryClient(HttpMessageHandler? httpHandler = null)
    {
        _httpHandler = httpHandler;
    }

    /// <summary>
    /// Gets package inventory from Azure Artifacts.
    /// </summary>
    /// <param name="options">Indexing options.</param>
    /// <param name="warnings">Warning collection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Indexed packages.</returns>
    public async Task<List<PrivateGalleryPackage>> GetPackagesAsync(
        PrivateGalleryIndexOptions options,
        List<string> warnings,
        CancellationToken cancellationToken = default)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (warnings is null) throw new ArgumentNullException(nameof(warnings));

        var organization = NormalizeRequired(options.Organization, nameof(options.Organization));
        var feed = NormalizeRequired(options.Feed, nameof(options.Feed));
        var project = NormalizeOptional(options.Project);
        var maxPackages = options.MaxPackages > 0 ? options.MaxPackages : 500;
        var packages = new Dictionary<string, PrivateGalleryPackage>(StringComparer.OrdinalIgnoreCase);
        const int pageSize = 100;
        var releaseFilters = options.IncludeAllVersions
            ? new bool?[] { true, false }
            : new bool?[] { null };

        using var http = PrivateGalleryHttp.CreateClient(options.RequestTimeoutSeconds, _httpHandler);
        foreach (var releaseFilter in releaseFilters)
        {
            var skip = 0;
            while (packages.Count < maxPackages)
            {
                var take = Math.Min(pageSize, maxPackages - packages.Count);
                var uri = BuildPackagesUri(
                    organization,
                    project,
                    feed,
                    includeAllVersions: options.IncludeAllVersions,
                    includeDescription: true,
                    isRelease: releaseFilter,
                    top: take,
                    skip: skip);

                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                PrivateGalleryHttp.ApplyAuthentication(request, options.Token, options.AuthenticationKind);

                using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    var detail = await ReadErrorAsync(response).ConfigureAwait(false);
                    throw new InvalidOperationException(
                        $"Azure Artifacts package query failed ({(int)response.StatusCode} {response.ReasonPhrase}) for feed '{feed}'.{detail}");
                }

                using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var document = JsonDocument.Parse(stream);
                if (!TryGetArray(document.RootElement, "value", out var value))
                {
                    warnings.Add("Azure Artifacts package response did not include a 'value' array.");
                    break;
                }

                var fetched = 0;
                foreach (var element in value.EnumerateArray())
                {
                    var package = ParsePackage(element, warnings);
                    if (package is null)
                        continue;

                    if (packages.TryGetValue(package.Id, out var existing))
                        MergePackage(existing, package);
                    else
                        packages[package.Id] = package;

                    fetched++;
                    if (packages.Count >= maxPackages)
                        break;
                }

                if (fetched < take)
                    break;

                skip += fetched;
            }

            if (packages.Count >= maxPackages)
                break;
        }

        return packages.Values
            .OrderBy(package => package.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Gets package-level metrics from Azure Artifacts.
    /// </summary>
    /// <param name="options">Indexing options.</param>
    /// <param name="packageIds">Package ids to query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Metrics keyed by package id.</returns>
    public async Task<Dictionary<string, PrivateGalleryPackageMetrics>> GetPackageMetricsAsync(
        PrivateGalleryIndexOptions options,
        IEnumerable<string> packageIds,
        CancellationToken cancellationToken = default)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (packageIds is null) throw new ArgumentNullException(nameof(packageIds));

        var ids = packageIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (ids.Length == 0)
            return new Dictionary<string, PrivateGalleryPackageMetrics>(StringComparer.OrdinalIgnoreCase);

        var organization = NormalizeRequired(options.Organization, nameof(options.Organization));
        var feed = NormalizeRequired(options.Feed, nameof(options.Feed));
        var project = NormalizeOptional(options.Project);
        var uri = BuildPackageMetricsUri(organization, project, feed);
        using var http = PrivateGalleryHttp.CreateClient(options.RequestTimeoutSeconds, _httpHandler);
        using var response = await SendJsonPostAsync(http, uri, new { packageIds = ids }, options, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Azure Artifacts package metrics query failed ({(int)response.StatusCode} {response.ReasonPhrase}) for feed '{feed}'.");

        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var document = JsonDocument.Parse(stream);
        return ParseMetricsArray(document.RootElement, "packageId");
    }

    /// <summary>
    /// Gets version-level metrics from Azure Artifacts.
    /// </summary>
    /// <param name="options">Indexing options.</param>
    /// <param name="packageId">Package id.</param>
    /// <param name="packageVersionIds">Package version ids to query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Metrics keyed by package version id.</returns>
    public async Task<Dictionary<string, PrivateGalleryPackageMetrics>> GetPackageVersionMetricsAsync(
        PrivateGalleryIndexOptions options,
        string packageId,
        IEnumerable<string> packageVersionIds,
        CancellationToken cancellationToken = default)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(packageId)) throw new ArgumentException("Package id is required.", nameof(packageId));
        if (packageVersionIds is null) throw new ArgumentNullException(nameof(packageVersionIds));

        var ids = packageVersionIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (ids.Length == 0)
            return new Dictionary<string, PrivateGalleryPackageMetrics>(StringComparer.OrdinalIgnoreCase);

        var organization = NormalizeRequired(options.Organization, nameof(options.Organization));
        var feed = NormalizeRequired(options.Feed, nameof(options.Feed));
        var project = NormalizeOptional(options.Project);
        var uri = BuildPackageVersionMetricsUri(organization, project, feed, packageId.Trim());
        using var http = PrivateGalleryHttp.CreateClient(options.RequestTimeoutSeconds, _httpHandler);
        using var response = await SendJsonPostAsync(http, uri, new { packageVersionIds = ids }, options, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Azure Artifacts package version metrics query failed ({(int)response.StatusCode} {response.ReasonPhrase}) for package '{packageId}'.");

        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var document = JsonDocument.Parse(stream);
        return ParseMetricsArray(document.RootElement, "packageVersionId");
    }

    private static Uri BuildPackagesUri(
        string organization,
        string? project,
        string feed,
        bool includeAllVersions,
        bool includeDescription,
        bool? isRelease,
        int top,
        int skip)
    {
        var projectSegment = string.IsNullOrWhiteSpace(project)
            ? string.Empty
            : "/" + Uri.EscapeDataString(project!);
        var builder = new UriBuilder($"https://feeds.dev.azure.com/{Uri.EscapeDataString(organization)}{projectSegment}/_apis/packaging/Feeds/{Uri.EscapeDataString(feed)}/packages");
        var query = new List<string>
        {
            "protocolType=NuGet",
            "includeUrls=true",
            "includeAllVersions=" + includeAllVersions.ToString(CultureInfo.InvariantCulture).ToLowerInvariant(),
            "includeDescription=" + includeDescription.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()
        };
        if (isRelease.HasValue)
            query.Add("isRelease=" + isRelease.Value.ToString(CultureInfo.InvariantCulture).ToLowerInvariant());

        query.Add("$top=" + top.ToString(CultureInfo.InvariantCulture));
        query.Add("$skip=" + skip.ToString(CultureInfo.InvariantCulture));
        query.Add("api-version=" + ApiVersion);

        builder.Query = string.Join("&", query);
        return builder.Uri;
    }

    private static Uri BuildPackageMetricsUri(string organization, string? project, string feed)
    {
        var projectSegment = string.IsNullOrWhiteSpace(project)
            ? string.Empty
            : "/" + Uri.EscapeDataString(project!);
        return new Uri($"https://feeds.dev.azure.com/{Uri.EscapeDataString(organization)}{projectSegment}/_apis/packaging/Feeds/{Uri.EscapeDataString(feed)}/packagemetricsbatch?api-version={ApiVersion}");
    }

    private static Uri BuildPackageVersionMetricsUri(string organization, string? project, string feed, string packageId)
    {
        var projectSegment = string.IsNullOrWhiteSpace(project)
            ? string.Empty
            : "/" + Uri.EscapeDataString(project!);
        return new Uri($"https://feeds.dev.azure.com/{Uri.EscapeDataString(organization)}{projectSegment}/_apis/packaging/Feeds/{Uri.EscapeDataString(feed)}/Packages/{Uri.EscapeDataString(packageId)}/versionmetricsbatch?api-version={ApiVersion}");
    }

    private static async Task<HttpResponseMessage> SendJsonPostAsync(
        HttpClient http,
        Uri uri,
        object body,
        PrivateGalleryIndexOptions options,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        PrivateGalleryHttp.ApplyAuthentication(request, options.Token, options.AuthenticationKind);
        return await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static PrivateGalleryPackage? ParsePackage(JsonElement element, List<string> warnings)
    {
        var id = ReadString(element, "id");
        var name = ReadString(element, "name");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
        {
            warnings.Add("Azure Artifacts returned a package without id or name.");
            return null;
        }

        var package = new PrivateGalleryPackage
        {
            Id = id!,
            Name = name!,
            ProtocolType = ReadString(element, "protocolType") ?? "NuGet",
            Description = ReadString(element, "description"),
            WebUrl = ReadLink(element, "web")
        };

        if (TryGetArray(element, "versions", out var versions))
        {
            foreach (var versionElement in versions.EnumerateArray())
            {
                var version = ParseVersion(versionElement);
                if (version is null)
                    continue;

                package.Versions.Add(version);
                if (version.IsLatest || string.IsNullOrWhiteSpace(package.LatestVersion))
                    package.LatestVersion = version.Version;
            }
        }

        if (string.IsNullOrWhiteSpace(package.LatestVersion))
            package.LatestVersion = ReadString(element, "version");

        package.Versions = package.Versions
            .OrderByDescending(version => version.IsLatest)
            .ThenByDescending(version => version.PublishedAtUtc)
            .ThenBy(version => version.Version, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return package;
    }

    private static void MergePackage(PrivateGalleryPackage target, PrivateGalleryPackage source)
    {
        if (string.IsNullOrWhiteSpace(target.Description))
            target.Description = source.Description;
        if (string.IsNullOrWhiteSpace(target.WebUrl))
            target.WebUrl = source.WebUrl;

        foreach (var version in source.Versions)
        {
            if (target.Versions.Any(existing => existing.Id.Equals(version.Id, StringComparison.OrdinalIgnoreCase) ||
                                                existing.Version.Equals(version.Version, StringComparison.OrdinalIgnoreCase)))
                continue;

            target.Versions.Add(version);
        }

        target.Versions = target.Versions
            .OrderByDescending(version => version.IsLatest)
            .ThenByDescending(version => version.PublishedAtUtc)
            .ThenBy(version => version.Version, StringComparer.OrdinalIgnoreCase)
            .ToList();
        target.LatestVersion = target.Versions.FirstOrDefault(static version => version.IsLatest)?.Version ??
                               target.LatestVersion ??
                               source.LatestVersion;
    }

    private static PrivateGalleryPackageVersion? ParseVersion(JsonElement element)
    {
        var id = ReadString(element, "id");
        var versionText = ReadString(element, "version") ?? ReadString(element, "normalizedVersion");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(versionText))
            return null;

        var version = new PrivateGalleryPackageVersion
        {
            Id = id!,
            Version = versionText!,
            NormalizedVersion = ReadString(element, "normalizedVersion"),
            IsLatest = ReadBool(element, "isLatest") ?? false,
            IsListed = ReadBool(element, "isListed"),
            IsDeleted = ReadBool(element, "isDeleted"),
            PublishedAtUtc = ReadDateTimeOffset(element, "publishDate"),
            Description = ReadString(element, "description") ?? ReadString(element, "packageDescription"),
            Author = ReadString(element, "author")
        };

        if (TryGetArray(element, "dependencies", out var dependencies))
        {
            foreach (var dependencyElement in dependencies.EnumerateArray())
            {
                var dependencyName = ReadString(dependencyElement, "packageName");
                if (string.IsNullOrWhiteSpace(dependencyName))
                    continue;

                version.Dependencies.Add(new PrivateGalleryDependency
                {
                    Name = dependencyName!,
                    VersionRange = ReadString(dependencyElement, "versionRange"),
                    Group = ReadString(dependencyElement, "group")
                });
            }
        }

        if (TryGetArray(element, "views", out var views))
        {
            foreach (var viewElement in views.EnumerateArray())
            {
                var viewName = ReadString(viewElement, "name");
                if (!string.IsNullOrWhiteSpace(viewName))
                    version.Views.Add(viewName!);
            }
        }

        return version;
    }

    private static Dictionary<string, PrivateGalleryPackageMetrics> ParseMetricsArray(JsonElement element, string idProperty)
    {
        var metrics = new Dictionary<string, PrivateGalleryPackageMetrics>(StringComparer.OrdinalIgnoreCase);
        if (element.ValueKind != JsonValueKind.Array)
            return metrics;

        foreach (var item in element.EnumerateArray())
        {
            var id = ReadString(item, idProperty);
            if (string.IsNullOrWhiteSpace(id))
                continue;

            metrics[id!] = new PrivateGalleryPackageMetrics
            {
                DownloadCount = ReadInt64(item, "downloadCount"),
                UniqueUsers = ReadInt64(item, "downloadUniqueUsers"),
                LastDownloadedAtUtc = ReadDateTimeOffset(item, "lastDownloaded")
            };
        }

        return metrics;
    }

    private static string NormalizeRequired(string? value, string name)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value is required.", name)
            : value!.Trim().Trim('/');

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim().Trim('/');

    private static bool TryGetArray(JsonElement element, string propertyName, out JsonElement value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out value) &&
               value.ValueKind == JsonValueKind.Array;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : value.ToString()?.Trim();
    }

    private static bool? ReadBool(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static DateTimeOffset? ReadDateTimeOffset(JsonElement element, string propertyName)
    {
        var value = ReadString(element, propertyName);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }

    private static long? ReadInt64(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.Number)
        {
            if (value.TryGetInt64(out var integer))
                return integer;
            if (value.TryGetDouble(out var number))
                return Convert.ToInt64(Math.Round(number, MidpointRounding.AwayFromZero));
        }

        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static string? ReadLink(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("_links", out var links) ||
            links.ValueKind != JsonValueKind.Object ||
            !links.TryGetProperty(name, out var link) ||
            link.ValueKind != JsonValueKind.Object)
            return null;

        return ReadString(link, "href");
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(text) ? string.Empty : " " + text.Trim();
        }
        catch
        {
            return string.Empty;
        }
    }
}
