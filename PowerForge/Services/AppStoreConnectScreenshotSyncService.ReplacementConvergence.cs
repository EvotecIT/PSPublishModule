namespace PowerForge;

public sealed partial class AppStoreConnectScreenshotSyncService
{
    private static readonly TimeSpan ReplacementInventoryPollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DefaultReplacementInventoryTimeout = TimeSpan.FromMinutes(2);

    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly TimeSpan _replacementInventoryTimeout;

    internal AppStoreConnectScreenshotSyncService(
        AppStoreConnectClient client,
        Func<TimeSpan, CancellationToken, Task> delay,
        TimeSpan? replacementInventoryTimeout = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
        _replacementInventoryTimeout = replacementInventoryTimeout ?? DefaultReplacementInventoryTimeout;
        if (_replacementInventoryTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(replacementInventoryTimeout), "Replacement inventory timeout must be positive.");
    }

    private static ReplacementInventoryExpectation CreateReplacementInventoryExpectation(
        string screenshotSetId,
        string displayType,
        IReadOnlyCollection<AppStoreConnectScreenshotInfo> replacedScreenshots,
        IReadOnlyCollection<AppStoreConnectScreenshotUploadResult> uploaded,
        IReadOnlyList<string> expectedChecksums)
    {
        var expectedIds = uploaded
            .Select(static upload => upload.Screenshot.Id)
            .ToArray();
        if (expectedIds.Length != expectedChecksums.Count)
            throw new InvalidOperationException($"Screenshot replacement evidence for '{displayType}' is incomplete.");

        return new ReplacementInventoryExpectation(
            screenshotSetId,
            displayType,
            replacedScreenshots.ToDictionary(static screenshot => screenshot.Id, StringComparer.Ordinal),
            expectedIds,
            expectedIds
                .Select((id, index) => new KeyValuePair<string, string>(id, expectedChecksums[index]))
                .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal));
    }

    private async Task WaitForExactReplacementInventoryAsync(
        ReplacementInventoryExpectation expectation,
        ScreenshotSnapshot screenshotSnapshot,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_replacementInventoryTimeout);
        var boundedToken = timeoutSource.Token;

        try
        {
            while (true)
            {
                screenshotSnapshot.ValidateUnchanged();
                var finalScreenshots = await _client.GetScreenshotsAsync(
                        expectation.ScreenshotSetId,
                        limit: 200,
                        boundedToken)
                    .ConfigureAwait(false);
                if (expectation.IsExact(finalScreenshots))
                    return;

                if (expectation.FindConcurrentMutation(finalScreenshots) is not null)
                {
                    throw new InvalidOperationException(
                        $"App Store Connect screenshot inventory for '{expectation.DisplayType}' changed during replacement. " +
                        "The final remote inventory contains an asset outside the approved replacement operation; review and run a new plan before submission.");
                }

                await _delay(ReplacementInventoryPollInterval, boundedToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested &&
            timeoutSource.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                $"App Store Connect screenshot inventory for '{expectation.DisplayType}' did not converge within {_replacementInventoryTimeout}. " +
                "The final remote inventory could not be proven to match the approved screenshot bytes; review and run a new plan before submission.");
        }

    }

    private async Task ValidateExactReplacementInventoriesAsync(
        IReadOnlyCollection<ReplacementInventoryExpectation> expectations,
        ScreenshotSnapshot screenshotSnapshot,
        CancellationToken cancellationToken)
    {
        if (expectations.Count <= 1)
            return;

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_replacementInventoryTimeout);
        var boundedToken = timeoutSource.Token;

        try
        {
            while (true)
            {
                var allExact = true;
                foreach (var expectation in expectations)
                {
                    screenshotSnapshot.ValidateUnchanged();
                    var screenshots = await _client.GetScreenshotsAsync(
                            expectation.ScreenshotSetId,
                            limit: 200,
                            boundedToken)
                        .ConfigureAwait(false);
                    if (expectation.IsExact(screenshots))
                        continue;

                    if (expectation.FindConcurrentMutation(screenshots) is not null)
                    {
                        throw new InvalidOperationException(
                            $"App Store Connect screenshot inventory for '{expectation.DisplayType}' changed after screenshot replacement. " +
                            "The final cross-set inventory contains an asset outside the approved replacement operation; review and run a new plan before submission.");
                    }

                    allExact = false;
                }

                if (allExact)
                    return;

                await _delay(ReplacementInventoryPollInterval, boundedToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested &&
            timeoutSource.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                $"App Store Connect screenshot inventories could not be revalidated within {_replacementInventoryTimeout}. " +
                "The final cross-set inventory could not be proven to match the approved screenshot bytes; review and run a new plan before submission.");
        }
    }

    private sealed class ReplacementInventoryExpectation
    {
        public ReplacementInventoryExpectation(
            string screenshotSetId,
            string displayType,
            IReadOnlyDictionary<string, AppStoreConnectScreenshotInfo> replacedById,
            string[] expectedIds,
            IReadOnlyDictionary<string, string> expectedById)
        {
            ScreenshotSetId = screenshotSetId;
            DisplayType = displayType;
            ReplacedById = replacedById;
            ExpectedIds = expectedIds;
            ExpectedById = expectedById;
        }

        public string ScreenshotSetId { get; }

        public string DisplayType { get; }

        private IReadOnlyDictionary<string, AppStoreConnectScreenshotInfo> ReplacedById { get; }

        private string[] ExpectedIds { get; }

        private IReadOnlyDictionary<string, string> ExpectedById { get; }

        public bool IsExact(IReadOnlyCollection<AppStoreConnectScreenshotInfo> screenshots)
        {
            var observed = screenshots.ToArray();
            return observed.Length == ExpectedIds.Length &&
                   observed.Select(static screenshot => screenshot.Id).SequenceEqual(ExpectedIds, StringComparer.Ordinal) &&
                   observed.Select(static screenshot => screenshot.SourceFileChecksum?.Trim() ?? string.Empty)
                       .SequenceEqual(ExpectedIds.Select(id => ExpectedById[id]), StringComparer.OrdinalIgnoreCase);
        }

        public AppStoreConnectScreenshotInfo? FindConcurrentMutation(
            IReadOnlyCollection<AppStoreConnectScreenshotInfo> screenshots)
            => screenshots.FirstOrDefault(screenshot =>
            {
                if (ExpectedById.TryGetValue(screenshot.Id, out var expectedChecksum))
                {
                    var observedChecksum = screenshot.SourceFileChecksum?.Trim();
                    return !string.IsNullOrWhiteSpace(observedChecksum) &&
                           !string.Equals(observedChecksum, expectedChecksum, StringComparison.OrdinalIgnoreCase);
                }

                if (!ReplacedById.TryGetValue(screenshot.Id, out var replaced))
                    return true;

                var observedReplacedChecksum = screenshot.SourceFileChecksum?.Trim();
                return !string.IsNullOrWhiteSpace(observedReplacedChecksum) &&
                       !string.Equals(
                           observedReplacedChecksum,
                           replaced.SourceFileChecksum?.Trim(),
                           StringComparison.OrdinalIgnoreCase);
            });
    }
}
