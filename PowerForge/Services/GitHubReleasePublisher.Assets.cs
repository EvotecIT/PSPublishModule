using System;
using System.Collections.Generic;
using System.Globalization;
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
        ValidateExpectedAssetId(
            fileName,
            expectedAssetId,
            ReadCurrentAssetId(owner, repo, token, apiBaseUrl, releaseId, fileName, cancellationToken));
    }

    private static void ValidateFinalAssetSet(
        string owner,
        string repo,
        string token,
        string apiBaseUrl,
        long releaseId,
        IReadOnlyDictionary<string, long> expectedAssets,
        bool requireExactAssetSet,
        CancellationToken cancellationToken) {
        var currentAssets = ListReleaseAssets(owner, repo, token, apiBaseUrl, releaseId, cancellationToken);
        if (requireExactAssetSet) {
            var current = CreateReplaceableAssetMap(currentAssets);
            ValidateExistingAssetNamesAuthorized(current.Keys, new HashSet<string>(
                expectedAssets.Keys,
                StringComparer.OrdinalIgnoreCase));
            if (current.Count != expectedAssets.Count)
                throw new InvalidOperationException(
                    $"GitHub release asset reconciliation returned {current.Count} asset(s), expected {expectedAssets.Count}.");
        }

        foreach (var expected in expectedAssets) {
            var current = FindUniqueReleaseAsset(currentAssets, expected.Key);
            if (current is null)
                throw new InvalidOperationException(
                    $"GitHub release asset '{expected.Key}' disappeared before recovery completed.");
            ValidateExpectedAssetId(expected.Key, expected.Value, current.Id);
        }
    }

    private HttpResponseMessage UploadAsset(
        string uploadUrl,
        string assetPath,
        string fileName,
        string token,
        Action<long, long>? reportProgress,
        Action validateReleaseBeforeRetry,
        Func<AssetUploadReconciliationState> reconcileUpload,
        CancellationToken cancellationToken) {
        if (validateReleaseBeforeRetry is null) throw new ArgumentNullException(nameof(validateReleaseBeforeRetry));
        if (reconcileUpload is null) throw new ArgumentNullException(nameof(reconcileUpload));
        var sawTransientFailure = false;
        for (var attempt = 1; ; attempt++) {
            cancellationToken.ThrowIfCancellationRequested();
            if (attempt > 1)
                validateReleaseBeforeRetry();
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

            var forbiddenResponseText = response.StatusCode == HttpStatusCode.Forbidden
                ? response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult()
                : null;
            if (IsTransientAssetUploadResponse(response, forbiddenResponseText)) {
                sawTransientFailure = true;
                var serverDelay = GetAssetRetryAfterDelay(response, forbiddenResponseText);
                var delay = serverDelay ?? GetAssetUploadExponentialDelay(attempt);
                var statusCode = response.StatusCode;
                var reasonPhrase = response.ReasonPhrase;
                var terminalAttempt = attempt >= MaximumAssetUploadAttempts;
                var waitBeforeReconciliation = serverDelay.HasValue ||
                                               (int)statusCode == 429 ||
                                               statusCode == HttpStatusCode.Forbidden;
                if (terminalAttempt && waitBeforeReconciliation)
                    return response;

                if (!terminalAttempt)
                    response.Dispose();
                if (waitBeforeReconciliation) {
                    _logger.Warn(
                        $"GitHub release asset upload for '{fileName}' returned " +
                        $"{(int)statusCode} {reasonPhrase}; waiting {FormatRetryDelay(delay)} before reconciliation.");
                }

                try {
                    if (waitBeforeReconciliation)
                        WaitBeforeAssetUploadRetry(delay, cancellationToken);
                    ReconcileUploadWithRetry(reconcileUpload, fileName, cancellationToken);
                }
                catch {
                    if (terminalAttempt)
                        response.Dispose();
                    throw;
                }

                if (terminalAttempt)
                    return response;

                if (!waitBeforeReconciliation) {
                    _logger.Warn(
                        $"GitHub release asset upload for '{fileName}' returned " +
                        $"{(int)statusCode} {reasonPhrase}; retrying attempt " +
                        $"{attempt + 1}/{MaximumAssetUploadAttempts} in {FormatRetryDelay(delay)}.");
                    WaitBeforeAssetUploadRetry(delay, cancellationToken);
                }
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

                    if (reconciliation == AssetUploadReconciliationState.Absent) {
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
            return IsTransientAssetUploadStatus(apiException.StatusCode) ||
                   (apiException.StatusCode == HttpStatusCode.Forbidden && apiException.RetryAfter.HasValue);
        return exception is HttpRequestException ||
               exception is IOException ||
               exception is TaskCanceledException;
    }

    internal static bool IsTransientAssetUploadStatus(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.RequestTimeout ||
           (int)statusCode == 429 ||
           (int)statusCode is >= 500 and <= 599;

    internal static bool IsTransientAssetUploadResponse(
        HttpResponseMessage response,
        string? responseText = null) {
        if (response is null) throw new ArgumentNullException(nameof(response));
        return IsTransientAssetUploadStatus(response.StatusCode) ||
               (response.StatusCode == HttpStatusCode.Forbidden &&
                GetAssetRetryAfterDelay(response, responseText).HasValue);
    }

    private static TimeSpan GetAssetUploadRetryDelay(HttpResponseMessage? response, int failedAttempt)
        => GetAssetRetryAfterDelay(response) ?? GetAssetUploadExponentialDelay(failedAttempt);

    private static TimeSpan GetAssetUploadRetryDelay(Exception exception, int failedAttempt) {
        if (exception is GitHubApiRequestException apiException &&
            apiException.RetryAfter is TimeSpan retryAfter)
            return retryAfter;

        return GetAssetUploadExponentialDelay(failedAttempt);
    }

    internal static TimeSpan? GetAssetRetryAfterDelay(
        HttpResponseMessage? response,
        string? responseText = null) {
        var retryAfter = response?.Headers.RetryAfter;
        if (retryAfter?.Delta is TimeSpan delta && delta > TimeSpan.Zero)
            return delta;
        if (retryAfter?.Date is DateTimeOffset date) {
            var until = date - DateTimeOffset.UtcNow;
            if (until > TimeSpan.Zero)
                return until;
        }

        if (response?.StatusCode == HttpStatusCode.Forbidden &&
            TryGetSingleInt64Header(response, "X-RateLimit-Remaining", out var remaining) &&
            remaining == 0 &&
            TryGetSingleInt64Header(response, "X-RateLimit-Reset", out var resetUnixSeconds)) {
            try {
                var untilReset = DateTimeOffset.FromUnixTimeSeconds(resetUnixSeconds) - DateTimeOffset.UtcNow;
                if (untilReset > TimeSpan.Zero)
                    return untilReset + TimeSpan.FromSeconds(1);
            }
            catch (ArgumentOutOfRangeException) {
                return null;
            }
        }

        if ((int?)response?.StatusCode == 429 ||
            (response?.StatusCode == HttpStatusCode.Forbidden &&
             IsSecondaryRateLimitResponse(responseText)))
            return TimeSpan.FromMinutes(1);

        return null;
    }

    private static bool IsSecondaryRateLimitResponse(string? responseText)
        => (responseText?.IndexOf("secondary rate limit", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;

    private static bool TryGetSingleInt64Header(
        HttpResponseMessage response,
        string headerName,
        out long value) {
        value = 0;
        if (!response.Headers.TryGetValues(headerName, out var values))
            return false;
        var text = values.FirstOrDefault();
        return text is not null &&
               long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static TimeSpan GetAssetUploadExponentialDelay(int failedAttempt)
        => TimeSpan.FromSeconds(1 << Math.Min(failedAttempt, 4));

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
            Exception? transientException = null;
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

                transientException = exception;
                reconciliation = AssetUploadReconciliationState.TemporarilyUnavailable;
                _logger.Warn(
                    $"Could not reconcile interrupted GitHub release asset '{fileName}' " +
                    $"({DescribeException(exception)}).");
            }

            if (reconciliation != AssetUploadReconciliationState.TemporarilyUnavailable ||
                attempt >= MaximumAssetReconciliationAttempts)
                return reconciliation;

            var delay = transientException is null
                ? GetAssetUploadRetryDelay(response: null, attempt)
                : GetAssetUploadRetryDelay(transientException, attempt);
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

                var delay = GetAssetUploadRetryDelay(exception, attempt);
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
        IReadOnlyDictionary<string, long> verifiedAssetIds,
        bool requireExactAssetSet,
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
                    verifiedAssetIds,
                    requireExactAssetSet,
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
                !string.Equals(messages[messages.Count - 1], current.Message, StringComparison.Ordinal))
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
        string fileName,
        CancellationToken cancellationToken) {
        var currentAssets = ListReleaseAssets(owner, repo, token, apiBaseUrl, releaseId, cancellationToken);

        var sameNameAssets = currentAssets
            .Where(asset => string.Equals(asset.Name, fileName, StringComparison.OrdinalIgnoreCase))
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

        throw new InvalidOperationException(
            $"GitHub release asset '{fileName}' is in state 'starter' after an interrupted upload. " +
            "Its publisher cannot be proven, so recovery refuses to delete it.");
    }

    private sealed class GitHubApiRequestException : InvalidOperationException {
        internal GitHubApiRequestException(
            string message,
            HttpStatusCode statusCode,
            TimeSpan? retryAfter = null)
            : base(message) {
            StatusCode = statusCode;
            RetryAfter = retryAfter is TimeSpan delay && delay > TimeSpan.Zero ? delay : null;
        }

        internal HttpStatusCode StatusCode { get; }
        internal TimeSpan? RetryAfter { get; }
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
                        response.StatusCode,
                        GetAssetRetryAfterDelay(response, responseText));
            }

            var pageAssets = Deserialize<GitHubReleaseAssetResponse[]>(responseText) ?? Array.Empty<GitHubReleaseAssetResponse>();
            assets.AddRange(pageAssets);
            if (pageAssets.Length < 100)
                break;
        }

        return assets;
    }
}
