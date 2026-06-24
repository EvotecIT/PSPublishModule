namespace PowerForge;

/// <summary>
/// Builds compact App Store Connect release-state summaries.
/// </summary>
public sealed class AppStoreConnectReleaseStateService
{
    private readonly AppStoreConnectClient _client;

    /// <summary>
    /// Initializes a release state service.
    /// </summary>
    public AppStoreConnectReleaseStateService(AppStoreConnectClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>
    /// Reads App Store, TestFlight, review, and beta-group state for one app.
    /// </summary>
    public async Task<AppStoreConnectReleaseStateResult> GetAsync(
        AppStoreConnectReleaseStateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.AppId))
            throw new ArgumentException("AppId is required.", nameof(request));

        var appId = request.AppId.Trim();
        var platforms = NormalizePlatforms(request.Platforms);
        var platformStates = new List<AppStoreConnectPlatformReleaseState>();
        var messages = new List<string>();

        foreach (var platform in platforms)
            platformStates.Add(await GetPlatformStateAsync(appId, request, platform, cancellationToken).ConfigureAwait(false));

        var betaGroups = await GetBetaGroupStatesAsync(appId, request, cancellationToken).ConfigureAwait(false);
        if (betaGroups.Length == 0)
            messages.Add("No beta groups were included in the summary.");

        var nextActions = platformStates
            .SelectMany(static state => state.NextActions.Select(action => $"{state.Platform}: {action}"))
            .Concat(betaGroups.SelectMany(static group => group.NextActions.Select(action => $"BetaGroup {group.Name ?? group.Id}: {action}")))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new AppStoreConnectReleaseStateResult
        {
            AppId = appId,
            VersionString = NormalizeOptional(request.VersionString),
            BuildNumber = NormalizeOptional(request.BuildNumber),
            CheckedAt = DateTimeOffset.UtcNow,
            Platforms = platformStates.ToArray(),
            BetaGroups = betaGroups,
            NextActions = nextActions,
            Messages = messages.ToArray()
        };
    }

    private async Task<AppStoreConnectPlatformReleaseState> GetPlatformStateAsync(
        string appId,
        AppStoreConnectReleaseStateRequest request,
        ApplePlatform platform,
        CancellationToken cancellationToken)
    {
        var versionString = NormalizeOptional(request.VersionString);
        var buildNumber = NormalizeOptional(request.BuildNumber);
        var messages = new List<string>();
        var nextActions = new List<string>();

        var version = (await _client.GetVersionsAsync(
            appId,
            versionString,
            platform,
            limit: 10,
            cancellationToken).ConfigureAwait(false)).FirstOrDefault();
        if (version is null)
        {
            messages.Add(versionString is null
                ? "No App Store version matched the query."
                : $"App Store version '{versionString}' was not found.");
            nextActions.Add("Create App Store distribution version.");
        }

        AppStoreConnectBuildInfo? matchedBuild = null;
        if (buildNumber is not null)
        {
            matchedBuild = (await _client.GetBuildsAsync(
                appId,
                buildNumber,
                limit: 20,
                marketingVersion: versionString,
                platform: platform,
                cancellationToken: cancellationToken).ConfigureAwait(false)).FirstOrDefault();
            if (matchedBuild is null)
            {
                messages.Add($"Build '{buildNumber}' was not found for platform '{platform}'.");
                nextActions.Add("Upload or wait for the requested build to process.");
            }
        }

        AppStoreConnectBuildInfo? selectedBuild = null;
        var selectedBuildId = version is null
            ? null
            : await _client.GetVersionBuildIdAsync(version.Id, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(selectedBuildId))
            selectedBuild = await _client.GetBuildAsync(selectedBuildId!, cancellationToken).ConfigureAwait(false);

        bool? matchedBuildSelected = null;
        if (matchedBuild is not null && version is not null)
        {
            matchedBuildSelected = string.Equals(selectedBuildId, matchedBuild.Id, StringComparison.OrdinalIgnoreCase);
            if (matchedBuildSelected == false)
                nextActions.Add("Select the requested build on the App Store version.");
        }
        else if (version is not null && selectedBuild is null)
        {
            nextActions.Add("Select a build on the App Store version.");
        }

        var reviewSubmissions = await _client.GetReviewSubmissionsAsync(
            appId,
            platform,
            limit: 50,
            cancellationToken).ConfigureAwait(false);

        var testFlightBuild = matchedBuild ?? selectedBuild;
        AppStoreConnectBuildBetaDetailInfo? betaDetail = null;
        AppStoreConnectBetaAppReviewSubmissionInfo? betaSubmission = null;
        if (testFlightBuild is not null)
        {
            betaDetail = await _client.GetBuildBetaDetailAsync(testFlightBuild.Id, cancellationToken).ConfigureAwait(false);
            betaSubmission = await _client.GetBetaAppReviewSubmissionForBuildAsync(testFlightBuild.Id, cancellationToken).ConfigureAwait(false);
        }

        AddAppStoreNextActions(version, nextActions);
        AddTestFlightNextActions(testFlightBuild, betaDetail, betaSubmission, nextActions);

        return new AppStoreConnectPlatformReleaseState
        {
            Platform = platform,
            Version = version,
            SelectedBuild = selectedBuild,
            MatchedBuild = matchedBuild,
            TestFlightBuild = testFlightBuild,
            MatchedBuildSelected = matchedBuildSelected,
            ReviewSubmissions = reviewSubmissions,
            BetaDetail = betaDetail,
            BetaReviewSubmission = betaSubmission,
            NextActions = nextActions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Messages = messages.ToArray()
        };
    }

    private async Task<AppStoreConnectBetaGroupReleaseState[]> GetBetaGroupStatesAsync(
        string appId,
        AppStoreConnectReleaseStateRequest request,
        CancellationToken cancellationToken)
    {
        var includeAll = request.IncludeAllBetaGroups;
        var groupIds = NormalizeStrings(request.BetaGroupIds);
        var groupNames = NormalizeStrings(request.BetaGroupNames);
        if (!includeAll && groupIds.Length == 0 && groupNames.Length == 0)
            return Array.Empty<AppStoreConnectBetaGroupReleaseState>();

        var allGroups = await _client.GetBetaGroupsAsync(appId, limit: 200, cancellationToken: cancellationToken).ConfigureAwait(false);
        var groups = allGroups
            .Where(group =>
                includeAll ||
                groupIds.Contains(group.Id, StringComparer.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(group.Name) && groupNames.Contains(group.Name!, StringComparer.OrdinalIgnoreCase)))
            .GroupBy(static group => group.Id, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();

        var result = new List<AppStoreConnectBetaGroupReleaseState>();
        foreach (var group in groups)
        {
            var testers = await _client.GetBetaTestersForGroupAsync(group.Id, limit: 200, cancellationToken).ConfigureAwait(false);
            var isFull = group.PublicLinkLimit.HasValue
                ? testers.Length >= group.PublicLinkLimit.Value
                : (bool?)null;
            var nextActions = new List<string>();
            if (group.PublicLinkEnabled == false)
                nextActions.Add("Enable the public TestFlight link if external self-service testers are expected.");
            if (isFull == true)
                nextActions.Add("Raise the public-link tester limit or remove inactive testers.");

            result.Add(new AppStoreConnectBetaGroupReleaseState
            {
                Id = group.Id,
                Name = group.Name,
                PublicLinkEnabled = group.PublicLinkEnabled,
                PublicLink = group.PublicLink,
                PublicLinkLimit = group.PublicLinkLimit,
                TesterCount = testers.Length,
                IsFull = isFull,
                IsInternalGroup = group.IsInternalGroup,
                NextActions = nextActions.ToArray()
            });
        }

        return result.ToArray();
    }

    private static void AddAppStoreNextActions(AppStoreConnectVersionInfo? version, List<string> nextActions)
    {
        var state = NormalizeOptional(version?.AppStoreState ?? version?.AppVersionState);
        if (state is null)
            return;

        if (string.Equals(state, "PREPARE_FOR_SUBMISSION", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(state, "READY_FOR_REVIEW", StringComparison.OrdinalIgnoreCase))
            nextActions.Add("Submit the App Store version for review when readiness passes.");
        else if (string.Equals(state, "WAITING_FOR_REVIEW", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(state, "IN_REVIEW", StringComparison.OrdinalIgnoreCase))
            nextActions.Add("Wait for App Review.");
        else if (string.Equals(state, "PENDING_DEVELOPER_RELEASE", StringComparison.OrdinalIgnoreCase))
            nextActions.Add("Release the approved App Store version when ready.");
    }

    private static void AddTestFlightNextActions(
        AppStoreConnectBuildInfo? build,
        AppStoreConnectBuildBetaDetailInfo? betaDetail,
        AppStoreConnectBetaAppReviewSubmissionInfo? betaSubmission,
        List<string> nextActions)
    {
        if (build is null)
            return;
        if (!string.Equals(build.ProcessingState, "VALID", StringComparison.OrdinalIgnoreCase))
        {
            nextActions.Add("Wait for the TestFlight build to finish processing.");
            return;
        }

        var externalState = NormalizeOptional(betaDetail?.ExternalBuildState);
        if (externalState is null)
            return;

        if (string.Equals(externalState, "READY_FOR_BETA_SUBMISSION", StringComparison.OrdinalIgnoreCase))
            nextActions.Add("Submit the TestFlight build to Beta App Review.");
        else if (string.Equals(externalState, "WAITING_FOR_BETA_REVIEW", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(betaSubmission?.BetaReviewState, "WAITING_FOR_REVIEW", StringComparison.OrdinalIgnoreCase))
            nextActions.Add("Wait for Beta App Review.");
        else if (string.Equals(externalState, "BETA_APPROVED", StringComparison.OrdinalIgnoreCase))
            nextActions.Add("External TestFlight is approved; verify public link availability.");
    }

    private static ApplePlatform[] NormalizePlatforms(ApplePlatform[]? platforms)
    {
        var normalized = (platforms ?? Array.Empty<ApplePlatform>())
            .Distinct()
            .ToArray();
        return normalized.Length == 0 ? new[] { ApplePlatform.iOS } : normalized;
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

    private static string[] NormalizeStrings(string[]? values)
        => (values ?? Array.Empty<string>())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
