using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace PowerForge;

public sealed partial class AppStoreConnectClient
{
    /// <summary>
    /// Lists TestFlight beta groups for an app.
    /// </summary>
    public Task<AppStoreConnectBetaGroupInfo[]> GetBetaGroupsAsync(
        string appId,
        string? name = null,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appId))
            throw new ArgumentException("App id is required.", nameof(appId));

        var query = new Dictionary<string, string?>
        {
            ["filter[app]"] = appId.Trim(),
            ["limit"] = ClampLimit(limit).ToString(CultureInfo.InvariantCulture)
        };
        if (!string.IsNullOrWhiteSpace(name))
            query["filter[name]"] = name!.Trim();

        return GetArrayAsync("betaGroups" + BuildQuery(query), ParseBetaGroup, cancellationToken);
    }

    /// <summary>
    /// Lists beta testers, optionally filtering by email.
    /// </summary>
    public Task<AppStoreConnectBetaTesterInfo[]> GetBetaTestersAsync(
        string? email = null,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["limit"] = ClampLimit(limit).ToString(CultureInfo.InvariantCulture)
        };
        if (!string.IsNullOrWhiteSpace(email))
            query["filter[email]"] = email!.Trim();

        return GetArrayAsync("betaTesters" + BuildQuery(query), ParseBetaTester, cancellationToken);
    }

    /// <summary>
    /// Creates a beta tester and optionally adds the tester to beta groups.
    /// </summary>
    public Task<AppStoreConnectBetaTesterInfo> CreateBetaTesterAsync(
        string email,
        string? firstName = null,
        string? lastName = null,
        string[]? betaGroupIds = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required.", nameof(email));

        var body = new
        {
            data = new
            {
                type = "betaTesters",
                attributes = new
                {
                    email = email.Trim(),
                    firstName = string.IsNullOrWhiteSpace(firstName) ? null : firstName!.Trim(),
                    lastName = string.IsNullOrWhiteSpace(lastName) ? null : lastName!.Trim()
                },
                relationships = betaGroupIds is null || betaGroupIds.Length == 0
                    ? null
                    : new
                    {
                        betaGroups = new
                        {
                            data = betaGroupIds
                                .Where(static id => !string.IsNullOrWhiteSpace(id))
                                .Select(static id => new { type = "betaGroups", id = id.Trim() })
                                .ToArray()
                        }
                    }
            }
        };

        return PostSingleAsync("betaTesters", body, ParseBetaTester, cancellationToken);
    }

    /// <summary>
    /// Adds one or more builds to a beta group.
    /// </summary>
    public Task AddBuildsToBetaGroupAsync(
        string betaGroupId,
        string[] buildIds,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(betaGroupId))
            throw new ArgumentException("Beta group id is required.", nameof(betaGroupId));
        var ids = NormalizeIds(buildIds, nameof(buildIds));
        var body = new
        {
            data = ids.Select(static id => new { type = "builds", id }).ToArray()
        };

        return SendJsonNoContentAsync(
            HttpMethod.Post,
            $"betaGroups/{Uri.EscapeDataString(betaGroupId.Trim())}/relationships/builds",
            body,
            cancellationToken);
    }

    /// <summary>
    /// Adds one or more beta testers to a beta group.
    /// </summary>
    public Task AddBetaTestersToBetaGroupAsync(
        string betaGroupId,
        string[] betaTesterIds,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(betaGroupId))
            throw new ArgumentException("Beta group id is required.", nameof(betaGroupId));
        var ids = NormalizeIds(betaTesterIds, nameof(betaTesterIds));
        var body = new
        {
            data = ids.Select(static id => new { type = "betaTesters", id }).ToArray()
        };

        return SendJsonNoContentAsync(
            HttpMethod.Post,
            $"betaGroups/{Uri.EscapeDataString(betaGroupId.Trim())}/relationships/betaTesters",
            body,
            cancellationToken);
    }

    /// <summary>
    /// Reads the Beta App Review submission associated with a build, when one exists.
    /// </summary>
    public Task<AppStoreConnectBetaAppReviewSubmissionInfo?> GetBetaAppReviewSubmissionForBuildAsync(
        string buildId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buildId))
            throw new ArgumentException("Build id is required.", nameof(buildId));

        return GetSingleAsync(
            $"builds/{Uri.EscapeDataString(buildId.Trim())}/betaAppReviewSubmission",
            ParseBetaAppReviewSubmission,
            cancellationToken);
    }

    /// <summary>
    /// Submits a build to Beta App Review for external TestFlight testing.
    /// </summary>
    public Task<AppStoreConnectBetaAppReviewSubmissionInfo> CreateBetaAppReviewSubmissionAsync(
        string buildId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buildId))
            throw new ArgumentException("Build id is required.", nameof(buildId));

        var body = new
        {
            data = new
            {
                type = "betaAppReviewSubmissions",
                relationships = new
                {
                    build = new
                    {
                        data = new { type = "builds", id = buildId.Trim() }
                    }
                }
            }
        };

        return PostSingleAsync("betaAppReviewSubmissions", body, ParseBetaAppReviewSubmission, cancellationToken);
    }

    private async Task SendJsonNoContentAsync(
        HttpMethod method,
        string relativeUrl,
        object body,
        CancellationToken cancellationToken)
    {
        using var _ = await SendJsonAsync(method, relativeUrl, body, cancellationToken).ConfigureAwait(false);
    }

    private static string[] NormalizeIds(string[]? ids, string parameterName)
    {
        var normalized = (ids ?? Array.Empty<string>())
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalized.Length == 0)
            throw new ArgumentException("At least one id is required.", parameterName);
        return normalized;
    }

    private static AppStoreConnectBetaGroupInfo ParseBetaGroup(JsonElement item)
    {
        var attrs = GetAttributes(item);
        return new AppStoreConnectBetaGroupInfo
        {
            Id = GetString(item, "id") ?? string.Empty,
            Name = GetString(attrs, "name"),
            PublicLinkEnabled = GetBool(attrs, "publicLinkEnabled"),
            PublicLinkLimit = GetInt32(attrs, "publicLinkLimit"),
            PublicLink = GetString(attrs, "publicLink"),
            FeedbackEnabled = GetBool(attrs, "feedbackEnabled"),
            IsInternalGroup = GetBool(attrs, "isInternalGroup")
        };
    }

    private static AppStoreConnectBetaTesterInfo ParseBetaTester(JsonElement item)
    {
        var attrs = GetAttributes(item);
        return new AppStoreConnectBetaTesterInfo
        {
            Id = GetString(item, "id") ?? string.Empty,
            Email = GetString(attrs, "email"),
            FirstName = GetString(attrs, "firstName"),
            LastName = GetString(attrs, "lastName"),
            InviteType = GetString(attrs, "inviteType")
        };
    }

    private static AppStoreConnectBetaAppReviewSubmissionInfo ParseBetaAppReviewSubmission(JsonElement item)
    {
        var attrs = GetAttributes(item);
        return new AppStoreConnectBetaAppReviewSubmissionInfo
        {
            Id = GetString(item, "id") ?? string.Empty,
            BetaReviewState = GetString(attrs, "betaReviewState"),
            SubmittedDate = GetDateTimeOffset(attrs, "submittedDate"),
            BuildId = GetRelationshipDataId(item, "build")
        };
    }

    private static int? GetInt32(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var prop))
            return null;
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var value))
            return value;
        return int.TryParse(prop.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}
