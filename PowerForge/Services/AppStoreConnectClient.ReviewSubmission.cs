using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace PowerForge;

public sealed partial class AppStoreConnectClient
{
    /// <summary>
    /// Lists recent and current review submissions for an app.
    /// </summary>
    public Task<AppStoreConnectReviewSubmissionInfo[]> GetReviewSubmissionsAsync(
        string appId,
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
        if (platform.HasValue)
            query["filter[platform]"] = ToAppStoreConnectPlatform(platform.Value);

        return GetArrayAsync("reviewSubmissions" + BuildQuery(query), ParseReviewSubmission, cancellationToken);
    }

    /// <summary>
    /// Creates a review submission for an app and platform.
    /// </summary>
    public Task<AppStoreConnectReviewSubmissionInfo> CreateReviewSubmissionAsync(
        string appId,
        ApplePlatform platform,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appId))
            throw new ArgumentException("App id is required.", nameof(appId));

        var body = new
        {
            data = new
            {
                type = "reviewSubmissions",
                attributes = new { platform = ToAppStoreConnectPlatform(platform) },
                relationships = new
                {
                    app = new
                    {
                        data = new { type = "apps", id = appId.Trim() }
                    }
                }
            }
        };

        return PostSingleAsync("reviewSubmissions", body, ParseReviewSubmission, cancellationToken);
    }

    /// <summary>
    /// Creates a review submission item for an App Store version.
    /// </summary>
    public Task<AppStoreConnectReviewSubmissionItemInfo> CreateReviewSubmissionItemAsync(
        string reviewSubmissionId,
        string appStoreVersionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reviewSubmissionId))
            throw new ArgumentException("Review submission id is required.", nameof(reviewSubmissionId));
        if (string.IsNullOrWhiteSpace(appStoreVersionId))
            throw new ArgumentException("App Store version id is required.", nameof(appStoreVersionId));

        var body = new
        {
            data = new
            {
                type = "reviewSubmissionItems",
                relationships = new
                {
                    reviewSubmission = new
                    {
                        data = new { type = "reviewSubmissions", id = reviewSubmissionId.Trim() }
                    },
                    appStoreVersion = new
                    {
                        data = new { type = "appStoreVersions", id = appStoreVersionId.Trim() }
                    }
                }
            }
        };

        return PostSingleAsync("reviewSubmissionItems", body, ParseReviewSubmissionItem, cancellationToken);
    }

    /// <summary>
    /// Lists items in a review submission.
    /// </summary>
    public Task<AppStoreConnectReviewSubmissionItemInfo[]> GetReviewSubmissionItemsAsync(
        string reviewSubmissionId,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reviewSubmissionId))
            throw new ArgumentException("Review submission id is required.", nameof(reviewSubmissionId));

        var query = new Dictionary<string, string?>
        {
            ["fields[reviewSubmissionItems]"] = "state,appStoreVersion",
            ["limit"] = ClampLimit(limit).ToString(CultureInfo.InvariantCulture)
        };

        return GetArrayAsync(
            $"reviewSubmissions/{Uri.EscapeDataString(reviewSubmissionId.Trim())}/items" + BuildQuery(query),
            ParseReviewSubmissionItem,
            cancellationToken);
    }

    /// <summary>
    /// Marks a review submission as submitted to App Review.
    /// </summary>
    public async Task<AppStoreConnectReviewSubmissionInfo> SubmitReviewSubmissionAsync(
        string reviewSubmissionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reviewSubmissionId))
            throw new ArgumentException("Review submission id is required.", nameof(reviewSubmissionId));

        var body = new
        {
            data = new
            {
                type = "reviewSubmissions",
                id = reviewSubmissionId.Trim(),
                attributes = new { submitted = true }
            }
        };

        var result = await PatchSingleAsync(
            $"reviewSubmissions/{Uri.EscapeDataString(reviewSubmissionId.Trim())}",
            body,
            ParseReviewSubmission,
            cancellationToken).ConfigureAwait(false);
        result.IsSubmitted ??= true;
        return result;
    }

    private async Task<T> PatchSingleAsync<T>(string relativeUrl, object body, Func<JsonElement, T> parse, CancellationToken cancellationToken)
    {
        using var doc = await SendJsonAsync(new HttpMethod("PATCH"), relativeUrl, body, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("App Store Connect API request returned no response body.");
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
            throw new InvalidOperationException("App Store Connect API request returned no data.");
        return parse(data);
    }

    private static AppStoreConnectReviewSubmissionInfo ParseReviewSubmission(JsonElement item)
    {
        var attrs = GetAttributes(item);
        return new AppStoreConnectReviewSubmissionInfo
        {
            Id = GetString(item, "id") ?? string.Empty,
            Platform = GetString(attrs, "platform"),
            IsSubmitted = GetBool(attrs, "submitted") ?? GetBool(attrs, "isSubmitted"),
            State = GetString(attrs, "state")
        };
    }

    private static AppStoreConnectReviewSubmissionItemInfo ParseReviewSubmissionItem(JsonElement item)
    {
        return new AppStoreConnectReviewSubmissionItemInfo
        {
            Id = GetString(item, "id") ?? string.Empty,
            ReviewSubmissionId = GetRelationshipDataId(item, "reviewSubmission"),
            AppStoreVersionId = GetRelationshipDataId(item, "appStoreVersion"),
            State = GetString(GetAttributes(item), "state")
        };
    }

}
