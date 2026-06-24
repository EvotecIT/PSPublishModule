using System.Text.Json;

namespace PowerForge;

public sealed partial class AppStoreConnectClient
{
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
