namespace PowerForge;

/// <summary>
/// Submits prepared App Store Connect Distribution versions to App Review.
/// </summary>
public sealed class AppStoreConnectReviewSubmissionService
{
    private readonly AppStoreConnectClient _client;

    /// <summary>
    /// Initializes an App Review submission service.
    /// </summary>
    public AppStoreConnectReviewSubmissionService(AppStoreConnectClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>
    /// Creates a review submission, adds the App Store version, and submits it.
    /// </summary>
    public async Task<AppStoreConnectReviewSubmissionResult> SubmitAsync(
        AppStoreConnectReviewSubmissionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.AppId))
            throw new ArgumentException("AppId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.VersionString))
            throw new ArgumentException("VersionString is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.BuildNumber))
            throw new ArgumentException("BuildNumber is required.", nameof(request));

        var appId = request.AppId.Trim();
        var versionString = request.VersionString.Trim();
        var buildNumber = request.BuildNumber.Trim();
        var messages = new List<string>();

        var version = (await _client.GetVersionsAsync(
            appId,
            versionString,
            request.Platform,
            limit: 10,
            cancellationToken).ConfigureAwait(false)).FirstOrDefault()
            ?? throw new InvalidOperationException($"App Store version '{versionString}' was not found for app '{appId}' and platform '{request.Platform}'.");
        messages.Add($"Found App Store version '{versionString}' for platform '{request.Platform}'.");

        var build = await ResolveSelectedBuildAsync(version.Id, appId, versionString, buildNumber, request, cancellationToken).ConfigureAwait(false);
        AppStoreConnectReleaseReadinessResult? readiness = null;
        if (request.CheckReadiness)
        {
            readiness = await new AppStoreConnectReleaseReadinessService(_client)
                .CheckAsync(CreateReadinessRequest(request, appId, versionString, buildNumber), cancellationToken)
                .ConfigureAwait(false);
            messages.Add(readiness.IsReady
                ? "App Store version readiness check passed."
                : "App Store version readiness check failed.");
            if (request.RequireReady && !readiness.IsReady)
                throw new InvalidOperationException("App Store version is not ready for review submission: " +
                    string.Join("; ", readiness.Checks.Where(static check => !check.Passed).Select(static check => check.Message)));
        }

        var reviewSubmission = await _client.CreateReviewSubmissionAsync(appId, request.Platform, cancellationToken).ConfigureAwait(false);
        messages.Add($"Created review submission '{reviewSubmission.Id}' for platform '{request.Platform}'.");

        var reviewSubmissionItem = await _client.CreateReviewSubmissionItemAsync(reviewSubmission.Id, version.Id, cancellationToken).ConfigureAwait(false);
        messages.Add($"Added App Store version '{versionString}' to review submission '{reviewSubmission.Id}'.");

        reviewSubmission = await _client.SubmitReviewSubmissionAsync(reviewSubmission.Id, cancellationToken).ConfigureAwait(false);
        messages.Add($"Submitted App Store version '{versionString}' to App Review.");

        return new AppStoreConnectReviewSubmissionResult
        {
            AppId = appId,
            VersionString = versionString,
            BuildNumber = buildNumber,
            Platform = request.Platform,
            Version = version,
            Build = build,
            ReviewSubmission = reviewSubmission,
            ReviewSubmissionItem = reviewSubmissionItem,
            Readiness = readiness,
            Messages = messages.ToArray()
        };
    }

    private async Task<AppStoreConnectBuildInfo?> ResolveSelectedBuildAsync(
        string versionId,
        string appId,
        string versionString,
        string buildNumber,
        AppStoreConnectReviewSubmissionRequest request,
        CancellationToken cancellationToken)
    {
        var expectedBuild = (await _client.GetBuildsAsync(
            appId,
            buildNumber,
            limit: 20,
            marketingVersion: versionString,
            platform: request.Platform,
            cancellationToken: cancellationToken).ConfigureAwait(false)).FirstOrDefault()
            ?? throw new InvalidOperationException($"Build '{buildNumber}' was not found for app '{appId}', version '{versionString}', and platform '{request.Platform}'.");

        if (request.RequireValidBuild)
            ValidateBuildIsSubmittable(expectedBuild, buildNumber);

        if (!request.RequireSelectedBuild)
            return expectedBuild;

        var selectedBuildId = await _client.GetVersionBuildIdAsync(versionId, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(selectedBuildId, expectedBuild.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"App Store version '{versionString}' does not have build '{buildNumber}' selected for platform '{request.Platform}'. Run PrepareDistribution first.");

        return expectedBuild;
    }

    private static AppStoreConnectReleaseReadinessRequest CreateReadinessRequest(
        AppStoreConnectReviewSubmissionRequest request,
        string appId,
        string versionString,
        string buildNumber)
    {
        var source = request.ReadinessRequest ?? new AppStoreConnectReleaseReadinessRequest();
        return new AppStoreConnectReleaseReadinessRequest
        {
            AppId = appId,
            VersionString = versionString,
            BuildNumber = buildNumber,
            Platform = request.Platform,
            Locale = string.IsNullOrWhiteSpace(source.Locale) ? "en-US" : source.Locale.Trim(),
            RequireSelectedBuild = source.RequireSelectedBuild,
            RequireValidBuild = source.RequireValidBuild,
            RequireDescription = source.RequireDescription,
            RequireKeywords = source.RequireKeywords,
            RequireSupportUrl = source.RequireSupportUrl,
            RequireMarketingUrl = source.RequireMarketingUrl,
            RequirePromotionalText = source.RequirePromotionalText,
            RequireWhatsNew = source.RequireWhatsNew,
            RequireScreenshots = source.RequireScreenshots,
            RequireCompleteScreenshots = source.RequireCompleteScreenshots,
            MinimumScreenshotsPerSet = source.MinimumScreenshotsPerSet,
            RequiredScreenshotDisplayTypes = source.RequiredScreenshotDisplayTypes,
            ScreenshotSpec = source.ScreenshotSpec
        };
    }

    private static void ValidateBuildIsSubmittable(AppStoreConnectBuildInfo build, string buildNumber)
    {
        if (!string.Equals(build.ProcessingState, "VALID", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Build '{buildNumber}' cannot be submitted because processing state is '{build.ProcessingState ?? "unknown"}'.");
        if (build.Expired == true)
            throw new InvalidOperationException($"Build '{buildNumber}' cannot be submitted because it is expired.");
    }
}
