using System.Text.Json;

namespace PowerForge;

public sealed partial class AppStoreConnectClient
{
    // Resolve the item's explicit relationship through Apple's version resource. Opaque item
    // IDs are compatibility hints for submission, never authority for cancelling other items.
    internal async Task<(AppStoreConnectVersionInfo Version, string? AppId, string? BuildId)?> GetReviewWithdrawalVersionAsync(
        string appStoreVersionRelationshipId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(appStoreVersionRelationshipId))
            throw new ArgumentException("App Store version relationship id is required.", nameof(appStoreVersionRelationshipId));
        var query = new Dictionary<string, string?>
        {
            ["fields[appStoreVersions]"] = "versionString,platform,appStoreState,appVersionState,app,build"
        };
        using var document = await GetJsonAsync(
            $"appStoreVersions/{Uri.EscapeDataString(appStoreVersionRelationshipId.Trim())}" + BuildQuery(query),
            cancellationToken, returnNullOnNotFound: true).ConfigureAwait(false);
        if (document is null || !document.RootElement.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
            return null;
        if (data.ValueKind != JsonValueKind.Object || GetString(data, "type") != "appStoreVersions" ||
            !data.TryGetProperty("relationships", out var relationships) ||
            !relationships.TryGetProperty("app", out var app) || !app.TryGetProperty("data", out var appData) ||
            GetString(appData, "type") != "apps" ||
            !relationships.TryGetProperty("build", out var build) || !build.TryGetProperty("data", out var buildData) ||
            GetString(buildData, "type") != "builds")
            throw new InvalidOperationException("The review item's version resource did not return an unambiguous app/build binding.");
        return (ParseVersion(data), GetString(appData, "id"), GetString(buildData, "id"));
    }

    /// <summary>Reads one review submission, returning null when it no longer exists.</summary>
    public async Task<AppStoreConnectReviewSubmissionInfo?> GetReviewSubmissionAsync(
        string reviewSubmissionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reviewSubmissionId))
            throw new ArgumentException("Review submission id is required.", nameof(reviewSubmissionId));
        using var document = await GetJsonAsync(
            $"reviewSubmissions/{Uri.EscapeDataString(reviewSubmissionId.Trim())}",
            cancellationToken,
            returnNullOnNotFound: true).ConfigureAwait(false);
        if (document is null || !document.RootElement.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
            return null;
        return ParseReviewSubmission(data);
    }

    /// <summary>
    /// Cancels the entire submission, including all its items. Use the review submission
    /// service's confirmed, single-version withdrawal when changing a selected build.
    /// </summary>
    public Task<AppStoreConnectReviewSubmissionInfo> CancelReviewSubmissionAsync(
        string reviewSubmissionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reviewSubmissionId))
            throw new ArgumentException("Review submission id is required.", nameof(reviewSubmissionId));
        var id = reviewSubmissionId.Trim();
        var body = new
        {
            data = new { type = "reviewSubmissions", id, attributes = new { canceled = true } }
        };
        return PatchSingleAsync($"reviewSubmissions/{Uri.EscapeDataString(id)}", body, ParseReviewSubmission, cancellationToken);
    }
}
