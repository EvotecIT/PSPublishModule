using System.Text.Json;

namespace PowerForge;

public sealed partial class AppStoreConnectClient
{
    /// <summary>
    /// Reads the delivery state for a package accepted by App Store Connect upload transport.
    /// </summary>
    public Task<AppStoreConnectBuildUploadInfo?> GetBuildUploadAsync(
        string buildUploadId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buildUploadId))
            throw new ArgumentException("Build upload id is required.", nameof(buildUploadId));

        return GetSingleAsync(
            $"buildUploads/{Uri.EscapeDataString(buildUploadId.Trim())}",
            ParseBuildUpload,
            cancellationToken);
    }

    private static AppStoreConnectBuildUploadInfo ParseBuildUpload(JsonElement item)
    {
        var attributes = GetAttributes(item);
        var stateElement = default(JsonElement);
        var hasState = attributes.ValueKind == JsonValueKind.Object &&
                       attributes.TryGetProperty("state", out stateElement);
        var issueContainer = hasState && stateElement.ValueKind == JsonValueKind.Object
            ? stateElement
            : attributes;
        var state = !hasState
            ? null
            : stateElement.ValueKind == JsonValueKind.Object
                ? GetString(stateElement, "state")
                : stateElement.ValueKind == JsonValueKind.String
                    ? stateElement.GetString()
                    : null;

        return new AppStoreConnectBuildUploadInfo
        {
            Id = GetString(item, "id") ?? string.Empty,
            MarketingVersion = GetString(attributes, "cfBundleShortVersionString"),
            BuildNumber = GetString(attributes, "cfBundleVersion"),
            Platform = GetString(attributes, "platform"),
            State = state,
            Errors = ParseBuildUploadIssues(issueContainer, "errors"),
            Warnings = ParseBuildUploadIssues(issueContainer, "warnings"),
            UploadedDate = GetDateTimeOffset(attributes, "uploadedDate")
        };
    }

    private static AppStoreConnectBuildUploadIssue[] ParseBuildUploadIssues(
        JsonElement state,
        string propertyName)
    {
        if (state.ValueKind != JsonValueKind.Object ||
            !state.TryGetProperty(propertyName, out var issues) ||
            issues.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<AppStoreConnectBuildUploadIssue>();
        }

        return issues.EnumerateArray()
            .Where(static issue => issue.ValueKind == JsonValueKind.Object)
            .Select(issue => new AppStoreConnectBuildUploadIssue
            {
                Code = GetString(issue, "code"),
                Description = GetString(issue, "description") ?? GetString(issue, "message")
            })
            .ToArray();
    }
}
