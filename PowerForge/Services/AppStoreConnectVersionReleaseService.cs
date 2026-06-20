namespace PowerForge;

/// <summary>
/// Releases approved App Store Connect Distribution versions to the App Store.
/// </summary>
public sealed class AppStoreConnectVersionReleaseService
{
    private readonly AppStoreConnectClient _client;

    /// <summary>
    /// Initializes an App Store version release service.
    /// </summary>
    public AppStoreConnectVersionReleaseService(AppStoreConnectClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>
    /// Requests manual release of an approved App Store version.
    /// </summary>
    public async Task<AppStoreConnectVersionReleaseResult> ReleaseAsync(
        AppStoreConnectVersionReleaseRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.AppId))
            throw new ArgumentException("AppId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.VersionString))
            throw new ArgumentException("VersionString is required.", nameof(request));

        var appId = request.AppId.Trim();
        var versionString = request.VersionString.Trim();
        var version = (await _client.GetVersionsAsync(
            appId,
            versionString,
            request.Platform,
            limit: 10,
            cancellationToken).ConfigureAwait(false)).FirstOrDefault()
            ?? throw new InvalidOperationException($"App Store version '{versionString}' was not found for app '{appId}' and platform '{request.Platform}'.");

        if (request.RequirePendingDeveloperRelease)
            ValidateVersionCanBeReleased(version, versionString, request.Platform);

        var releaseRequest = await _client.CreateVersionReleaseRequestAsync(version.Id, cancellationToken).ConfigureAwait(false);
        return new AppStoreConnectVersionReleaseResult
        {
            AppId = appId,
            VersionString = versionString,
            Platform = request.Platform,
            Version = version,
            ReleaseRequest = releaseRequest,
            Messages = new[]
            {
                $"Requested App Store release for version '{versionString}' on platform '{request.Platform}'."
            }
        };
    }

    private static void ValidateVersionCanBeReleased(
        AppStoreConnectVersionInfo version,
        string versionString,
        ApplePlatform platform)
    {
        if (IsPendingDeveloperRelease(version.AppStoreState) || IsPendingDeveloperRelease(version.AppVersionState))
            return;

        var state = FirstNonEmpty(version.AppStoreState, version.AppVersionState, "unknown");
        throw new InvalidOperationException($"App Store version '{versionString}' for platform '{platform}' cannot be released because state is '{state}', not PENDING_DEVELOPER_RELEASE.");
    }

    private static bool IsPendingDeveloperRelease(string? state)
        => string.Equals(state, "PENDING_DEVELOPER_RELEASE", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(state, "Pending Developer Release", StringComparison.OrdinalIgnoreCase);

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}
