using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;

namespace PowerForge;

/// <summary>
/// App Store Connect API client.
/// </summary>
public sealed partial class AppStoreConnectClient : IDisposable
{
    private static readonly Uri DefaultBaseUri = new("https://api.appstoreconnect.apple.com/v1/");

    private readonly AppStoreConnectApiCredential _credential;
    private readonly AppStoreConnectJwtTokenGenerator _tokenGenerator;
    private readonly HttpClient _httpClient;
    private readonly bool _disposeClient;

    /// <summary>
    /// Creates an App Store Connect API client.
    /// </summary>
    public AppStoreConnectClient(
        AppStoreConnectApiCredential credential,
        HttpClient? httpClient = null,
        AppStoreConnectJwtTokenGenerator? tokenGenerator = null)
    {
        _credential = credential ?? throw new ArgumentNullException(nameof(credential));
        _tokenGenerator = tokenGenerator ?? new AppStoreConnectJwtTokenGenerator();
        _httpClient = httpClient ?? CreateDefaultHttpClient();
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
            ["limit"] = ClampLimit(limit).ToString(CultureInfo.InvariantCulture)
        };
        if (!string.IsNullOrWhiteSpace(versionString)) query["filter[versionString]"] = versionString!.Trim();
        if (platform.HasValue) query["filter[platform]"] = ToAppStoreConnectPlatform(platform.Value);

        return GetArrayAsync(
            $"apps/{Uri.EscapeDataString(appId.Trim())}/appStoreVersions" + BuildQuery(query),
            ParseVersion,
            cancellationToken);
    }

    /// <summary>
    /// Creates an App Store version for an app and platform.
    /// </summary>
    public Task<AppStoreConnectVersionInfo> CreateVersionAsync(
        string appId,
        string versionString,
        ApplePlatform platform,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appId))
            throw new ArgumentException("App id is required.", nameof(appId));
        if (string.IsNullOrWhiteSpace(versionString))
            throw new ArgumentException("Version string is required.", nameof(versionString));

        var body = new
        {
            data = new
            {
                type = "appStoreVersions",
                attributes = new
                {
                    platform = ToAppStoreConnectPlatform(platform),
                    versionString = versionString.Trim()
                },
                relationships = new
                {
                    app = new
                    {
                        data = new { type = "apps", id = appId.Trim() }
                    }
                }
            }
        };

        return PostSingleAsync("appStoreVersions", body, ParseVersion, cancellationToken);
    }

    /// <summary>
    /// Reads the build relationship id currently selected for an App Store version.
    /// </summary>
    public async Task<string?> GetVersionBuildIdAsync(string versionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(versionId))
            throw new ArgumentException("Version id is required.", nameof(versionId));

        using var doc = await GetJsonAsync(
            $"appStoreVersions/{Uri.EscapeDataString(versionId.Trim())}/relationships/build",
            cancellationToken,
            returnNullOnNotFound: true).ConfigureAwait(false);
        if (doc is null ||
            !doc.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind == JsonValueKind.Null)
            return null;
        if (data.ValueKind != JsonValueKind.Object)
            return null;

        return GetString(data, "id");
    }

    /// <summary>
    /// Selects a build for an App Store version.
    /// </summary>
    public async Task SetVersionBuildAsync(
        string versionId,
        string buildId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(versionId))
            throw new ArgumentException("Version id is required.", nameof(versionId));
        if (string.IsNullOrWhiteSpace(buildId))
            throw new ArgumentException("Build id is required.", nameof(buildId));

        var body = new
        {
            data = new { type = "builds", id = buildId.Trim() }
        };

        using var _ = await SendJsonAsync(
            new HttpMethod("PATCH"),
            $"appStoreVersions/{Uri.EscapeDataString(versionId.Trim())}/relationships/build",
            body,
            cancellationToken).ConfigureAwait(false);
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
    /// Lists subscription groups for an app.
    /// </summary>
    public Task<AppStoreConnectSubscriptionGroupInfo[]> GetSubscriptionGroupsAsync(
        string appId,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appId))
            throw new ArgumentException("App id is required.", nameof(appId));

        var query = new Dictionary<string, string?>
        {
            ["limit"] = ClampLimit(limit).ToString(CultureInfo.InvariantCulture)
        };

        return GetArrayAsync(
            $"apps/{Uri.EscapeDataString(appId.Trim())}/subscriptionGroups" + BuildQuery(query),
            ParseSubscriptionGroup,
            cancellationToken);
    }

    /// <summary>
    /// Lists auto-renewable subscriptions for a subscription group.
    /// </summary>
    public Task<AppStoreConnectSubscriptionInfo[]> GetSubscriptionsAsync(
        string subscriptionGroupId,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subscriptionGroupId))
            throw new ArgumentException("Subscription group id is required.", nameof(subscriptionGroupId));

        var query = new Dictionary<string, string?>
        {
            ["limit"] = ClampLimit(limit).ToString(CultureInfo.InvariantCulture)
        };

        return GetArrayAsync(
            $"subscriptionGroups/{Uri.EscapeDataString(subscriptionGroupId.Trim())}/subscriptions" + BuildQuery(query),
            ParseSubscription,
            cancellationToken);
    }

    /// <summary>
    /// Lists all auto-renewable subscriptions for an app across subscription groups.
    /// </summary>
    public async Task<AppStoreConnectSubscriptionInfo[]> GetSubscriptionsForAppAsync(
        string appId,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        var groups = await GetSubscriptionGroupsAsync(appId, limit, cancellationToken).ConfigureAwait(false);
        var subscriptions = new List<AppStoreConnectSubscriptionInfo>();
        foreach (var group in groups)
        {
            var groupSubscriptions = await GetSubscriptionsAsync(group.Id, limit, cancellationToken).ConfigureAwait(false);
            foreach (var subscription in groupSubscriptions)
            {
                subscription.SubscriptionGroupId = group.Id;
                subscription.SubscriptionGroupReferenceName = group.ReferenceName;
                subscriptions.Add(subscription);
            }
        }

        return subscriptions.ToArray();
    }

    /// <summary>
    /// Lists App Store version localizations for an App Store version.
    /// </summary>
    public Task<AppStoreConnectVersionLocalizationInfo[]> GetVersionLocalizationsAsync(
        string versionId,
        string? locale = null,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(versionId))
            throw new ArgumentException("Version id is required.", nameof(versionId));

        var query = new Dictionary<string, string?>
        {
            ["limit"] = ClampLimit(limit).ToString(CultureInfo.InvariantCulture)
        };
        if (!string.IsNullOrWhiteSpace(locale)) query["filter[locale]"] = locale!.Trim();

        return GetArrayAsync(
            $"appStoreVersions/{Uri.EscapeDataString(versionId.Trim())}/appStoreVersionLocalizations" + BuildQuery(query),
            ParseVersionLocalization,
            cancellationToken);
    }

    /// <summary>
    /// Lists screenshot sets for an App Store version localization.
    /// </summary>
    public Task<AppStoreConnectScreenshotSetInfo[]> GetScreenshotSetsAsync(
        string versionLocalizationId,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(versionLocalizationId))
            throw new ArgumentException("Version localization id is required.", nameof(versionLocalizationId));

        var query = new Dictionary<string, string?>
        {
            ["limit"] = ClampLimit(limit).ToString(CultureInfo.InvariantCulture)
        };

        return GetArrayAsync(
            $"appStoreVersionLocalizations/{Uri.EscapeDataString(versionLocalizationId.Trim())}/appScreenshotSets" + BuildQuery(query),
            ParseScreenshotSet,
            cancellationToken);
    }

    /// <summary>
    /// Lists screenshots for an App Store Connect screenshot set.
    /// </summary>
    public Task<AppStoreConnectScreenshotInfo[]> GetScreenshotsAsync(
        string screenshotSetId,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(screenshotSetId))
            throw new ArgumentException("Screenshot set id is required.", nameof(screenshotSetId));

        var query = new Dictionary<string, string?>
        {
            ["limit"] = ClampLimit(limit).ToString(CultureInfo.InvariantCulture)
        };

        return GetArrayAsync(
            $"appScreenshotSets/{Uri.EscapeDataString(screenshotSetId.Trim())}/appScreenshots" + BuildQuery(query),
            ParseScreenshot,
            cancellationToken);
    }

    /// <summary>
    /// Creates a screenshot set for an App Store version localization.
    /// </summary>
    public Task<AppStoreConnectScreenshotSetInfo> CreateScreenshotSetAsync(
        string versionLocalizationId,
        string screenshotDisplayType,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(versionLocalizationId))
            throw new ArgumentException("Version localization id is required.", nameof(versionLocalizationId));
        if (string.IsNullOrWhiteSpace(screenshotDisplayType))
            throw new ArgumentException("Screenshot display type is required.", nameof(screenshotDisplayType));

        var body = new
        {
            data = new
            {
                type = "appScreenshotSets",
                attributes = new { screenshotDisplayType = screenshotDisplayType.Trim() },
                relationships = new
                {
                    appStoreVersionLocalization = new
                    {
                        data = new { type = "appStoreVersionLocalizations", id = versionLocalizationId.Trim() }
                    }
                }
            }
        };

        return PostSingleAsync("appScreenshotSets", body, ParseScreenshotSet, cancellationToken);
    }

    /// <summary>
    /// Creates an App Store screenshot asset reservation.
    /// </summary>
    public Task<AppStoreConnectScreenshotInfo> CreateScreenshotReservationAsync(
        string screenshotSetId,
        string fileName,
        long fileSize,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(screenshotSetId))
            throw new ArgumentException("Screenshot set id is required.", nameof(screenshotSetId));
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name is required.", nameof(fileName));
        if (fileSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(fileSize), "File size must be greater than zero.");

        var body = new
        {
            data = new
            {
                type = "appScreenshots",
                attributes = new { fileName = fileName.Trim(), fileSize },
                relationships = new
                {
                    appScreenshotSet = new
                    {
                        data = new { type = "appScreenshotSets", id = screenshotSetId.Trim() }
                    }
                }
            }
        };

        return PostSingleAsync("appScreenshots", body, ParseScreenshot, cancellationToken);
    }

    /// <summary>
    /// Uploads and commits a screenshot file to an existing screenshot set.
    /// </summary>
    public async Task<AppStoreConnectScreenshotUploadResult> UploadScreenshotAsync(
        string screenshotSetId,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path is required.", nameof(filePath));

        var fullPath = Path.GetFullPath(filePath);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
            throw new FileNotFoundException("Screenshot file was not found.", fullPath);

        var reservation = await CreateScreenshotReservationAsync(
            screenshotSetId,
            file.Name,
            file.Length,
            cancellationToken).ConfigureAwait(false);

        foreach (var operation in reservation.UploadOperations)
            await ExecuteUploadOperationAsync(fullPath, operation, cancellationToken).ConfigureAwait(false);

        var checksum = ComputeMd5Checksum(fullPath);
        var committed = await CommitScreenshotUploadAsync(reservation.Id, checksum, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(committed.SourceFileChecksum))
            reservation.SourceFileChecksum = committed.SourceFileChecksum;
        if (!string.IsNullOrWhiteSpace(committed.AssetDeliveryState))
            reservation.AssetDeliveryState = committed.AssetDeliveryState;

        return new AppStoreConnectScreenshotUploadResult
        {
            Screenshot = reservation,
            FilePath = fullPath,
            SourceFileChecksum = checksum,
            UploadOperationCount = reservation.UploadOperations.Length
        };
    }

    /// <summary>
    /// Deletes an App Store Connect screenshot.
    /// </summary>
    public async Task DeleteScreenshotAsync(string screenshotId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(screenshotId))
            throw new ArgumentException("Screenshot id is required.", nameof(screenshotId));

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"appScreenshots/{Uri.EscapeDataString(screenshotId.Trim())}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokenGenerator.CreateToken(_credential));

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"App Store Connect API request failed ({(int)response.StatusCode} {response.ReasonPhrase}): {content}");
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
        var list = new List<T>();
        var visitedPages = new HashSet<string>(StringComparer.Ordinal);
        string? nextPage = relativeUrl;
        while (true)
        {
            var currentPage = nextPage;
            if (currentPage is null)
                break;
            if (!visitedPages.Add(currentPage))
                throw new InvalidOperationException($"App Store Connect API returned a repeated pagination link: {currentPage}");

            using var doc = await GetJsonAsync(currentPage, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("App Store Connect API request returned no response body.");
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                    list.Add(parse(item));
            }

            nextPage = GetNextPageLink(doc.RootElement);
        }

        return list.ToArray();
    }

    private static string? GetNextPageLink(JsonElement root)
    {
        if (!root.TryGetProperty("links", out var links) || links.ValueKind != JsonValueKind.Object)
            return null;
        if (!links.TryGetProperty("next", out var next) || next.ValueKind != JsonValueKind.String)
            return null;

        var value = next.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private async Task<T> PostSingleAsync<T>(string relativeUrl, object body, Func<JsonElement, T> parse, CancellationToken cancellationToken)
    {
        using var doc = await SendJsonAsync(HttpMethod.Post, relativeUrl, body, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("App Store Connect API request returned no response body.");
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
            throw new InvalidOperationException("App Store Connect API request returned no data.");
        return parse(data);
    }

    private async Task<AppStoreConnectScreenshotInfo> CommitScreenshotUploadAsync(
        string screenshotId,
        string sourceFileChecksum,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            data = new
            {
                type = "appScreenshots",
                id = screenshotId,
                attributes = new { uploaded = true, sourceFileChecksum }
            }
        };

        using var doc = await SendJsonAsync(
            new HttpMethod("PATCH"),
            $"appScreenshots/{Uri.EscapeDataString(screenshotId)}",
            body,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("App Store Connect API request returned no response body.");
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
            throw new InvalidOperationException("App Store Connect API request returned no data.");
        return ParseScreenshot(data);
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
        var response = await SendGetWithTransientRetryAsync(relativeUrl, cancellationToken).ConfigureAwait(false);
        if (returnNullOnNotFound && response.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"App Store Connect API request failed ({(int)response.StatusCode} {response.ReasonPhrase}): {response.Content}");

        return JsonDocument.Parse(response.Content);
    }

    private async Task<JsonDocument?> SendJsonAsync(HttpMethod method, string relativeUrl, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativeUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokenGenerator.CreateToken(_credential));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var json = JsonSerializer.Serialize(body);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"App Store Connect API request failed ({(int)response.StatusCode} {response.ReasonPhrase}): {content}");

        return string.IsNullOrWhiteSpace(content) ? null : JsonDocument.Parse(content);
    }

    private async Task ExecuteUploadOperationAsync(
        string filePath,
        AppStoreConnectUploadOperation operation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(operation.Url))
            throw new InvalidOperationException("Upload operation URL is missing.");
        if (operation.Length < 0)
            throw new InvalidOperationException("Upload operation length cannot be negative.");
        if (operation.Length > int.MaxValue)
            throw new InvalidOperationException("Upload operation is too large for the current uploader.");

        var bytes = new byte[(int)operation.Length];
        using (var stream = File.OpenRead(filePath))
        {
            stream.Seek(operation.Offset, SeekOrigin.Begin);
            var read = 0;
            while (read < bytes.Length)
            {
                var count = await stream.ReadAsync(bytes, read, bytes.Length - read, cancellationToken).ConfigureAwait(false);
                if (count == 0)
                    throw new EndOfStreamException("Screenshot file ended before upload operation bytes were read.");
                read += count;
            }
        }

        using var request = new HttpRequestMessage(new HttpMethod(string.IsNullOrWhiteSpace(operation.Method) ? "PUT" : operation.Method), operation.Url);
        request.Content = new ByteArrayContent(bytes);
        foreach (var header in operation.RequestHeaders)
        {
            if (string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value))
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"App Store Connect asset upload failed ({(int)response.StatusCode} {response.ReasonPhrase}): {content}");
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

    private static AppStoreConnectVersionLocalizationInfo ParseVersionLocalization(JsonElement item)
    {
        var attrs = GetAttributes(item);
        return new AppStoreConnectVersionLocalizationInfo
        {
            Id = GetString(item, "id") ?? string.Empty,
            Locale = GetString(attrs, "locale"),
            Name = GetString(attrs, "name"),
            Description = GetString(attrs, "description"),
            Keywords = GetString(attrs, "keywords"),
            MarketingUrl = GetString(attrs, "marketingUrl"),
            PromotionalText = GetString(attrs, "promotionalText"),
            SupportUrl = GetString(attrs, "supportUrl"),
            WhatsNew = GetString(attrs, "whatsNew")
        };
    }

    private static AppStoreConnectSubscriptionGroupInfo ParseSubscriptionGroup(JsonElement item)
    {
        var attrs = GetAttributes(item);
        return new AppStoreConnectSubscriptionGroupInfo
        {
            Id = GetString(item, "id") ?? string.Empty,
            ReferenceName = GetString(attrs, "referenceName")
        };
    }

    private static AppStoreConnectSubscriptionInfo ParseSubscription(JsonElement item)
    {
        var attrs = GetAttributes(item);
        return new AppStoreConnectSubscriptionInfo
        {
            Id = GetString(item, "id") ?? string.Empty,
            ProductId = GetString(attrs, "productId"),
            Name = GetString(attrs, "name"),
            State = GetString(attrs, "state"),
            SubscriptionPeriod = GetString(attrs, "subscriptionPeriod"),
            FamilySharable = GetBool(attrs, "familySharable")
        };
    }

    private static AppStoreConnectScreenshotSetInfo ParseScreenshotSet(JsonElement item)
    {
        var attrs = GetAttributes(item);
        return new AppStoreConnectScreenshotSetInfo
        {
            Id = GetString(item, "id") ?? string.Empty,
            ScreenshotDisplayType = GetString(attrs, "screenshotDisplayType")
        };
    }

    private static AppStoreConnectScreenshotInfo ParseScreenshot(JsonElement item)
    {
        var attrs = GetAttributes(item);
        return new AppStoreConnectScreenshotInfo
        {
            Id = GetString(item, "id") ?? string.Empty,
            FileName = GetString(attrs, "fileName"),
            FileSize = GetInt64(attrs, "fileSize"),
            SourceFileChecksum = GetString(attrs, "sourceFileChecksum"),
            AssetToken = GetString(attrs, "assetToken"),
            AssetDeliveryState = GetNestedString(attrs, "assetDeliveryState", "state"),
            UploadOperations = GetUploadOperations(attrs, "uploadOperations")
        };
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

    private static long? GetInt64(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var prop))
            return null;
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var value))
            return value;
        return long.TryParse(prop.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static string? GetNestedString(JsonElement element, string objectName, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(objectName, out var nested) ||
            nested.ValueKind != JsonValueKind.Object)
            return null;

        return GetString(nested, propertyName);
    }

    private static AppStoreConnectUploadOperation[] GetUploadOperations(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var operations) ||
            operations.ValueKind != JsonValueKind.Array)
            return Array.Empty<AppStoreConnectUploadOperation>();

        var list = new List<AppStoreConnectUploadOperation>();
        foreach (var operation in operations.EnumerateArray())
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (operation.TryGetProperty("requestHeaders", out var requestHeaders) &&
                requestHeaders.ValueKind == JsonValueKind.Array)
            {
                foreach (var header in requestHeaders.EnumerateArray())
                {
                    var name = GetString(header, "name");
                    var value = GetString(header, "value");
                    if (!string.IsNullOrWhiteSpace(name) && value is not null)
                        headers[name!] = value;
                }
            }

            list.Add(new AppStoreConnectUploadOperation
            {
                Method = GetString(operation, "method") ?? "PUT",
                Url = GetString(operation, "url") ?? string.Empty,
                Offset = GetInt64(operation, "offset") ?? 0,
                Length = GetInt64(operation, "length") ?? 0,
                RequestHeaders = headers
            });
        }

        return list.ToArray();
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

    private static string ComputeMd5Checksum(string filePath)
    {
        using var md5 = MD5.Create();
        using var stream = File.OpenRead(filePath);
        var bytes = md5.ComputeHash(stream);
        return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
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
