using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;

namespace PowerForge;

public sealed partial class GitHubReleasePublisher {
    private IReadOnlyList<GitHubReleaseAssetResponse> ListReleaseAssetsWithRetry(
        string operation,
        string owner,
        string repo,
        string token,
        string apiBaseUrl,
        long releaseId,
        CancellationToken cancellationToken) {
        IReadOnlyList<GitHubReleaseAssetResponse>? assets = null;
        ExecuteAssetVerificationWithRetry(
            operation,
            () => assets = ListReleaseAssets(owner, repo, token, apiBaseUrl, releaseId, cancellationToken),
            cancellationToken);
        return assets ?? throw new InvalidOperationException($"{operation} returned no asset inventory.");
    }

    private long ReadCurrentAssetIdWithRetry(
        string operation,
        string owner,
        string repo,
        string token,
        string apiBaseUrl,
        long releaseId,
        string fileName,
        CancellationToken cancellationToken) {
        long assetId = 0;
        ExecuteAssetVerificationWithRetry(
            operation,
            () => assetId = ReadCurrentAssetId(
                owner,
                repo,
                token,
                apiBaseUrl,
                releaseId,
                fileName,
                cancellationToken),
            cancellationToken);
        return assetId;
    }

    private static long ReadCurrentAssetId(
        string owner,
        string repo,
        string token,
        string apiBaseUrl,
        long releaseId,
        string fileName,
        CancellationToken cancellationToken) {
        var asset = FindUniqueReleaseAsset(
            ListReleaseAssets(owner, repo, token, apiBaseUrl, releaseId, cancellationToken),
            fileName);
        return asset?.Id ?? throw new InvalidOperationException(
            $"GitHub release asset '{fileName}' was not present when its identity was verified.");
    }

    private static GitHubReleaseAssetResponse? FindUniqueReleaseAsset(
        IEnumerable<GitHubReleaseAssetResponse> assets,
        string fileName) {
        var matches = assets
            .Where(asset => string.Equals(asset.Name, fileName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length > 1)
            throw new InvalidOperationException(
                $"GitHub release contains duplicate asset name '{fileName}'; identity is ambiguous.");
        return matches.Length == 0 ? null : matches[0];
    }

    private bool DeleteExpectedExistingAssetWithRetry(
        string operation,
        string owner,
        string repo,
        string token,
        string apiBaseUrl,
        long releaseId,
        string fileName,
        long expectedAssetId,
        string? requiredState,
        CancellationToken cancellationToken) {
        var deleteMayHaveSucceeded = false;
        for (var attempt = 1; attempt <= MaximumAssetUploadAttempts; attempt++) {
            cancellationToken.ThrowIfCancellationRequested();
            var asset = FindUniqueReleaseAsset(
                ListReleaseAssetsWithRetry(
                    $"{operation} identity verification",
                    owner,
                    repo,
                    token,
                    apiBaseUrl,
                    releaseId,
                    cancellationToken),
                fileName);
            if (asset is null) {
                if (deleteMayHaveSucceeded) {
                    _logger.Info(
                        $"Confirmed GitHub release asset '{fileName}' is absent after an interrupted deletion.");
                    return true;
                }

                throw new InvalidOperationException(
                    $"GitHub release asset '{fileName}' disappeared before deletion of expected id {expectedAssetId}.");
            }

            ValidateExpectedAssetId(fileName, expectedAssetId, asset.Id);
            if (!string.IsNullOrWhiteSpace(requiredState) &&
                !string.Equals(asset.State, requiredState, StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException(
                    $"GitHub release asset '{fileName}' changed from state '{requiredState}' to " +
                    $"'{asset.State ?? "<missing>"}' before deletion.");
            }

            try {
                var uri = BuildApiUri(apiBaseUrl, $"/repos/{owner}/{repo}/releases/assets/{expectedAssetId}");
                using var request = new HttpRequestMessage(HttpMethod.Delete, uri);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                deleteMayHaveSucceeded = true;

                using var response = SharedClient.SendAsync(request, cancellationToken)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
                var responseText = response.Content.ReadAsStringAsync()
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
                if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound) {
                    throw new GitHubApiRequestException(
                        $"GitHub asset delete failed for '{fileName}' ({(int)response.StatusCode} {response.ReasonPhrase}). {TrimForMessage(responseText)}",
                        response.StatusCode,
                        GetAssetRetryAfterDelay(response));
                }

                var remainingAsset = FindUniqueReleaseAsset(
                    ListReleaseAssetsWithRetry(
                        $"{operation} post-delete verification",
                        owner,
                        repo,
                        token,
                        apiBaseUrl,
                        releaseId,
                        cancellationToken),
                    fileName);
                if (remainingAsset is not null) {
                    ValidateExpectedAssetId(fileName, expectedAssetId, remainingAsset.Id);
                    throw new InvalidOperationException(
                        $"GitHub release asset '{fileName}' remained present after deletion of id {expectedAssetId}.");
                }

                _logger.Info($"Deleted existing GitHub release asset before replacement: {fileName}");
                return true;
            }
            catch (Exception exception) when (IsTransientAssetUploadException(exception, cancellationToken)) {
                if (attempt >= MaximumAssetUploadAttempts) {
                    throw new InvalidOperationException(
                        $"{operation} failed after {MaximumAssetUploadAttempts} attempts. " +
                        DescribeException(exception),
                        exception);
                }

                var delay = GetAssetUploadRetryDelay(exception, attempt);
                _logger.Warn(
                    $"{operation} was interrupted ({DescribeException(exception)}); retrying attempt " +
                    $"{attempt + 1}/{MaximumAssetUploadAttempts} in {FormatRetryDelay(delay)}.");
                WaitBeforeAssetUploadRetry(delay, cancellationToken);
            }
        }

        throw new InvalidOperationException($"{operation} did not complete.");
    }
}
