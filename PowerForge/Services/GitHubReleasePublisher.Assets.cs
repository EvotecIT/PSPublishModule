using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;

namespace PowerForge;

public sealed partial class GitHubReleasePublisher {
    private const int MaximumAssetUploadAttempts = 4;
    private const int MaximumAssetReconciliationAttempts = 3;

    private enum AssetUploadReconciliationState {
        Absent,
        StarterRemoved,
        Uploaded,
        LegacyStateUnavailable,
        TemporarilyUnavailable
    }

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

    private HttpResponseMessage UploadAsset(
        string uploadUrl,
        string assetPath,
        string fileName,
        string token,
        Action<long, long>? reportProgress,
        Func<AssetUploadReconciliationState> reconcileUpload,
        CancellationToken cancellationToken) {
        if (reconcileUpload is null) throw new ArgumentNullException(nameof(reconcileUpload));
        var sawTransientFailure = false;
        for (var attempt = 1; ; attempt++) {
            cancellationToken.ThrowIfCancellationRequested();
            HttpResponseMessage response;
            try {
                response = UploadAssetOnce(
                    uploadUrl,
                    assetPath,
                    fileName,
                    token,
                    reportProgress,
                    cancellationToken);
            }
            catch (Exception exception) when (IsTransientAssetUploadException(exception, cancellationToken)) {
                sawTransientFailure = true;
                ReconcileUploadWithRetry(reconcileUpload, fileName, cancellationToken);
                if (attempt >= MaximumAssetUploadAttempts) {
                    throw new InvalidOperationException(
                        $"GitHub asset upload failed for '{fileName}' after {MaximumAssetUploadAttempts} attempts. " +
                        DescribeException(exception),
                        exception);
                }

                var delay = GetAssetUploadRetryDelay(response: null, attempt);
                _logger.Warn(
                    $"GitHub release asset upload for '{fileName}' was interrupted ({DescribeException(exception)}); " +
                    $"retrying attempt {attempt + 1}/{MaximumAssetUploadAttempts} in {FormatRetryDelay(delay)}.");
                WaitBeforeAssetUploadRetry(delay, cancellationToken);
                continue;
            }

            if (IsTransientAssetUploadStatus(response.StatusCode)) {
                sawTransientFailure = true;
                var delay = GetAssetUploadRetryDelay(response, attempt);
                var statusCode = response.StatusCode;
                var reasonPhrase = response.ReasonPhrase;
                var terminalAttempt = attempt >= MaximumAssetUploadAttempts;
                if (!terminalAttempt)
                    response.Dispose();
                try {
                    ReconcileUploadWithRetry(reconcileUpload, fileName, cancellationToken);
                }
                catch {
                    if (terminalAttempt)
                        response.Dispose();
                    throw;
                }

                if (terminalAttempt)
                    return response;

                _logger.Warn(
                    $"GitHub release asset upload for '{fileName}' returned " +
                    $"{(int)statusCode} {reasonPhrase}; retrying attempt " +
                    $"{attempt + 1}/{MaximumAssetUploadAttempts} in {FormatRetryDelay(delay)}.");
                WaitBeforeAssetUploadRetry(delay, cancellationToken);
                continue;
            }

            if ((int)response.StatusCode == 422) {
                var responseText = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                if (IsAlreadyExistsValidationError(responseText, fieldName: "name")) {
                    AssetUploadReconciliationState reconciliation;
                    try {
                        reconciliation = ReconcileUploadWithRetry(
                            reconcileUpload,
                            fileName,
                            cancellationToken);
                    }
                    catch {
                        response.Dispose();
                        throw;
                    }

                    if (reconciliation == AssetUploadReconciliationState.Absent ||
                        reconciliation == AssetUploadReconciliationState.StarterRemoved) {
                        response.Dispose();
                        if (attempt >= MaximumAssetUploadAttempts) {
                            throw new InvalidOperationException(
                                $"GitHub release asset '{fileName}' remained unavailable after " +
                                $"{MaximumAssetUploadAttempts} upload attempts.");
                        }

                        var delay = GetAssetUploadRetryDelay(response: null, attempt);
                        _logger.Warn(
                            $"GitHub release asset '{fileName}' was absent or incomplete after a name collision; " +
                            $"retrying attempt {attempt + 1}/{MaximumAssetUploadAttempts} in {FormatRetryDelay(delay)}.");
                        WaitBeforeAssetUploadRetry(delay, cancellationToken);
                        continue;
                    }

                    if (reconciliation == AssetUploadReconciliationState.TemporarilyUnavailable) {
                        response.Dispose();
                        if (attempt >= MaximumAssetUploadAttempts) {
                            throw new InvalidOperationException(
                                $"GitHub release asset '{fileName}' state could not be verified after " +
                                $"{MaximumAssetUploadAttempts} upload attempts.");
                        }

                        var delay = GetAssetUploadRetryDelay(response: null, attempt);
                        _logger.Warn(
                            $"GitHub release asset '{fileName}' state remained temporarily unavailable; " +
                            $"retrying attempt {attempt + 1}/{MaximumAssetUploadAttempts} in {FormatRetryDelay(delay)}.");
                        WaitBeforeAssetUploadRetry(delay, cancellationToken);
                        continue;
                    }

                    if (sawTransientFailure) {
                        response.Dispose();
                        throw new InvalidOperationException(
                            $"GitHub release asset '{fileName}' appeared after an interrupted upload with " +
                            $"state '{reconciliation}'; refusing to accept or skip unverified bytes.");
                    }
                }
            }

            return response;
        }
    }

    private static HttpResponseMessage UploadAssetOnce(
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

    internal static bool IsTransientAssetUploadException(
        Exception exception,
        CancellationToken cancellationToken) {
        if (exception is null) throw new ArgumentNullException(nameof(exception));
        if (cancellationToken.IsCancellationRequested) return false;
        if (exception is GitHubApiRequestException apiException)
            return IsTransientAssetUploadStatus(apiException.StatusCode);
        return exception is HttpRequestException ||
               exception is IOException ||
               exception is TaskCanceledException;
    }

    internal static bool IsTransientAssetUploadStatus(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.RequestTimeout ||
           (int)statusCode == 429 ||
           (int)statusCode is >= 500 and <= 599;

    private static TimeSpan GetAssetUploadRetryDelay(HttpResponseMessage? response, int failedAttempt) {
        var retryAfter = response?.Headers.RetryAfter;
        if (retryAfter?.Delta is TimeSpan delta && delta > TimeSpan.Zero)
            return delta > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : delta;
        if (retryAfter?.Date is DateTimeOffset date) {
            var until = date - DateTimeOffset.UtcNow;
            if (until > TimeSpan.Zero)
                return until > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : until;
        }

        return TimeSpan.FromSeconds(1 << Math.Min(failedAttempt, 4));
    }

    private void WaitBeforeAssetUploadRetry(TimeSpan delay, CancellationToken cancellationToken)
        => _assetRetryDelay(delay, cancellationToken);

    private static string FormatRetryDelay(TimeSpan delay)
        => delay.TotalSeconds >= 1
            ? $"{delay.TotalSeconds:0.#}s"
            : $"{delay.TotalMilliseconds:0}ms";

    private AssetUploadReconciliationState ReconcileUploadWithRetry(
        Func<AssetUploadReconciliationState> reconcileUpload,
        string fileName,
        CancellationToken cancellationToken) {
        for (var attempt = 1; attempt <= MaximumAssetReconciliationAttempts; attempt++) {
            cancellationToken.ThrowIfCancellationRequested();
            AssetUploadReconciliationState reconciliation;
            try {
                reconciliation = reconcileUpload();
            }
            catch (Exception exception) when (IsTransientAssetUploadException(exception, cancellationToken)) {
                if (attempt >= MaximumAssetReconciliationAttempts) {
                    throw new InvalidOperationException(
                        $"GitHub release asset reconciliation for '{fileName}' failed after " +
                        $"{MaximumAssetReconciliationAttempts} attempts. {DescribeException(exception)}",
                        exception);
                }

                reconciliation = AssetUploadReconciliationState.TemporarilyUnavailable;
                _logger.Warn(
                    $"Could not reconcile interrupted GitHub release asset '{fileName}' " +
                    $"({DescribeException(exception)}).");
            }

            if (reconciliation != AssetUploadReconciliationState.TemporarilyUnavailable ||
                attempt >= MaximumAssetReconciliationAttempts)
                return reconciliation;

            var delay = GetAssetUploadRetryDelay(response: null, attempt);
            _logger.Warn(
                $"GitHub release asset reconciliation for '{fileName}' is temporarily unavailable; " +
                $"retrying check {attempt + 1}/{MaximumAssetReconciliationAttempts} in {FormatRetryDelay(delay)}.");
            WaitBeforeAssetUploadRetry(delay, cancellationToken);
        }

        return AssetUploadReconciliationState.TemporarilyUnavailable;
    }

    private void ExecuteAssetVerificationWithRetry(
        string operation,
        Action verify,
        CancellationToken cancellationToken) {
        if (verify is null) throw new ArgumentNullException(nameof(verify));

        for (var attempt = 1; attempt <= MaximumAssetUploadAttempts; attempt++) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                verify();
                return;
            }
            catch (Exception exception) when (IsTransientAssetUploadException(exception, cancellationToken)) {
                if (attempt >= MaximumAssetUploadAttempts) {
                    throw new InvalidOperationException(
                        $"{operation} failed after {MaximumAssetUploadAttempts} attempts. " +
                        DescribeException(exception),
                        exception);
                }

                var delay = GetAssetUploadRetryDelay(response: null, attempt);
                _logger.Warn(
                    $"{operation} was interrupted ({DescribeException(exception)}); retrying attempt " +
                    $"{attempt + 1}/{MaximumAssetUploadAttempts} in {FormatRetryDelay(delay)}.");
                WaitBeforeAssetUploadRetry(delay, cancellationToken);
            }
        }
    }

    private void ValidateReleaseBeforeAssetMutationWithRetry(
        string operation,
        string owner,
        string repo,
        string token,
        string apiBaseUrl,
        long releaseId,
        string tagName,
        string? expectedReleaseBodyMarker,
        string? expectedTagCommitSha,
        bool requirePublishedStableRelease,
        CancellationToken cancellationToken)
        => ExecuteAssetVerificationWithRetry(
            operation,
            () => ValidateReleaseBeforeAssetMutation(
                owner,
                repo,
                token,
                apiBaseUrl,
                releaseId,
                tagName,
                expectedReleaseBodyMarker,
                expectedTagCommitSha,
                requirePublishedStableRelease,
                cancellationToken),
            cancellationToken);

    private void ValidateUploadedAssetWithRetry(
        string operation,
        string owner,
        string repo,
        string token,
        string apiBaseUrl,
        long releaseId,
        string tagName,
        string? expectedReleaseBodyMarker,
        string? expectedTagCommitSha,
        bool requirePublishedStableRelease,
        string fileName,
        long uploadedAssetId,
        CancellationToken cancellationToken)
        => ExecuteAssetVerificationWithRetry(
            operation,
            () => {
                ValidateReleaseBeforeAssetMutation(
                    owner,
                    repo,
                    token,
                    apiBaseUrl,
                    releaseId,
                    tagName,
                    expectedReleaseBodyMarker,
                    expectedTagCommitSha,
                    requirePublishedStableRelease,
                    cancellationToken);
                ValidateCurrentAssetIdentity(
                    owner,
                    repo,
                    token,
                    apiBaseUrl,
                    releaseId,
                    fileName,
                    uploadedAssetId,
                    cancellationToken);
            },
            cancellationToken);

    private void ValidateFinalAssetSetWithRetry(
        string operation,
        string owner,
        string repo,
        string token,
        string apiBaseUrl,
        long releaseId,
        string tagName,
        string? expectedReleaseBodyMarker,
        string? expectedTagCommitSha,
        bool requirePublishedStableRelease,
        IReadOnlyDictionary<string, long> uploadedAssetIds,
        CancellationToken cancellationToken)
        => ExecuteAssetVerificationWithRetry(
            operation,
            () => {
                ValidateReleaseBeforeAssetMutation(
                    owner,
                    repo,
                    token,
                    apiBaseUrl,
                    releaseId,
                    tagName,
                    expectedReleaseBodyMarker,
                    expectedTagCommitSha,
                    requirePublishedStableRelease,
                    cancellationToken);
                ValidateFinalAssetSet(
                    owner,
                    repo,
                    token,
                    apiBaseUrl,
                    releaseId,
                    uploadedAssetIds,
                    cancellationToken);
                ValidateReleaseBeforeAssetMutation(
                    owner,
                    repo,
                    token,
                    apiBaseUrl,
                    releaseId,
                    tagName,
                    expectedReleaseBodyMarker,
                    expectedTagCommitSha,
                    requirePublishedStableRelease,
                    cancellationToken);
            },
            cancellationToken);

    internal static string DescribeException(Exception exception) {
        if (exception is null) throw new ArgumentNullException(nameof(exception));
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException) {
            if (string.IsNullOrWhiteSpace(current.Message)) continue;
            if (messages.Count == 0 ||
                !string.Equals(messages[^1], current.Message, StringComparison.Ordinal))
                messages.Add(current.Message);
        }

        return messages.Count > 0
            ? string.Join(" -> ", messages)
            : exception.GetType().Name;
    }

    private AssetUploadReconciliationState ReconcileReleaseAssetAfterUploadFailure(
        string owner,
        string repo,
        string token,
        string apiBaseUrl,
        long releaseId,
        string tagName,
        string? expectedReleaseBodyMarker,
        string? expectedTagCommitSha,
        bool requirePublishedStableRelease,
        string fileName,
        CancellationToken cancellationToken) {
        var currentAssets = ListReleaseAssets(owner, repo, token, apiBaseUrl, releaseId, cancellationToken);

        var sameNameAssets = currentAssets
            .Where(asset => string.Equals(asset.Name, fileName, StringComparison.Ordinal))
            .ToArray();
        if (sameNameAssets.Length == 0)
            return AssetUploadReconciliationState.Absent;
        if (sameNameAssets.Length > 1)
            throw new InvalidOperationException(
                $"GitHub release contains duplicate asset name '{fileName}' after an interrupted upload; recovery is ambiguous.");

        var incompleteAsset = sameNameAssets[0];
        if (string.Equals(incompleteAsset.State, "uploaded", StringComparison.OrdinalIgnoreCase))
            return AssetUploadReconciliationState.Uploaded;
        if (string.IsNullOrWhiteSpace(incompleteAsset.State))
            return AssetUploadReconciliationState.LegacyStateUnavailable;
        if (!string.Equals(incompleteAsset.State, "starter", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"GitHub release asset '{fileName}' has unexpected state '{incompleteAsset.State}'.");

        ValidateReleaseBeforeAssetMutation(
            owner,
            repo,
            token,
            apiBaseUrl,
            releaseId,
            tagName,
            expectedReleaseBodyMarker,
            expectedTagCommitSha,
            requirePublishedStableRelease,
            cancellationToken);
        if (DeleteExpectedExistingAsset(
            owner,
            repo,
            token,
            apiBaseUrl,
            releaseId,
            fileName,
            incompleteAsset.Id,
            requiredState: "starter",
            cancellationToken)) {
            _logger.Warn($"Removed incomplete GitHub release asset '{fileName}' before retrying its upload.");
            return AssetUploadReconciliationState.StarterRemoved;
        }

        return AssetUploadReconciliationState.Absent;
    }

    private sealed class GitHubApiRequestException : InvalidOperationException {
        internal GitHubApiRequestException(string message, HttpStatusCode statusCode)
            : base(message) => StatusCode = statusCode;

        internal HttpStatusCode StatusCode { get; }
    }

    private bool DeleteExpectedExistingAsset(
        string owner,
        string repo,
        string token,
        string apiBaseUrl,
        long releaseId,
        string fileName,
        long expectedAssetId,
        string? requiredState,
        CancellationToken cancellationToken) {
        if (releaseId <= 0)
            throw new InvalidOperationException("GitHub release asset replacement requires the release id returned by GitHub.");

        var asset = ListReleaseAssets(owner, repo, token, apiBaseUrl, releaseId, cancellationToken)
            .FirstOrDefault(existing => string.Equals(existing.Name, fileName, StringComparison.OrdinalIgnoreCase));
        if (asset is null)
            return false;
        ValidateExpectedAssetId(fileName, expectedAssetId, asset.Id);
        if (!string.IsNullOrWhiteSpace(requiredState) &&
            !string.Equals(asset.State, requiredState, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException(
                $"GitHub release asset '{fileName}' changed from state '{requiredState}' to " +
                $"'{asset.State ?? "<missing>"}' before deletion.");
        }

        var uri = BuildApiUri(apiBaseUrl, $"/repos/{owner}/{repo}/releases/assets/{expectedAssetId}");
        using var request = new HttpRequestMessage(HttpMethod.Delete, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = SharedClient.SendAsync(request, cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult();
        var responseText = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        using (response) {
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
                throw new GitHubApiRequestException(
                    $"GitHub asset delete failed for '{fileName}' ({(int)response.StatusCode} {response.ReasonPhrase}). {TrimForMessage(responseText)}",
                    response.StatusCode);
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
                    throw new GitHubApiRequestException(
                        $"GitHub list-release-assets failed for release '{releaseId}' ({(int)response.StatusCode} {response.ReasonPhrase}). {TrimForMessage(responseText)}",
                        response.StatusCode);
            }

            var pageAssets = Deserialize<GitHubReleaseAssetResponse[]>(responseText) ?? Array.Empty<GitHubReleaseAssetResponse>();
            assets.AddRange(pageAssets);
            if (pageAssets.Length < 100)
                break;
        }

        return assets;
    }
}
