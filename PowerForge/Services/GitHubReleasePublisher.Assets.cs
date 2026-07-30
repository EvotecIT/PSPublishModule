using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;

namespace PowerForge;

public sealed partial class GitHubReleasePublisher {
    private static void ReportAssetProgress(
        IGitHubReleaseProgressReporter? progress,
        string assetPath,
        int zeroBasedPosition,
        int totalAssets,
        GitHubReleaseAssetProgressState state,
        long bytesTransferred = 0,
        long totalBytes = 0,
        string? detail = null) {
        if (progress is null)
            return;

        try {
            progress.Report(new GitHubReleaseAssetProgress {
                FilePath = assetPath,
                FileName = Path.GetFileName(assetPath) ?? assetPath,
                Position = zeroBasedPosition + 1,
                TotalAssets = totalAssets,
                State = state,
                BytesTransferred = bytesTransferred,
                TotalBytes = totalBytes,
                Detail = detail
            });
        }
        catch {
            // Progress is best effort and must never change release correctness.
        }
    }

    internal static HashSet<string> CreateAuthorizedAssetNameSet(IEnumerable<string> assetPaths) {
        if (assetPaths is null) throw new ArgumentNullException(nameof(assetPaths));
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var assetPath in assetPaths) {
            var fileName = Path.GetFileName(assetPath);
            if (string.IsNullOrWhiteSpace(fileName))
                throw new InvalidOperationException($"GitHub release asset path has no file name: '{assetPath}'.");
            if (!names.Add(fileName!))
                throw new InvalidOperationException($"GitHub release asset name '{fileName}' is duplicated.");
        }

        return names;
    }

    internal static void ValidateExistingAssetNamesAuthorized(
        IEnumerable<string> existingAssetNames,
        ISet<string> authorizedAssetNames) {
        if (existingAssetNames is null) throw new ArgumentNullException(nameof(existingAssetNames));
        if (authorizedAssetNames is null) throw new ArgumentNullException(nameof(authorizedAssetNames));

        var unauthorized = existingAssetNames
            .Where(name => !string.IsNullOrWhiteSpace(name) && !authorizedAssetNames.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (unauthorized.Length > 0)
            throw new InvalidOperationException(
                "The existing GitHub release contains asset names outside the authorized recovery set: " +
                string.Join(", ", unauthorized));
    }

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

    private static long ReadUploadedAssetId(string fileName, string responseText) {
        var uploaded = Deserialize<GitHubReleaseAssetResponse>(responseText);
        if (uploaded is null ||
            uploaded.Id <= 0 ||
            !string.Equals(uploaded.Name, fileName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"GitHub upload response did not bind '{fileName}' to an exact asset identity.");
        }

        return uploaded.Id;
    }

    private static void ValidateCurrentAssetIdentity(
        string owner,
        string repo,
        string token,
        string apiBaseUrl,
        long releaseId,
        string fileName,
        long expectedAssetId,
        CancellationToken cancellationToken) {
        var current = ListReleaseAssets(owner, repo, token, apiBaseUrl, releaseId, cancellationToken)
            .Where(asset => string.Equals(asset.Name, fileName, StringComparison.Ordinal))
            .ToArray();
        if (current.Length != 1)
            throw new InvalidOperationException(
                $"GitHub release asset '{fileName}' was not uniquely present after upload.");
        ValidateExpectedAssetId(fileName, expectedAssetId, current[0].Id);
    }

    private static void ValidateFinalAssetSet(
        string owner,
        string repo,
        string token,
        string apiBaseUrl,
        long releaseId,
        IReadOnlyDictionary<string, long> expectedAssets,
        CancellationToken cancellationToken) {
        var current = CreateReplaceableAssetMap(
            ListReleaseAssets(owner, repo, token, apiBaseUrl, releaseId, cancellationToken));
        ValidateExistingAssetNamesAuthorized(current.Keys, new HashSet<string>(
            expectedAssets.Keys,
            StringComparer.OrdinalIgnoreCase));
        if (current.Count != expectedAssets.Count)
            throw new InvalidOperationException(
                $"GitHub release asset reconciliation returned {current.Count} asset(s), expected {expectedAssets.Count}.");

        foreach (var expected in expectedAssets) {
            if (!current.TryGetValue(expected.Key, out var actualId))
                throw new InvalidOperationException(
                    $"GitHub release asset '{expected.Key}' disappeared before recovery completed.");
            ValidateExpectedAssetId(expected.Key, expected.Value, actualId);
        }
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
