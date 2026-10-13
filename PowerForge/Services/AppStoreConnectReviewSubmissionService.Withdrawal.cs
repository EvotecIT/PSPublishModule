namespace PowerForge;

public sealed partial class AppStoreConnectReviewSubmissionService
{
    /// <summary>Reads and binds a submitted version, selected build and sole review item without mutating Apple state.</summary>
    public async Task<AppStoreConnectReviewWithdrawalPlan> PlanWithdrawalAsync(
        AppStoreConnectReviewWithdrawalRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        var appId = RequireWithdrawalValue(request.AppId, nameof(request.AppId));
        var versionString = RequireWithdrawalValue(request.VersionString, nameof(request.VersionString));
        var buildNumber = RequireWithdrawalValue(request.BuildNumber, nameof(request.BuildNumber));
        var submissionId = RequireWithdrawalValue(request.ReviewSubmissionId, nameof(request.ReviewSubmissionId));
        var platform = request.Platform;
        var storePlatform = AppStoreConnectClient.ToAppStoreConnectPlatform(platform);

        var version = RequireOneWithdrawalTarget(await _client.GetVersionsAsync(
            appId, versionString, platform, cancellationToken: cancellationToken).ConfigureAwait(false), "App Store version");
        if (string.IsNullOrWhiteSpace(version.Id) || version.VersionString != versionString || version.Platform != storePlatform)
            throw new InvalidOperationException("The App Store version does not match the requested version and platform.");
        var states = new[] { version.AppStoreState, version.AppVersionState }.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        if (states.Length == 0 || states.Any(state => !IsWithdrawableVersionState(state!)))
            throw new InvalidOperationException("The App Store version is not in an eligible review or pending-release state.");

        var build = RequireOneWithdrawalTarget(await _client.GetBuildsAsync(
            appId, buildNumber, marketingVersion: versionString, platform: platform,
            cancellationToken: cancellationToken).ConfigureAwait(false), "uploaded build");
        if (build.Version != buildNumber || build.MarketingVersion != versionString || build.Platform != storePlatform ||
            string.IsNullOrWhiteSpace(build.Id) ||
            await _client.GetVersionBuildIdAsync(version.Id, cancellationToken).ConfigureAwait(false) != build.Id)
            throw new InvalidOperationException("The submitted version does not have the exact requested platform/version/build selected.");

        var submissions = await _client.GetReviewSubmissionsAsync(appId, platform, cancellationToken: cancellationToken).ConfigureAwait(false);
        var submission = RequireOneWithdrawalTarget(submissions.Where(value => value.Id == submissionId).ToArray(), "review submission");
        if (submission.Platform != storePlatform || submission.State is not ("WAITING_FOR_REVIEW" or "IN_REVIEW" or "UNRESOLVED_ISSUES" or "COMPLETE"))
            throw new InvalidOperationException("The exact review submission is not in an eligible submitted state.");
        var item = RequireOneWithdrawalTarget(await _client.GetReviewSubmissionItemsAsync(
            submissionId, cancellationToken: cancellationToken).ConfigureAwait(false), "review submission item; cancellation must not affect other items");
        if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.AppStoreVersionId) ||
            (!string.IsNullOrWhiteSpace(item.ReviewSubmissionId) && item.ReviewSubmissionId != submissionId) ||
            item.State is not ("READY_FOR_REVIEW" or "ACCEPTED" or "APPROVED"))
            throw new InvalidOperationException("The sole review submission item does not belong to the exact requested version and submission.");
        var itemBinding = await _client.GetReviewWithdrawalVersionAsync(item.AppStoreVersionId!, cancellationToken).ConfigureAwait(false);
        if (!itemBinding.HasValue || itemBinding.Value.AppId != appId || itemBinding.Value.BuildId != build.Id ||
            itemBinding.Value.Version.Id != version.Id || itemBinding.Value.Version.VersionString != versionString ||
            itemBinding.Value.Version.Platform != storePlatform || itemBinding.Value.Version.AppStoreState != version.AppStoreState ||
            itemBinding.Value.Version.AppVersionState != version.AppVersionState)
            throw new InvalidOperationException("The review item's authoritative version resource does not match the exact requested app/version/platform/build and state.");

        return new AppStoreConnectReviewWithdrawalPlan
        {
            AppId = appId, VersionString = versionString, BuildNumber = buildNumber, Platform = platform,
            AppStoreVersionId = version.Id, BuildId = build.Id, ReviewSubmissionId = submissionId,
            ReviewSubmissionItemId = item.Id, AppStoreState = version.AppStoreState, AppVersionState = version.AppVersionState,
            SubmissionState = submission.State, ItemState = item.State
        };
    }

    /// <summary>
    /// Revalidates a reviewed withdrawal plan and requests cancellation once. Replacement must
    /// wait until readback observes Developer Rejected; a pending response is not withdrawal.
    /// </summary>
    public async Task<AppStoreConnectReviewWithdrawalResult> WithdrawAsync(
        AppStoreConnectReviewWithdrawalPlan reviewedPlan,
        bool confirmWithdrawal = false,
        CancellationToken cancellationToken = default)
    {
        if (reviewedPlan is null) throw new ArgumentNullException(nameof(reviewedPlan));
        if (!confirmWithdrawal) throw new InvalidOperationException("Review withdrawal requires explicit confirmation of the exact single-item plan.");
        // Snapshot the reviewed object before awaiting so caller edits cannot redirect the operation.
        var expected = new AppStoreConnectReviewWithdrawalPlan
        {
            AppId = reviewedPlan.AppId, VersionString = reviewedPlan.VersionString, BuildNumber = reviewedPlan.BuildNumber,
            Platform = reviewedPlan.Platform, AppStoreVersionId = reviewedPlan.AppStoreVersionId, BuildId = reviewedPlan.BuildId,
            ReviewSubmissionId = reviewedPlan.ReviewSubmissionId, ReviewSubmissionItemId = reviewedPlan.ReviewSubmissionItemId,
            AppStoreState = reviewedPlan.AppStoreState, AppVersionState = reviewedPlan.AppVersionState,
            SubmissionState = reviewedPlan.SubmissionState, ItemState = reviewedPlan.ItemState
        };
        var current = await PlanWithdrawalAsync(new AppStoreConnectReviewWithdrawalRequest
        {
            AppId = expected.AppId, VersionString = expected.VersionString, BuildNumber = expected.BuildNumber,
            Platform = expected.Platform, ReviewSubmissionId = expected.ReviewSubmissionId
        }, cancellationToken).ConfigureAwait(false);
        if (current.AppStoreVersionId != expected.AppStoreVersionId || current.BuildId != expected.BuildId ||
            current.ReviewSubmissionItemId != expected.ReviewSubmissionItemId || current.AppStoreState != expected.AppStoreState ||
            current.AppVersionState != expected.AppVersionState || current.SubmissionState != expected.SubmissionState || current.ItemState != expected.ItemState)
            throw new InvalidOperationException("The reviewed withdrawal binding or state changed. Read and review a fresh plan before cancellation.");

        try
        {
            var response = await _client.CancelReviewSubmissionAsync(current.ReviewSubmissionId, cancellationToken).ConfigureAwait(false);
            if (response.Id != current.ReviewSubmissionId || response.Platform != AppStoreConnectClient.ToAppStoreConnectPlatform(current.Platform))
                throw new InvalidOperationException("Apple returned a different review submission identity after cancellation.");
            var submission = await _client.GetReviewSubmissionAsync(current.ReviewSubmissionId, cancellationToken).ConfigureAwait(false);
            if (submission is null || submission.Id != current.ReviewSubmissionId || submission.Platform != response.Platform)
                throw new InvalidOperationException("The cancelled review submission identity could not be read back.");
            var version = RequireOneWithdrawalTarget(await _client.GetVersionsAsync(
                current.AppId, current.VersionString, current.Platform, cancellationToken: cancellationToken).ConfigureAwait(false), "withdrawn App Store version");
            if (version.Id != current.AppStoreVersionId || version.VersionString != current.VersionString || version.Platform != response.Platform ||
                await _client.GetVersionBuildIdAsync(version.Id, cancellationToken).ConfigureAwait(false) != current.BuildId)
                throw new InvalidOperationException("The bound version/build identity changed during withdrawal readback.");
            var observedState = version.AppVersionState ?? version.AppStoreState;
            return new AppStoreConnectReviewWithdrawalResult
            {
                Plan = current, CancellationRequested = true,
                WithdrawalObserved = new[] { version.AppStoreState, version.AppVersionState }
                    .Where(value => !string.IsNullOrWhiteSpace(value)).All(value => value == "DEVELOPER_REJECTED") &&
                    observedState == "DEVELOPER_REJECTED",
                ResponseSubmissionState = response.State, ObservedSubmissionState = submission.State,
                ObservedVersionState = observedState
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Review cancellation was attempted. Reconcile the exact submission and version before retrying or replacing its build; no replacement was performed.", ex);
        }
    }

    private static string RequireWithdrawalValue(string value, string name)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.") : value.Trim();

    private static T RequireOneWithdrawalTarget<T>(T[] values, string description)
        => values.Length == 1 ? values[0] : throw new InvalidOperationException($"Expected exactly one {description}; found {values.Length}.");

    private static bool IsWithdrawableVersionState(string state)
        => state is "WAITING_FOR_EXPORT_COMPLIANCE" or "WAITING_FOR_REVIEW" or "IN_REVIEW" or "PENDING_DEVELOPER_RELEASE" or "PENDING_APPLE_RELEASE";
}
