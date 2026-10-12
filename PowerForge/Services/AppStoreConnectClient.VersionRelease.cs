using System.Net.Http;
using System.Text.Json;

namespace PowerForge;

public sealed partial class AppStoreConnectClient
{
    /// <summary>
    /// Sets an existing App Store version to wait for manual release after approval.
    /// This changes release policy; it does not submit the version or request public release.
    /// </summary>
    public async Task<AppStoreConnectVersionInfo> SetVersionManualReleaseAsync(
        string appStoreVersionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appStoreVersionId))
            throw new ArgumentException("App Store version id is required.", nameof(appStoreVersionId));

        var versionId = appStoreVersionId.Trim();
        var body = new
        {
            data = new
            {
                type = "appStoreVersions",
                id = versionId,
                attributes = new { releaseType = "MANUAL" }
            }
        };
        using var doc = await SendJsonAsync(
            new HttpMethod("PATCH"),
            $"appStoreVersions/{Uri.EscapeDataString(versionId)}",
            body,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("App Store Connect API request returned no response body.");
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("App Store Connect API request returned no version data.");
        if (!string.Equals(GetString(data, "type"), "appStoreVersions", StringComparison.Ordinal) ||
            !string.Equals(GetString(data, "id"), versionId, StringComparison.Ordinal))
            throw new InvalidOperationException("App Store Connect API response does not identify the requested version.");

        var version = ParseVersion(data);
        if (!string.Equals(version.ReleaseType, "MANUAL", StringComparison.Ordinal))
            throw new InvalidOperationException("App Store Connect API response does not confirm manual release policy.");
        return version;
    }

    /// <summary>
    /// Requests manual release of an approved App Store version.
    /// </summary>
    public Task<AppStoreConnectVersionReleaseRequestInfo> CreateVersionReleaseRequestAsync(
        string appStoreVersionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appStoreVersionId))
            throw new ArgumentException("App Store version id is required.", nameof(appStoreVersionId));

        var body = new
        {
            data = new
            {
                type = "appStoreVersionReleaseRequests",
                relationships = new
                {
                    appStoreVersion = new
                    {
                        data = new { type = "appStoreVersions", id = appStoreVersionId.Trim() }
                    }
                }
            }
        };

        return PostSingleAsync("appStoreVersionReleaseRequests", body, ParseVersionReleaseRequest, cancellationToken);
    }

    private static AppStoreConnectVersionReleaseRequestInfo ParseVersionReleaseRequest(JsonElement item)
    {
        return new AppStoreConnectVersionReleaseRequestInfo
        {
            Id = GetString(item, "id") ?? string.Empty,
            AppStoreVersionId = GetRelationshipDataId(item, "appStoreVersion")
        };
    }
}
