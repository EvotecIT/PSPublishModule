namespace PowerForge;

/// <summary>
/// Syncs local screenshot folders to App Store Connect screenshot sets.
/// </summary>
public sealed class AppStoreConnectScreenshotSyncService
{
    private readonly AppStoreConnectClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppStoreConnectScreenshotSyncService"/> class.
    /// </summary>
    /// <param name="client">App Store Connect client.</param>
    public AppStoreConnectScreenshotSyncService(AppStoreConnectClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>
    /// Syncs screenshots from local folders to App Store Connect.
    /// </summary>
    /// <param name="request">Sync request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Sync result.</returns>
    public async Task<AppStoreConnectScreenshotSyncResult> SyncAsync(
        AppStoreConnectScreenshotSyncRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var spec = request.Spec ?? throw new ArgumentException("Spec is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(spec.AppId))
            throw new ArgumentException("Spec.AppId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(spec.VersionString) && string.IsNullOrWhiteSpace(spec.VersionId))
            throw new ArgumentException("Spec.VersionString or Spec.VersionId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(spec.Locale))
            throw new ArgumentException("Spec.Locale is required.", nameof(request));
        if (spec.ScreenshotSets.Length == 0)
            throw new ArgumentException("At least one screenshot set mapping is required.", nameof(request));
        var duplicateDisplayTypes = spec.ScreenshotSets
            .Where(static set => !string.IsNullOrWhiteSpace(set.ScreenshotDisplayType))
            .GroupBy(static set => set.ScreenshotDisplayType.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();
        if (duplicateDisplayTypes.Length > 0)
            throw new ArgumentException($"Duplicate screenshot display type mapping: {string.Join(", ", duplicateDisplayTypes)}", nameof(request));

        var version = !string.IsNullOrWhiteSpace(spec.VersionId)
            ? new AppStoreConnectVersionInfo
            {
                Id = spec.VersionId!.Trim(),
                VersionString = spec.VersionString,
                Platform = spec.Platform.ToString()
            }
            : (await _client.GetVersionsAsync(
                spec.AppId,
                spec.VersionString,
                spec.Platform,
                limit: 10,
                cancellationToken).ConfigureAwait(false)).FirstOrDefault()
                ?? throw new InvalidOperationException($"App Store version '{spec.VersionString}' was not found for app '{spec.AppId}' and platform '{spec.Platform}'.");

        var localization = (await _client.GetVersionLocalizationsAsync(
            version.Id,
            spec.Locale,
            limit: 10,
            cancellationToken).ConfigureAwait(false)).FirstOrDefault()
            ?? throw new InvalidOperationException($"Localization '{spec.Locale}' was not found for App Store version '{version.Id}'.");

        var existingSets = await _client.GetScreenshotSetsAsync(
            localization.Id,
            limit: 200,
            cancellationToken).ConfigureAwait(false);

        var results = new List<AppStoreConnectScreenshotSetSyncResult>();
        foreach (var setSpec in spec.ScreenshotSets)
        {
            if (string.IsNullOrWhiteSpace(setSpec.ScreenshotDisplayType))
                throw new InvalidOperationException("ScreenshotDisplayType is required for every screenshot set mapping.");
            if (string.IsNullOrWhiteSpace(setSpec.Path))
                throw new InvalidOperationException($"Path is required for screenshot display type '{setSpec.ScreenshotDisplayType}'.");
            var maxCount = setSpec.MaxCount <= 0 ? 10 : setSpec.MaxCount;
            if (maxCount > 10)
                throw new InvalidOperationException($"MaxCount cannot exceed Apple's 10 screenshots per set limit for '{setSpec.ScreenshotDisplayType}'.");

            var folder = ResolvePath(request.BaseDirectory, setSpec.Path);
            if (!Directory.Exists(folder))
                throw new DirectoryNotFoundException($"Screenshot folder was not found: {folder}");

            var files = Directory.GetFiles(folder, string.IsNullOrWhiteSpace(setSpec.Filter) ? "*.png" : setSpec.Filter)
                .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
                .Take(maxCount)
                .ToArray();

            if (files.Length == 0)
                throw new InvalidOperationException($"No screenshots matched '{setSpec.Filter}' in '{folder}'.");

            var displayType = setSpec.ScreenshotDisplayType.Trim();
            var set = existingSets.FirstOrDefault(candidate =>
                string.Equals(candidate.ScreenshotDisplayType, displayType, StringComparison.OrdinalIgnoreCase));
            if (set is null)
            {
                set = await _client.CreateScreenshotSetAsync(localization.Id, displayType, cancellationToken).ConfigureAwait(false);
                existingSets = existingSets.Concat(new[] { set }).ToArray();
            }

            var deletedCount = 0;
            if (request.ReplaceExisting)
            {
                var existingScreenshots = await _client.GetScreenshotsAsync(set.Id, limit: 200, cancellationToken).ConfigureAwait(false);
                foreach (var screenshot in existingScreenshots)
                {
                    await _client.DeleteScreenshotAsync(screenshot.Id, cancellationToken).ConfigureAwait(false);
                    deletedCount++;
                }
            }

            var uploaded = new List<AppStoreConnectScreenshotUploadResult>();
            foreach (var file in files)
                uploaded.Add(await _client.UploadScreenshotAsync(set.Id, file, cancellationToken).ConfigureAwait(false));

            results.Add(new AppStoreConnectScreenshotSetSyncResult
            {
                ScreenshotDisplayType = displayType,
                ScreenshotSetId = set.Id,
                Path = folder,
                DeletedCount = deletedCount,
                Uploaded = uploaded.ToArray()
            });
        }

        return new AppStoreConnectScreenshotSyncResult
        {
            Version = version,
            Localization = localization,
            ScreenshotSets = results.ToArray()
        };
    }

    private static string ResolvePath(string baseDirectory, string path)
        => System.IO.Path.IsPathRooted(path)
            ? System.IO.Path.GetFullPath(path)
            : System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDirectory, path));
}
