namespace PowerForge;

/// <summary>
/// Checks App Store Connect Distribution readiness for one platform version.
/// </summary>
public sealed class AppStoreConnectReleaseReadinessService
{
    private readonly AppStoreConnectClient _client;

    /// <summary>
    /// Initializes a release readiness service.
    /// </summary>
    public AppStoreConnectReleaseReadinessService(AppStoreConnectClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>
    /// Checks whether version, build, metadata, and screenshots satisfy the requested readiness contract.
    /// </summary>
    public async Task<AppStoreConnectReleaseReadinessResult> CheckAsync(
        AppStoreConnectReleaseReadinessRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.AppId))
            throw new ArgumentException("AppId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.VersionString))
            throw new ArgumentException("VersionString is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Locale))
            throw new ArgumentException("Locale is required.", nameof(request));

        var checks = new List<AppStoreConnectReleaseReadinessCheck>();
        var version = (await _client.GetVersionsAsync(
            request.AppId,
            request.VersionString,
            request.Platform,
            limit: 10,
            cancellationToken).ConfigureAwait(false)).FirstOrDefault();
        AddCheck(checks, "version", version is not null, version is null
            ? $"App Store version '{request.VersionString}' was not found for platform '{request.Platform}'."
            : $"App Store version '{request.VersionString}' exists for platform '{request.Platform}'.");

        AppStoreConnectBuildInfo? build = null;
        string? selectedBuildId = null;
        AppStoreConnectVersionLocalizationInfo? localization = null;
        var screenshotReadiness = Array.Empty<AppStoreConnectReleaseScreenshotSetReadiness>();

        if (version is not null)
        {
            if (!string.IsNullOrWhiteSpace(request.BuildNumber))
            {
                build = (await _client.GetBuildsAsync(
                    request.AppId,
                    request.BuildNumber,
                    limit: 20,
                    marketingVersion: request.VersionString,
                    platform: request.Platform,
                    cancellationToken: cancellationToken).ConfigureAwait(false)).FirstOrDefault();
                AddCheck(checks, "build", build is not null, build is null
                    ? $"Build '{request.BuildNumber}' was not found for version '{request.VersionString}' and platform '{request.Platform}'."
                    : $"Build '{request.BuildNumber}' exists for version '{request.VersionString}' and platform '{request.Platform}'.");

                if (build is not null && request.RequireValidBuild)
                {
                    var valid = string.Equals(build.ProcessingState, "VALID", StringComparison.OrdinalIgnoreCase) && build.Expired != true;
                    AddCheck(checks, "build.valid", valid, valid
                        ? $"Build '{request.BuildNumber}' is VALID and not expired."
                        : $"Build '{request.BuildNumber}' is not selectable: processing state '{build.ProcessingState ?? "unknown"}', expired '{build.Expired?.ToString() ?? "unknown"}'.");
                }

                if (request.RequireSelectedBuild)
                {
                    selectedBuildId = await _client.GetVersionBuildIdAsync(version.Id, cancellationToken).ConfigureAwait(false);
                    var selected = build is not null && string.Equals(selectedBuildId, build.Id, StringComparison.OrdinalIgnoreCase);
                    AddCheck(checks, "build.selected", selected, selected
                        ? $"Build '{request.BuildNumber}' is selected for Distribution."
                        : $"Build '{request.BuildNumber}' is not selected for Distribution.");
                }
            }

            localization = (await _client.GetVersionLocalizationsAsync(
                version.Id,
                request.Locale,
                limit: 10,
                cancellationToken).ConfigureAwait(false)).FirstOrDefault();
            AddCheck(checks, "localization", localization is not null, localization is null
                ? $"Localization '{request.Locale}' was not found for App Store version '{version.Id}'."
                : $"Localization '{request.Locale}' exists.");

            if (localization is not null)
            {
                CheckLocalizedMetadata(checks, localization, request);
                if (request.RequireScreenshots)
                    screenshotReadiness = await CheckScreenshotsAsync(checks, localization.Id, request, cancellationToken).ConfigureAwait(false);
            }
        }

        return new AppStoreConnectReleaseReadinessResult
        {
            AppId = request.AppId.Trim(),
            VersionString = request.VersionString.Trim(),
            BuildNumber = string.IsNullOrWhiteSpace(request.BuildNumber) ? null : request.BuildNumber!.Trim(),
            Platform = request.Platform,
            IsReady = checks.Count > 0 && checks.All(static check => check.Passed),
            Version = version,
            Build = build,
            SelectedBuildId = selectedBuildId,
            Localization = localization,
            ScreenshotSets = screenshotReadiness,
            Checks = checks.ToArray()
        };
    }

    private static void CheckLocalizedMetadata(
        List<AppStoreConnectReleaseReadinessCheck> checks,
        AppStoreConnectVersionLocalizationInfo localization,
        AppStoreConnectReleaseReadinessRequest request)
    {
        CheckField(checks, "metadata.description", request.RequireDescription, localization.Description, "description");
        CheckField(checks, "metadata.keywords", request.RequireKeywords, localization.Keywords, "keywords");
        CheckField(checks, "metadata.supportUrl", request.RequireSupportUrl, localization.SupportUrl, "support URL");
        CheckField(checks, "metadata.marketingUrl", request.RequireMarketingUrl, localization.MarketingUrl, "marketing URL");
        CheckField(checks, "metadata.promotionalText", request.RequirePromotionalText, localization.PromotionalText, "promotional text");
        CheckField(checks, "metadata.whatsNew", request.RequireWhatsNew, localization.WhatsNew, "what's new text");
    }

    private async Task<AppStoreConnectReleaseScreenshotSetReadiness[]> CheckScreenshotsAsync(
        List<AppStoreConnectReleaseReadinessCheck> checks,
        string localizationId,
        AppStoreConnectReleaseReadinessRequest request,
        CancellationToken cancellationToken)
    {
        var requiredTypes = ResolveRequiredScreenshotTypes(request);
        if (requiredTypes.Length == 0)
        {
            AddCheck(checks, "screenshots.configured", false, "No required screenshot display types were configured.");
            return Array.Empty<AppStoreConnectReleaseScreenshotSetReadiness>();
        }

        var sets = await _client.GetScreenshotSetsAsync(localizationId, limit: 200, cancellationToken).ConfigureAwait(false);
        var result = new List<AppStoreConnectReleaseScreenshotSetReadiness>();
        foreach (var displayType in requiredTypes)
        {
            var set = sets.FirstOrDefault(candidate =>
                string.Equals(candidate.ScreenshotDisplayType, displayType, StringComparison.OrdinalIgnoreCase));
            if (set is null)
            {
                AddCheck(checks, $"screenshots.{displayType}", false, $"Screenshot set '{displayType}' was not found.");
                result.Add(new AppStoreConnectReleaseScreenshotSetReadiness { ScreenshotDisplayType = displayType });
                continue;
            }

            var screenshots = await _client.GetScreenshotsAsync(set.Id, limit: 200, cancellationToken).ConfigureAwait(false);
            var deliveryStates = screenshots
                .Select(static screenshot => screenshot.AssetDeliveryState)
                .Where(static state => !string.IsNullOrWhiteSpace(state))
                .Select(static state => state!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static state => state, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var fileNames = screenshots
                .Select(static screenshot => screenshot.FileName)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Select(static name => name!)
                .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            result.Add(new AppStoreConnectReleaseScreenshotSetReadiness
            {
                ScreenshotDisplayType = displayType,
                ScreenshotSetId = set.Id,
                Count = screenshots.Length,
                AssetDeliveryStates = deliveryStates,
                FileNames = fileNames
            });

            var enough = screenshots.Length >= Math.Max(1, request.MinimumScreenshotsPerSet);
            AddCheck(checks, $"screenshots.{displayType}.count", enough, enough
                ? $"Screenshot set '{displayType}' has {screenshots.Length} screenshot(s)."
                : $"Screenshot set '{displayType}' has {screenshots.Length} screenshot(s), below required minimum {Math.Max(1, request.MinimumScreenshotsPerSet)}.");

            if (request.RequireCompleteScreenshots)
            {
                var complete = screenshots.Length > 0 &&
                               screenshots.All(static screenshot => string.Equals(screenshot.AssetDeliveryState, "COMPLETE", StringComparison.OrdinalIgnoreCase));
                AddCheck(checks, $"screenshots.{displayType}.complete", complete, complete
                    ? $"Screenshot set '{displayType}' assets are COMPLETE."
                    : $"Screenshot set '{displayType}' assets are not all COMPLETE.");
            }
        }

        return result.ToArray();
    }

    private static string[] ResolveRequiredScreenshotTypes(AppStoreConnectReleaseReadinessRequest request)
    {
        var configured = request.RequiredScreenshotDisplayTypes ?? Array.Empty<string>();
        if (configured.Length == 0 && request.ScreenshotSpec is not null)
            configured = request.ScreenshotSpec.ScreenshotSets.Select(static set => set.ScreenshotDisplayType).ToArray();

        return configured
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void CheckField(
        List<AppStoreConnectReleaseReadinessCheck> checks,
        string name,
        bool required,
        string? value,
        string label)
    {
        if (!required)
            return;

        var present = !string.IsNullOrWhiteSpace(value);
        AddCheck(checks, name, present, present
            ? $"Localized {label} is present."
            : $"Localized {label} is missing.");
    }

    private static void AddCheck(
        List<AppStoreConnectReleaseReadinessCheck> checks,
        string name,
        bool passed,
        string message)
    {
        checks.Add(new AppStoreConnectReleaseReadinessCheck
        {
            Name = name,
            Passed = passed,
            Message = message
        });
    }
}
