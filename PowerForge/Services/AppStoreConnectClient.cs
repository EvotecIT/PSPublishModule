using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace PowerForge;

/// <summary>
/// Read-only App Store Connect API client.
/// </summary>
public sealed class AppStoreConnectClient : IDisposable
{
    private static readonly Uri DefaultBaseUri = new("https://api.appstoreconnect.apple.com/v1/");

    private readonly AppStoreConnectApiCredential _credential;
    private readonly AppStoreConnectJwtTokenGenerator _tokenGenerator;
    private readonly HttpClient _httpClient;
    private readonly bool _disposeClient;

    /// <summary>
    /// Creates a read-only App Store Connect API client.
    /// </summary>
    public AppStoreConnectClient(
        AppStoreConnectApiCredential credential,
        HttpClient? httpClient = null,
        AppStoreConnectJwtTokenGenerator? tokenGenerator = null)
    {
        _credential = credential ?? throw new ArgumentNullException(nameof(credential));
        _tokenGenerator = tokenGenerator ?? new AppStoreConnectJwtTokenGenerator();
        _httpClient = httpClient ?? new HttpClient { BaseAddress = DefaultBaseUri };
        _disposeClient = httpClient is null;
        if (_httpClient.BaseAddress is null)
            _httpClient.BaseAddress = DefaultBaseUri;
    }

    /// <summary>
    /// Reads an app by App Store Connect app id.
    /// </summary>
    public Task<AppStoreConnectAppInfo?> GetAppAsync(string appId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appId))
            throw new ArgumentException("App id is required.", nameof(appId));

        return GetSingleAsync($"apps/{Uri.EscapeDataString(appId.Trim())}", ParseApp, cancellationToken);
    }

    /// <summary>
    /// Finds apps by bundle id, name, and/or platform.
    /// </summary>
    public Task<AppStoreConnectAppInfo[]> FindAppsAsync(
        string? bundleId = null,
        string? name = null,
        ApplePlatform? platform = null,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?>();
        if (!string.IsNullOrWhiteSpace(bundleId)) query["filter[bundleId]"] = bundleId!.Trim();
        if (!string.IsNullOrWhiteSpace(name)) query["filter[name]"] = name!.Trim();
        if (platform.HasValue) query["filter[appStoreVersions.platform]"] = ToAppStoreConnectPlatform(platform.Value);
        query["limit"] = ClampLimit(limit).ToString(CultureInfo.InvariantCulture);

        return GetArrayAsync("apps" + BuildQuery(query), ParseApp, cancellationToken);
    }

    /// <summary>
    /// Lists App Store versions for an app.
    /// </summary>
    public Task<AppStoreConnectVersionInfo[]> GetVersionsAsync(
        string appId,
        string? versionString = null,
        ApplePlatform? platform = null,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appId))
            throw new ArgumentException("App id is required.", nameof(appId));

        var query = new Dictionary<string, string?>
        {
            ["filter[app]"] = appId.Trim(),
            ["limit"] = ClampLimit(limit).ToString(CultureInfo.InvariantCulture)
        };
        if (!string.IsNullOrWhiteSpace(versionString)) query["filter[versionString]"] = versionString!.Trim();
        if (platform.HasValue) query["filter[platform]"] = ToAppStoreConnectPlatform(platform.Value);

        return GetArrayAsync("appStoreVersions" + BuildQuery(query), ParseVersion, cancellationToken);
    }

    /// <summary>
    /// Lists builds for an app.
    /// </summary>
    public Task<AppStoreConnectBuildInfo[]> GetBuildsAsync(
        string appId,
        string? buildNumber = null,
        int limit = 20,
        string? marketingVersion = null,
        ApplePlatform? platform = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appId))
            throw new ArgumentException("App id is required.", nameof(appId));

        var query = new Dictionary<string, string?>
        {
            ["filter[app]"] = appId.Trim(),
            ["limit"] = ClampLimit(limit).ToString(CultureInfo.InvariantCulture)
        };
        if (!string.IsNullOrWhiteSpace(buildNumber)) query["filter[version]"] = buildNumber!.Trim();
        if (!string.IsNullOrWhiteSpace(marketingVersion) || platform.HasValue) query["include"] = "preReleaseVersion";

        return GetBuildArrayAsync("builds" + BuildQuery(query), marketingVersion, platform, cancellationToken);
    }

    /// <summary>
    /// Compares local Xcode version values with App Store Connect app/version/build data.
    /// </summary>
    public async Task<AppleAppReleaseDriftReport> TestReleaseDriftAsync(
        string projectPath,
        string? appId = null,
        string? bundleId = null,
        ApplePlatform? platform = null,
        CancellationToken cancellationToken = default)
    {
        var local = new XcodeProjectVersionEditor().Read(projectPath);
        var messages = new List<string>();

        AppStoreConnectAppInfo? app = null;
        if (!string.IsNullOrWhiteSpace(appId))
        {
            app = await GetAppAsync(appId!, cancellationToken).ConfigureAwait(false);
            if (app is null) messages.Add($"App Store Connect app id '{appId}' was not found.");
        }
        else
        {
            var apps = await FindAppsAsync(bundleId: bundleId, platform: platform, limit: 10, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (apps.Length == 1)
                app = apps[0];
            else if (apps.Length == 0)
                messages.Add("No App Store Connect app matched the supplied filters.");
            else
                messages.Add($"Multiple App Store Connect apps matched the supplied filters ({apps.Length}); provide AppId.");
        }

        AppStoreConnectVersionInfo? remoteVersion = null;
        AppStoreConnectBuildInfo? remoteBuild = null;
        if (app is not null)
        {
            if (local.MarketingVersion is null)
                messages.Add("Local MARKETING_VERSION is missing or inconsistent.");
            else
            {
                remoteVersion = (await GetVersionsAsync(app.Id, local.MarketingVersion, platform, limit: 10, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
                if (remoteVersion is null)
                    messages.Add($"Remote App Store version '{local.MarketingVersion}' was not found.");
            }

            if (local.BuildNumber is null)
                messages.Add("Local CURRENT_PROJECT_VERSION is missing or inconsistent.");
            else
            {
                remoteBuild = (await GetBuildsAsync(
                    app.Id,
                    local.BuildNumber,
                    limit: 10,
                    marketingVersion: local.MarketingVersion,
                    platform: platform,
                    cancellationToken: cancellationToken).ConfigureAwait(false)).FirstOrDefault();
                if (remoteBuild is null)
                    messages.Add($"Remote build '{local.BuildNumber}' was not found.");
            }
        }

        return new AppleAppReleaseDriftReport
        {
            Local = local,
            App = app,
            RemoteVersion = remoteVersion,
            RemoteBuild = remoteBuild,
            IsMatch = app is not null && local.MarketingVersion is not null && local.BuildNumber is not null && remoteVersion is not null && remoteBuild is not null,
            Messages = messages.ToArray()
        };
    }

    /// <summary>
    /// Disposes the internally owned HTTP client.
    /// </summary>
    public void Dispose()
    {
        if (_disposeClient)
            _httpClient.Dispose();
    }

    private async Task<T?> GetSingleAsync<T>(string relativeUrl, Func<JsonElement, T> parse, CancellationToken cancellationToken)
    {
        using var doc = await GetJsonAsync(relativeUrl, cancellationToken, returnNullOnNotFound: true).ConfigureAwait(false);
        if (doc is null)
            return default;

        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
            return default;
        return parse(data);
    }

    private async Task<T[]> GetArrayAsync<T>(string relativeUrl, Func<JsonElement, T> parse, CancellationToken cancellationToken)
    {
        using var doc = await GetJsonAsync(relativeUrl, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("App Store Connect API request returned no response body.");
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return Array.Empty<T>();

        var list = new List<T>();
        foreach (var item in data.EnumerateArray())
            list.Add(parse(item));
        return list.ToArray();
    }

    private async Task<AppStoreConnectBuildInfo[]> GetBuildArrayAsync(
        string relativeUrl,
        string? marketingVersion,
        ApplePlatform? platform,
        CancellationToken cancellationToken)
    {
        using var doc = await GetJsonAsync(relativeUrl, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("App Store Connect API request returned no response body.");
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return Array.Empty<AppStoreConnectBuildInfo>();

        var preReleaseVersions = ReadIncludedPreReleaseVersions(doc.RootElement);
        var list = new List<AppStoreConnectBuildInfo>();
        foreach (var item in data.EnumerateArray())
        {
            var build = ParseBuild(item, preReleaseVersions);
            if (BuildMatches(build, marketingVersion, platform))
                list.Add(build);
        }

        return list.ToArray();
    }

    private async Task<JsonDocument?> GetJsonAsync(string relativeUrl, CancellationToken cancellationToken, bool returnNullOnNotFound = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokenGenerator.CreateToken(_credential));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (returnNullOnNotFound && response.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"App Store Connect API request failed ({(int)response.StatusCode} {response.ReasonPhrase}): {content}");

        return JsonDocument.Parse(content);
    }

    private static AppStoreConnectAppInfo ParseApp(JsonElement item)
    {
        var attrs = GetAttributes(item);
        return new AppStoreConnectAppInfo
        {
            Id = GetString(item, "id") ?? string.Empty,
            Name = GetString(attrs, "name"),
            BundleId = GetString(attrs, "bundleId"),
            Sku = GetString(attrs, "sku"),
            PrimaryLocale = GetString(attrs, "primaryLocale")
        };
    }

    private static AppStoreConnectVersionInfo ParseVersion(JsonElement item)
    {
        var attrs = GetAttributes(item);
        return new AppStoreConnectVersionInfo
        {
            Id = GetString(item, "id") ?? string.Empty,
            VersionString = GetString(attrs, "versionString"),
            AppStoreState = GetString(attrs, "appStoreState"),
            AppVersionState = GetString(attrs, "appVersionState"),
            Platform = GetString(attrs, "platform")
        };
    }

    private static AppStoreConnectBuildInfo ParseBuild(JsonElement item)
    {
        var attrs = GetAttributes(item);
        return new AppStoreConnectBuildInfo
        {
            Id = GetString(item, "id") ?? string.Empty,
            Version = GetString(attrs, "version"),
            ProcessingState = GetString(attrs, "processingState"),
            UploadedDate = GetDateTimeOffset(attrs, "uploadedDate"),
            Expired = GetBool(attrs, "expired"),
            MinOsVersion = GetString(attrs, "minOsVersion")
        };
    }

    private static AppStoreConnectBuildInfo ParseBuild(JsonElement item, IReadOnlyDictionary<string, BuildPreReleaseVersion> preReleaseVersions)
    {
        var build = ParseBuild(item);
        var preReleaseVersionId = GetRelationshipDataId(item, "preReleaseVersion");
        if (!string.IsNullOrWhiteSpace(preReleaseVersionId) &&
            preReleaseVersions.TryGetValue(preReleaseVersionId!, out var preReleaseVersion))
        {
            build.MarketingVersion = preReleaseVersion.Version;
            build.Platform = preReleaseVersion.Platform;
        }

        return build;
    }

    private static bool BuildMatches(AppStoreConnectBuildInfo build, string? marketingVersion, ApplePlatform? platform)
    {
        if (!string.IsNullOrWhiteSpace(marketingVersion))
        {
            var trimmedMarketingVersion = marketingVersion!.Trim();
            if (!string.Equals(build.MarketingVersion, trimmedMarketingVersion, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (platform.HasValue &&
            !string.Equals(build.Platform, ToAppStoreConnectPlatform(platform.Value), StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static Dictionary<string, BuildPreReleaseVersion> ReadIncludedPreReleaseVersions(JsonElement root)
    {
        var result = new Dictionary<string, BuildPreReleaseVersion>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("included", out var included) || included.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in included.EnumerateArray())
        {
            if (!string.Equals(GetString(item, "type"), "preReleaseVersions", StringComparison.OrdinalIgnoreCase))
                continue;

            var id = GetString(item, "id");
            if (string.IsNullOrWhiteSpace(id))
                continue;

            var attrs = GetAttributes(item);
            result[id!] = new BuildPreReleaseVersion(
                GetString(attrs, "version"),
                GetString(attrs, "platform"));
        }

        return result;
    }

    private static JsonElement GetAttributes(JsonElement item)
        => item.TryGetProperty("attributes", out var attrs) ? attrs : default;

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var prop))
            return null;
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.ToString();
    }

    private static bool? GetBool(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var prop))
            return null;
        return prop.ValueKind == JsonValueKind.True ? true : prop.ValueKind == JsonValueKind.False ? false : null;
    }

    private static string? GetRelationshipDataId(JsonElement item, string relationshipName)
    {
        if (item.ValueKind != JsonValueKind.Object ||
            !item.TryGetProperty("relationships", out var relationships) ||
            relationships.ValueKind != JsonValueKind.Object ||
            !relationships.TryGetProperty(relationshipName, out var relationship) ||
            relationship.ValueKind != JsonValueKind.Object ||
            !relationship.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object)
            return null;

        return GetString(data, "id");
    }

    private static DateTimeOffset? GetDateTimeOffset(JsonElement element, string propertyName)
    {
        var value = GetString(element, propertyName);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static string BuildQuery(Dictionary<string, string?> values)
    {
        var parts = values
            .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value))
            .Select(kvp => Uri.EscapeDataString(kvp.Key) + "=" + Uri.EscapeDataString(kvp.Value!))
            .ToArray();
        return parts.Length == 0 ? string.Empty : "?" + string.Join("&", parts);
    }

    private static string ToAppStoreConnectPlatform(ApplePlatform platform)
        => platform switch
        {
            ApplePlatform.iOS => "IOS",
            ApplePlatform.iPadOS => "IOS",
            ApplePlatform.macOS => "MAC_OS",
            ApplePlatform.tvOS => "TV_OS",
            ApplePlatform.watchOS => "WATCH_OS",
            ApplePlatform.visionOS => "VISION_OS",
            _ => platform.ToString().ToUpperInvariant()
        };

    private static int ClampLimit(int limit)
    {
        if (limit < 1) return 1;
        if (limit > 200) return 200;
        return limit;
    }

    private sealed class BuildPreReleaseVersion
    {
        public BuildPreReleaseVersion(string? version, string? platform)
        {
            Version = version;
            Platform = platform;
        }

        public string? Version { get; }

        public string? Platform { get; }
    }
}
