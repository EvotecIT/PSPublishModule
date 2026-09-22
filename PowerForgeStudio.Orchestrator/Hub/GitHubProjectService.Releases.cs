using System.Text.Json;
using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Orchestrator.Hub;

public sealed partial class GitHubProjectService
{
    /// <summary>Reads at most ten recent releases and their asset download counts.</summary>
    public async Task<GitHubPage<GitHubReleaseMetric>> FetchRecentReleasesAsync(string slug, CancellationToken cancellationToken = default)
    {
        using var response = await ReadJsonAsync($"{RepositoryPath(slug)}/releases?per_page=10&page=1", cancellationToken).ConfigureAwait(false);
        var root = response.Document.RootElement;
        if (root.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("GitHub returned an invalid release listing.");
        var releases = root.EnumerateArray().Select(ParseRelease).ToArray();
        return new GitHubPage<GitHubReleaseMetric>(releases, response.HasNext ?? releases.Length >= 10);
    }

    private static GitHubReleaseMetric ParseRelease(JsonElement element)
    {
        if (!element.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var id) || id <= 0 ||
            !element.TryGetProperty("tag_name", out var tagElement) || tagElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(tagElement.GetString()))
            throw new InvalidDataException("GitHub returned a release without an identity.");
        var tag = tagElement.GetString()!;
        var name = ReadString(element, "name") is { Length: > 0 } title ? title : tag;
        var assets = new List<GitHubReleaseAssetMetric>();
        var inventoryComplete = element.TryGetProperty("assets", out var assetElements) && assetElements.ValueKind == JsonValueKind.Array;
        if (inventoryComplete)
        {
            foreach (var asset in assetElements.EnumerateArray().Take(100))
            {
                var assetName = ReadString(asset, "name");
                if (string.IsNullOrWhiteSpace(assetName)) continue;
                assets.Add(new GitHubReleaseAssetMetric(
                    assetName,
                    ReadNonNegativeInt64(asset, "download_count"),
                    ReadNonNegativeInt64(asset, "size")));
            }
            inventoryComplete = assetElements.GetArrayLength() < 100;
        }
        var published = DateTimeOffset.TryParse(ReadString(element, "published_at"), out var timestamp)
            ? timestamp.ToUniversalTime() : (DateTimeOffset?)null;
        return new GitHubReleaseMetric(id, tag, name, ReadString(element, "html_url"), published,
            ReadBool(element, "draft"), ReadBool(element, "prerelease"), assets, inventoryComplete);
    }

    private static string? ReadString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static long? ReadNonNegativeInt64(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.TryGetInt64(out var number) && number >= 0
            ? number : null;

    private static bool ReadBool(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;
}
