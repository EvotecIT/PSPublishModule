using System.Net.Http;
using System.Text.Json;

namespace PowerForge;

public sealed partial class AppStoreConnectClient
{
    /// <summary>
    /// Sets an existing build's publisher-reviewed non-exempt encryption declaration.
    /// The caller must determine the appropriate declaration; this method does not classify encryption or attach documentation.
    /// </summary>
    public async Task<AppStoreConnectBuildInfo> SetBuildUsesNonExemptEncryptionAsync(
        string buildId,
        bool usesNonExemptEncryption,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buildId))
            throw new ArgumentException("Build id is required.", nameof(buildId));

        var id = buildId.Trim();
        var body = new
        {
            data = new
            {
                type = "builds",
                id,
                attributes = new { usesNonExemptEncryption }
            }
        };
        using var doc = await SendJsonAsync(
            new HttpMethod("PATCH"),
            $"builds/{Uri.EscapeDataString(id)}",
            body,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("App Store Connect API request returned no response body.");
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("App Store Connect API request returned no build data.");
        if (!string.Equals(GetString(data, "type"), "builds", StringComparison.Ordinal) ||
            !string.Equals(GetString(data, "id"), id, StringComparison.Ordinal))
            throw new InvalidOperationException("App Store Connect API response does not identify the requested build.");

        var build = ParseBuild(data, ReadIncludedPreReleaseVersions(doc.RootElement));
        if (build.UsesNonExemptEncryption != usesNonExemptEncryption)
            throw new InvalidOperationException("App Store Connect API response does not confirm the requested encryption declaration.");
        return build;
    }

    /// <summary>
    /// Reads a build by App Store Connect build id.
    /// </summary>
    public async Task<AppStoreConnectBuildInfo?> GetBuildAsync(
        string buildId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buildId))
            throw new ArgumentException("Build id is required.", nameof(buildId));

        using var doc = await GetJsonAsync(
            $"builds/{Uri.EscapeDataString(buildId.Trim())}?include=preReleaseVersion",
            cancellationToken,
            returnNullOnNotFound: true).ConfigureAwait(false);
        if (doc is null ||
            !doc.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind == JsonValueKind.Null)
            return null;

        return ParseBuild(data, ReadIncludedPreReleaseVersions(doc.RootElement));
    }
}
