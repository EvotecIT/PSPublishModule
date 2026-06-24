namespace PowerForge;

/// <summary>
/// Submits TestFlight builds to Beta App Review for external testing.
/// </summary>
public sealed class AppStoreConnectBetaAppReviewSubmissionService
{
    private readonly AppStoreConnectClient _client;

    /// <summary>
    /// Initializes a Beta App Review submission service.
    /// </summary>
    public AppStoreConnectBetaAppReviewSubmissionService(AppStoreConnectClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>
    /// Submits a processed build to Beta App Review, reusing an existing submission when App Store Connect already has one.
    /// </summary>
    public async Task<AppStoreConnectBetaAppReviewSubmissionResult> SubmitAsync(
        AppStoreConnectBetaAppReviewSubmissionRequest request,
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

        var build = (await _client.GetBuildsAsync(
            appId,
            buildNumber,
            limit: 20,
            marketingVersion: versionString,
            platform: request.Platform,
            cancellationToken: cancellationToken).ConfigureAwait(false)).FirstOrDefault()
            ?? throw new InvalidOperationException($"Build '{buildNumber}' was not found for app '{appId}', version '{versionString}', and platform '{request.Platform}'.");

        if (request.RequireValidBuild)
            ValidateBuildIsSubmittable(build, buildNumber);

        var submission = await _client.GetBetaAppReviewSubmissionForBuildAsync(build.Id, cancellationToken).ConfigureAwait(false);
        if (submission is null)
        {
            submission = await _client.CreateBetaAppReviewSubmissionAsync(build.Id, cancellationToken).ConfigureAwait(false);
            messages.Add($"Submitted build '{buildNumber}' to Beta App Review.");
        }
        else
        {
            messages.Add($"Reused existing Beta App Review submission '{submission.Id}' for build '{buildNumber}' with state '{submission.BetaReviewState ?? "unknown"}'.");
        }

        return new AppStoreConnectBetaAppReviewSubmissionResult
        {
            AppId = appId,
            VersionString = versionString,
            BuildNumber = buildNumber,
            Platform = request.Platform,
            Build = build,
            Submission = submission,
            Messages = messages.ToArray()
        };
    }

    private static void ValidateBuildIsSubmittable(AppStoreConnectBuildInfo build, string buildNumber)
    {
        if (!string.Equals(build.ProcessingState, "VALID", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Build '{buildNumber}' cannot be submitted to Beta App Review because processing state is '{build.ProcessingState ?? "unknown"}'.");
        if (build.Expired == true)
            throw new InvalidOperationException($"Build '{buildNumber}' cannot be submitted to Beta App Review because it is expired.");
    }
}
