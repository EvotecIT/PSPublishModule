using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;

namespace PowerForge;

public sealed partial class GitHubReleasePublisher {
    internal static bool TryReserveExistingAssetForReplacement(
        IDictionary<string, long> replaceableAssets,
        string fileName,
        out long assetId) {
        if (replaceableAssets is null) throw new ArgumentNullException(nameof(replaceableAssets));
        assetId = 0;
        if (string.IsNullOrWhiteSpace(fileName)) return false;

        if (!replaceableAssets.TryGetValue(fileName, out assetId)) return false;
        replaceableAssets.Remove(fileName);
        return true;
    }

    private static Dictionary<string, long> CreateReplaceableAssetMap(IEnumerable<GitHubReleaseAssetResponse> existingAssets) {
        var assets = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in existingAssets) {
            if (string.IsNullOrWhiteSpace(asset.Name)) continue;
            if (assets.ContainsKey(asset.Name!))
                throw new InvalidOperationException($"GitHub release contains duplicate asset name '{asset.Name}'; replacement is ambiguous.");
            assets.Add(asset.Name!, asset.Id);
        }

        return assets;
    }

    internal static void ValidateExpectedAssetId(string fileName, long expectedAssetId, long actualAssetId) {
        if (actualAssetId == expectedAssetId) return;
        throw new InvalidOperationException(
            $"GitHub release asset '{fileName}' changed from id {expectedAssetId} to {actualAssetId} after the verified replacement snapshot.");
    }

    private static HttpResponseMessage UploadAsset(
        string uploadUrl,
        string assetPath,
        string fileName,
        string token,
        Action<long, long>? reportProgress,
        CancellationToken cancellationToken) {
        var target = new Uri(uploadUrl + "?name=" + Uri.EscapeDataString(fileName));

        using var content = new GitHubReleaseProgressFileContent(assetPath, reportProgress);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var request = new HttpRequestMessage(HttpMethod.Post, target) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return SharedClient.SendAsync(request, cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult();
    }

    private bool DeleteExpectedExistingAsset(
        string owner,
        string repo,
        string token,
        string apiBaseUrl,
        long releaseId,
        string fileName,
        long expectedAssetId,
        CancellationToken cancellationToken) {
        if (releaseId <= 0)
            throw new InvalidOperationException("GitHub release asset replacement requires the release id returned by GitHub.");

        var asset = ListReleaseAssets(owner, repo, token, apiBaseUrl, releaseId, cancellationToken)
            .FirstOrDefault(existing => string.Equals(existing.Name, fileName, StringComparison.OrdinalIgnoreCase));
        if (asset is null)
            return false;
        ValidateExpectedAssetId(fileName, expectedAssetId, asset.Id);

        var uri = BuildApiUri(apiBaseUrl, $"/repos/{owner}/{repo}/releases/assets/{expectedAssetId}");
        using var request = new HttpRequestMessage(HttpMethod.Delete, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = SharedClient.SendAsync(request, cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult();
        var responseText = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        using (response) {
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
                throw new InvalidOperationException($"GitHub asset delete failed for '{fileName}' ({(int)response.StatusCode} {response.ReasonPhrase}). {TrimForMessage(responseText)}");
        }

        _logger.Info($"Deleted existing GitHub release asset before replacement: {fileName}");
        return true;
    }

    private static IReadOnlyList<GitHubReleaseAssetResponse> ListReleaseAssets(
        string owner,
        string repo,
        string token,
        string apiBaseUrl,
        long releaseId,
        CancellationToken cancellationToken) {
        var assets = new List<GitHubReleaseAssetResponse>();
        for (var page = 1; ; page++) {
            cancellationToken.ThrowIfCancellationRequested();
            var uri = BuildApiUri(apiBaseUrl, $"/repos/{owner}/{repo}/releases/{releaseId}/assets?per_page=100&page={page}");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = SharedClient.SendAsync(request, cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult();
            var responseText = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            using (response) {
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException($"GitHub list-release-assets failed for release '{releaseId}' ({(int)response.StatusCode} {response.ReasonPhrase}). {TrimForMessage(responseText)}");
            }

            var pageAssets = Deserialize<GitHubReleaseAssetResponse[]>(responseText) ?? Array.Empty<GitHubReleaseAssetResponse>();
            assets.AddRange(pageAssets);
            if (pageAssets.Length < 100)
                break;
        }

        return assets;
    }
}
