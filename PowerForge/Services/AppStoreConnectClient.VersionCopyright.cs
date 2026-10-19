using System.Net.Http;
using System.Text.Json;

namespace PowerForge;

public sealed partial class AppStoreConnectClient
{
    /// <summary>Sets a version's publisher-supplied copyright notice without changing release policy or its selected build.</summary>
    public async Task<AppStoreConnectVersionInfo> SetVersionCopyrightAsync(
        string appStoreVersionId, string copyright, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appStoreVersionId))
            throw new ArgumentException("App Store version id is required.", nameof(appStoreVersionId));
        if (string.IsNullOrWhiteSpace(copyright))
            throw new ArgumentException("Publisher copyright is required.", nameof(copyright));
        var id = appStoreVersionId.Trim();
        var notice = copyright.Trim();
        var body = new { data = new { type = "appStoreVersions", id, attributes = new { copyright = notice } } };
        using var doc = await SendJsonAsync(new HttpMethod("PATCH"), $"appStoreVersions/{Uri.EscapeDataString(id)}", body, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("App Store Connect API request returned no response body.");
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
            !string.Equals(GetString(data, "type"), "appStoreVersions", StringComparison.Ordinal) ||
            !string.Equals(GetString(data, "id"), id, StringComparison.Ordinal))
            throw new InvalidOperationException("App Store Connect API response does not identify the requested version.");
        var version = ParseVersion(data);
        if (!string.Equals(version.Copyright, notice, StringComparison.Ordinal))
            throw new InvalidOperationException("App Store Connect API response does not confirm the requested copyright notice.");
        return version;
    }
}
