using System.Text.Json;

namespace PowerForge;

public sealed partial class AppStoreConnectClient
{
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
