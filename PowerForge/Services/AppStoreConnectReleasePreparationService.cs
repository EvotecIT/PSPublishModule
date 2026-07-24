namespace PowerForge;

/// <summary>
/// Prepares App Store Connect Distribution metadata for an uploaded Apple app build.
/// </summary>
public sealed class AppStoreConnectReleasePreparationService
{
    private readonly AppStoreConnectClient _client;

    /// <summary>
    /// Creates a release preparation service.
    /// </summary>
    /// <param name="client">App Store Connect client.</param>
    public AppStoreConnectReleasePreparationService(AppStoreConnectClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>
    /// Ensures the App Store version exists when needed and applies the requested version- or app-scoped release metadata.
    /// </summary>
    public async Task<AppStoreConnectReleasePreparationResult> PrepareAsync(
        AppStoreConnectReleasePreparationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.AppId))
            throw new ArgumentException("AppId is required.", nameof(request));
        var requiresVersion = request.CreateVersion ||
                              request.SelectBuild ||
                              request.MetadataSpec is not null ||
                              request.ScreenshotSpec is not null ||
                              request.CheckReadiness;
        if (requiresVersion && string.IsNullOrWhiteSpace(request.VersionString))
            throw new ArgumentException("VersionString is required.", nameof(request));
        if ((request.SelectBuild || request.CheckReadiness) && string.IsNullOrWhiteSpace(request.BuildNumber))
            throw new ArgumentException("BuildNumber is required when build selection or release readiness is enabled.", nameof(request));

        var appId = request.AppId.Trim();
        var versionString = request.VersionString?.Trim() ?? string.Empty;
        var buildNumber = request.BuildNumber?.Trim() ?? string.Empty;
        var messages = new List<string>();
        var createdVersion = false;
        AppStoreConnectVersionInfo? version = null;
        if (requiresVersion)
        {
            var configuredVersionId = ResolveConfiguredVersionId(request);
            if (!request.CreateVersion && configuredVersionId is not null)
            {
                version = new AppStoreConnectVersionInfo
                {
                    Id = configuredVersionId,
                    VersionString = versionString,
                    Platform = request.Platform.ToString()
                };
                messages.Add($"Using configured App Store version '{versionString}' for platform '{request.Platform}'.");
            }
            else
            {
                var versions = await _client.GetVersionsAsync(
                    appId,
                    versionString,
                    request.Platform,
                    limit: 10,
                    cancellationToken).ConfigureAwait(false);
                version = versions.FirstOrDefault();
            }

            if (version is null)
            {
                if (!request.CreateVersion)
                    throw new InvalidOperationException($"App Store version '{versionString}' was not found for app '{appId}' and platform '{request.Platform}'.");

                version = await _client.CreateVersionAsync(appId, versionString, request.Platform, cancellationToken).ConfigureAwait(false);
                createdVersion = true;
                messages.Add($"Created App Store version '{versionString}' for platform '{request.Platform}'.");
            }
            else
            {
                messages.Add($"Found App Store version '{versionString}' for platform '{request.Platform}'.");
            }
        }

        AppStoreConnectBuildInfo? build = null;
        string? previousBuildId = null;
        var selectedBuild = false;
        if (request.SelectBuild)
        {
            build = (await _client.GetBuildsAsync(
                appId,
                buildNumber,
                limit: 20,
                marketingVersion: versionString,
                platform: request.Platform,
                cancellationToken: cancellationToken).ConfigureAwait(false)).FirstOrDefault();
            if (build is null)
                throw new InvalidOperationException($"Build '{buildNumber}' was not found for app '{appId}', version '{versionString}', and platform '{request.Platform}'.");

            if (request.RequireValidBuild)
                ValidateBuildIsSelectable(build, buildNumber);

            previousBuildId = await _client.GetVersionBuildIdAsync(version!.Id, cancellationToken).ConfigureAwait(false);
            if (string.Equals(previousBuildId, build.Id, StringComparison.OrdinalIgnoreCase))
            {
                messages.Add($"Build '{buildNumber}' is already selected for App Store version '{versionString}'.");
            }
            else
            {
                await _client.SetVersionBuildAsync(version.Id, build.Id, cancellationToken).ConfigureAwait(false);
                selectedBuild = true;
                messages.Add($"Selected build '{buildNumber}' for App Store version '{versionString}'.");
            }
        }

        AppStoreConnectVersionMetadataSyncResult? metadata = null;
        if (request.MetadataSpec is not null)
        {
            var metadataSpec = CreateMetadataSpecForVersion(request.MetadataSpec, appId, versionString, request.Platform, version!.Id);
            metadata = await new AppStoreConnectVersionMetadataSyncService(_client).SyncAsync(
                new AppStoreConnectVersionMetadataSyncRequest { Spec = metadataSpec },
                cancellationToken).ConfigureAwait(false);
            messages.Add("Synchronized App Store version metadata.");
        }

        var appInfoMetadataResults = new List<AppStoreConnectAppInfoMetadataSyncResult>();
        foreach (var sourceSpec in request.AppInfoMetadataSpecs ?? Array.Empty<AppStoreConnectAppInfoMetadataSpec>())
        {
            var appInfoMetadataSpec = CreateAppInfoMetadataSpec(sourceSpec, appId);
            var appInfoMetadata = await new AppStoreConnectAppInfoMetadataSyncService(_client).SyncAsync(
                new AppStoreConnectAppInfoMetadataSyncRequest { Spec = appInfoMetadataSpec },
                cancellationToken).ConfigureAwait(false);
            appInfoMetadataResults.Add(appInfoMetadata);
            messages.Add($"Synchronized App Store App Information metadata for locale '{appInfoMetadataSpec.Locale}'.");
        }

        AppStoreConnectScreenshotSyncResult? screenshots = null;
        if (request.ScreenshotSpec is not null)
        {
            var screenshotSpec = CreateScreenshotSpecForVersion(request.ScreenshotSpec, appId, versionString, request.Platform, version!.Id);
            screenshots = await new AppStoreConnectScreenshotSyncService(_client).SyncAsync(
                new AppStoreConnectScreenshotSyncRequest
                {
                    Spec = screenshotSpec,
                    ReplaceExisting = request.ReplaceScreenshots,
                    BaseDirectory = request.BaseDirectory
                },
                cancellationToken).ConfigureAwait(false);
            messages.Add("Synchronized App Store screenshots.");
        }

        AppStoreConnectReleaseReadinessResult? readiness = null;
        if (request.CheckReadiness)
        {
            var readinessRequest = CreateReadinessRequestForVersion(
                request.ReadinessRequest,
                appId,
                versionString,
                buildNumber,
                request.Platform,
                request.ScreenshotSpec);
            readiness = await new AppStoreConnectReleaseReadinessService(_client)
                .CheckAsync(readinessRequest, cancellationToken)
                .ConfigureAwait(false);
            messages.Add(readiness.IsReady
                ? "App Store version readiness check passed."
                : "App Store version readiness check failed.");
        }

        return new AppStoreConnectReleasePreparationResult
        {
            AppId = appId,
            VersionString = versionString,
            BuildNumber = buildNumber,
            Platform = request.Platform,
            Version = version,
            Build = build,
            CreatedVersion = createdVersion,
            SelectedBuild = selectedBuild,
            PreviousBuildId = previousBuildId,
            Screenshots = screenshots,
            Metadata = metadata,
            AppInfoMetadataResults = appInfoMetadataResults.ToArray(),
            Readiness = readiness,
            Messages = messages.ToArray()
        };
    }

    private static string? ResolveConfiguredVersionId(AppStoreConnectReleasePreparationRequest request)
    {
        var configuredIds = new[]
            {
                request.ScreenshotSpec?.VersionId,
                request.MetadataSpec?.VersionId
            }
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (configuredIds.Length > 1)
            throw new InvalidOperationException("Configured screenshot and metadata mappings target different App Store version ids.");

        return configuredIds.SingleOrDefault();
    }

    private static void ValidateBuildIsSelectable(AppStoreConnectBuildInfo build, string buildNumber)
    {
        if (!string.Equals(build.ProcessingState, "VALID", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Build '{buildNumber}' cannot be selected because processing state is '{build.ProcessingState ?? "unknown"}'.");
        if (build.Expired == true)
            throw new InvalidOperationException($"Build '{buildNumber}' cannot be selected because it is expired.");
    }

    private static AppStoreConnectScreenshotSyncSpec CreateScreenshotSpecForVersion(
        AppStoreConnectScreenshotSyncSpec source,
        string appId,
        string versionString,
        ApplePlatform platform,
        string versionId)
    {
        if (!string.IsNullOrWhiteSpace(source.AppId) &&
            !string.Equals(source.AppId.Trim(), appId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Screenshot config AppId '{source.AppId}' does not match release app id '{appId}'.");
        var sourceVersionString = string.IsNullOrWhiteSpace(source.VersionString) ? null : source.VersionString!.Trim();
        if (sourceVersionString is not null &&
            !string.Equals(sourceVersionString, versionString, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Screenshot config VersionString '{source.VersionString}' does not match release version '{versionString}'.");
        if (source.Platform != platform)
            throw new InvalidOperationException($"Screenshot config Platform '{source.Platform}' does not match release platform '{platform}'.");

        return new AppStoreConnectScreenshotSyncSpec
        {
            AppId = appId,
            VersionString = versionString,
            VersionId = versionId,
            Platform = platform,
            Locale = source.Locale,
            ScreenshotSets = source.ScreenshotSets,
            Quality = source.Quality
        };
    }

    private static AppStoreConnectVersionMetadataSpec CreateMetadataSpecForVersion(
        AppStoreConnectVersionMetadataSpec source,
        string appId,
        string versionString,
        ApplePlatform platform,
        string versionId)
    {
        if (!string.IsNullOrWhiteSpace(source.AppId) &&
            !string.Equals(source.AppId.Trim(), appId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Metadata config AppId '{source.AppId}' does not match release app id '{appId}'.");
        var sourceVersionString = string.IsNullOrWhiteSpace(source.VersionString) ? null : source.VersionString!.Trim();
        if (sourceVersionString is not null &&
            !string.Equals(sourceVersionString, versionString, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Metadata config VersionString '{source.VersionString}' does not match release version '{versionString}'.");
        if (source.Platform != platform)
            throw new InvalidOperationException($"Metadata config Platform '{source.Platform}' does not match release platform '{platform}'.");

        return new AppStoreConnectVersionMetadataSpec
        {
            AppId = appId,
            VersionString = versionString,
            VersionId = versionId,
            Platform = platform,
            Locale = source.Locale,
            Metadata = source.Metadata
        };
    }

    private static AppStoreConnectAppInfoMetadataSpec CreateAppInfoMetadataSpec(
        AppStoreConnectAppInfoMetadataSpec source,
        string appId)
    {
        if (!string.IsNullOrWhiteSpace(source.AppId) &&
            !string.Equals(source.AppId.Trim(), appId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"App Information metadata config AppId '{source.AppId}' does not match release app id '{appId}'.");

        return new AppStoreConnectAppInfoMetadataSpec
        {
            AppId = appId,
            AppInfoId = source.AppInfoId,
            Locale = source.Locale,
            Metadata = source.Metadata
        };
    }

    private static AppStoreConnectReleaseReadinessRequest CreateReadinessRequestForVersion(
        AppStoreConnectReleaseReadinessRequest? source,
        string appId,
        string versionString,
        string buildNumber,
        ApplePlatform platform,
        AppStoreConnectScreenshotSyncSpec? screenshotSpec)
    {
        source ??= new AppStoreConnectReleaseReadinessRequest();
        return new AppStoreConnectReleaseReadinessRequest
        {
            AppId = appId,
            VersionString = versionString,
            BuildNumber = buildNumber,
            Platform = platform,
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
            ScreenshotSpec = screenshotSpec ?? source.ScreenshotSpec
        };
    }
}
