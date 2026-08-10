namespace PowerForge;

/// <summary>
/// Syncs local screenshot folders to App Store Connect screenshot sets.
/// </summary>
public sealed class AppStoreConnectScreenshotSyncService
{
    private const int AppleScreenshotSetLimit = 10;

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
            throw new ArgumentException(
                spec.UseReleaseVersion
                    ? "Spec.UseReleaseVersion must be resolved by the unified Apple release workflow before screenshot sync."
                    : "Spec.VersionString or Spec.VersionId is required.",
                nameof(request));
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

        var validation = new AppStoreConnectScreenshotSyncConfigValidator()
            .Validate(spec, request.BaseDirectory, expectedSourceCommit: request.ExpectedSourceCommit);
        if (!validation.IsValid)
        {
            var messages = validation.Messages
                .Concat(validation.ScreenshotSets.SelectMany(static set => set.Messages))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            throw new InvalidOperationException(
                $"Screenshot preflight failed: {string.Join(" ", messages)}");
        }

        var sourceSets = spec.ScreenshotSets
            .Select(setSpec => PreflightScreenshotSet(request.BaseDirectory, setSpec))
            .ToArray();
        using var screenshotSnapshot = CreateScreenshotSnapshot(sourceSets, request.ExpectedFileSha256);
        var preflightedSets = screenshotSnapshot.Sets;

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

        var plannedSets = new List<PlannedScreenshotSet>();
        foreach (var preflightedSet in preflightedSets)
        {
            var set = existingSets.FirstOrDefault(candidate =>
                string.Equals(candidate.ScreenshotDisplayType, preflightedSet.ScreenshotDisplayType, StringComparison.OrdinalIgnoreCase));
            AppStoreConnectScreenshotInfo[] existingScreenshots = Array.Empty<AppStoreConnectScreenshotInfo>();
            if (set is not null)
                existingScreenshots = await _client.GetScreenshotsAsync(set.Id, limit: 200, cancellationToken).ConfigureAwait(false);

            var missingFiles = FindMissingFiles(preflightedSet.Files, existingScreenshots);
            if (!request.ReplaceExisting && existingScreenshots.Length + missingFiles.Length > AppleScreenshotSetLimit)
            {
                throw new InvalidOperationException(
                    $"Screenshot display type '{preflightedSet.ScreenshotDisplayType}' already has {existingScreenshots.Length} screenshots; " +
                    $"uploading {missingFiles.Length} missing screenshots would exceed Apple's {AppleScreenshotSetLimit} screenshots per set limit.");
            }

            plannedSets.Add(new PlannedScreenshotSet(preflightedSet, set, existingScreenshots));
        }

        if (request.ReplaceExisting && !string.IsNullOrWhiteSpace(request.ExpectedRemoteInventorySha256))
        {
            var remoteInventory = plannedSets.Select(static plannedSet =>
                new AppStoreConnectReleaseScreenshotSetReadiness
                {
                    ScreenshotDisplayType = plannedSet.Preflighted.ScreenshotDisplayType,
                    ScreenshotSetId = plannedSet.ScreenshotSet?.Id,
                    Count = plannedSet.ExistingScreenshots.Length,
                    Screenshots = plannedSet.ExistingScreenshots.Select(static screenshot =>
                        new AppStoreConnectReleaseScreenshotAssetReadiness
                        {
                            Id = screenshot.Id,
                            FileName = screenshot.FileName,
                            FileSize = screenshot.FileSize,
                            SourceFileChecksum = screenshot.SourceFileChecksum,
                            AssetDeliveryState = screenshot.AssetDeliveryState
                        }).ToArray()
                });
            var actualInventorySha256 = AppStoreConnectScreenshotInventory.ComputeSha256(remoteInventory);
            if (!actualInventorySha256.Equals(request.ExpectedRemoteInventorySha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "App Store Connect screenshots changed after Apple plan approval. Review a new exact screenshot replacement plan before deleting remote assets.");
            }
        }

        var results = new List<AppStoreConnectScreenshotSetSyncResult>();
        foreach (var plannedSet in plannedSets)
        {
            var displayType = plannedSet.Preflighted.ScreenshotDisplayType;
            var set = plannedSet.ScreenshotSet;
            if (set is null)
            {
                set = await _client.CreateScreenshotSetAsync(localization.Id, displayType, cancellationToken).ConfigureAwait(false);
                existingSets = existingSets.Concat(new[] { set }).ToArray();
            }

            var deletedCount = 0;
            var filesToUpload = FindMissingFiles(plannedSet.Preflighted.Files, plannedSet.ExistingScreenshots);
            if (request.ReplaceExisting)
            {
                // MD5 is an App Store Connect transport field, not approval evidence. A destructive
                // replacement must create fresh asset identities for every approved immutable byte set.
                foreach (var screenshot in plannedSet.ExistingScreenshots)
                {
                    await _client.DeleteScreenshotAsync(screenshot.Id, cancellationToken).ConfigureAwait(false);
                    deletedCount++;
                }

                filesToUpload = plannedSet.Preflighted.Files;
            }

            var uploaded = new List<AppStoreConnectScreenshotUploadResult>();
            foreach (var file in filesToUpload)
            {
                var upload = await _client.UploadScreenshotAsync(
                    set.Id,
                    file,
                    screenshotSnapshot.GetSha256(file),
                    cancellationToken).ConfigureAwait(false);
                upload.FilePath = screenshotSnapshot.GetSourcePath(file);
                uploaded.Add(upload);
            }

            results.Add(new AppStoreConnectScreenshotSetSyncResult
            {
                ScreenshotDisplayType = displayType,
                ScreenshotSetId = set.Id,
                Path = plannedSet.Preflighted.Folder,
                DeletedCount = deletedCount,
                Uploaded = uploaded.ToArray()
            });

            if (request.ReplaceExisting)
            {
                var finalScreenshots = await _client.GetScreenshotsAsync(
                    set.Id,
                    limit: 200,
                    cancellationToken).ConfigureAwait(false);
                var expectedChecksums = plannedSet.Preflighted.Files
                    .Select(ComputeSourceChecksum)
                    .ToArray();
                var finalChecksums = finalScreenshots
                    .Select(static screenshot => screenshot.SourceFileChecksum?.Trim() ?? string.Empty)
                    .ToArray();
                var expectedIds = uploaded
                    .Select(static upload => upload.Screenshot.Id)
                    .ToArray();
                var finalIds = finalScreenshots
                    .Select(static screenshot => screenshot.Id)
                    .ToArray();
                if (finalIds.Length != expectedIds.Length ||
                    !finalIds.SequenceEqual(expectedIds, StringComparer.Ordinal) ||
                    finalChecksums.Length != expectedChecksums.Length ||
                    !finalChecksums.SequenceEqual(expectedChecksums, StringComparer.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"App Store Connect screenshot inventory for '{displayType}' changed during replacement. " +
                        "The final remote inventory does not exactly match the approved screenshot bytes; review and run a new plan before submission.");
                }
            }
        }

        return new AppStoreConnectScreenshotSyncResult
        {
            Version = version,
            Localization = localization,
            ScreenshotSets = results.ToArray()
        };
    }

    private static string ComputeSourceChecksum(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var md5 = System.Security.Cryptography.MD5.Create();
        return BitConverter.ToString(md5.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static ScreenshotSnapshot CreateScreenshotSnapshot(
        IReadOnlyCollection<PreflightedScreenshotSet> sourceSets,
        IReadOnlyDictionary<string, string>? expectedFileSha256)
    {
        var comparer = Path.DirectorySeparatorChar == '\\'
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var expected = expectedFileSha256 is null
            ? null
            : expectedFileSha256.ToDictionary(
                static value => value.Key,
                static value => value.Value,
                comparer);
        var root = Path.Combine(Path.GetTempPath(), "PowerForge", "appstore-screenshot-snapshot", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
#if NET8_0_OR_GREATER
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
#endif
        try
        {
            var approvedScreenshots = expected is null
                ? null
                : SelectApprovedScreenshotFiles(sourceSets, expected, comparer);
            var mappings = new Dictionary<string, string>(comparer);
            var sha256 = new Dictionary<string, string>(comparer);
            var consumedApprovedFiles = new HashSet<string>(comparer);
            var sets = sourceSets.Select((set, setIndex) =>
            {
                var setRoot = Path.Combine(root, setIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Directory.CreateDirectory(setRoot);
                var files = set.Files.Select(sourcePath =>
                {
                    var source = Path.GetFullPath(sourcePath);
                    string? expectedSha256 = null;
                    if (approvedScreenshots is not null &&
                        !approvedScreenshots.TryGetValue(source, out expectedSha256))
                    {
                        throw new InvalidOperationException(
                            $"Screenshot '{source}' was not part of the approved Apple release plan. Review a new exact plan before upload.");
                    }
                    consumedApprovedFiles.Add(source);

                    var snapshotPath = Path.Combine(setRoot, Path.GetFileName(source));
                    File.Copy(source, snapshotPath, overwrite: false);
                    var actualSha256 = ComputeSha256(snapshotPath);
                    if (approvedScreenshots is not null)
                    {
                        if (!actualSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException(
                                $"Screenshot '{source}' changed after Apple plan approval. Review the exact replacement bytes before upload.");
                        }
                    }
                    mappings[snapshotPath] = source;
                    sha256[snapshotPath] = actualSha256;
                    return snapshotPath;
                }).ToArray();
                return new PreflightedScreenshotSet(
                    set.ScreenshotDisplayType,
                    set.Folder,
                    set.Filter,
                    set.MaxCount,
                    files);
            }).ToArray();
            if (approvedScreenshots is not null)
            {
                var missing = approvedScreenshots.Keys
                    .Where(path => !consumedApprovedFiles.Contains(Path.GetFullPath(path)))
                    .OrderBy(static path => path, comparer)
                    .ToArray();
                if (missing.Length > 0)
                {
                    throw new InvalidOperationException(
                        "Approved screenshots disappeared before the immutable upload snapshot was created: " +
                        string.Join(", ", missing));
                }
            }
            return new ScreenshotSnapshot(root, sets, mappings, sha256);
        }
        catch
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
            throw;
        }
    }

    private static Dictionary<string, string> SelectApprovedScreenshotFiles(
        IReadOnlyCollection<PreflightedScreenshotSet> sourceSets,
        IReadOnlyDictionary<string, string> expected,
        StringComparer pathComparer)
    {
        var selected = new Dictionary<string, string>(pathComparer);
        foreach (var set in sourceSets)
        {
            var approved = expected
                .Where(item => pathComparer.Equals(
                    Path.GetDirectoryName(Path.GetFullPath(item.Key)),
                    set.Folder))
                .Where(item => MatchesScreenshotFilter(Path.GetFileName(item.Key), set.Filter))
                .OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Take(set.MaxCount);
            foreach (var item in approved)
                selected[Path.GetFullPath(item.Key)] = item.Value;
        }
        return selected;
    }

    private static bool MatchesScreenshotFilter(string fileName, string filter)
    {
        var expression = "^" + System.Text.RegularExpressions.Regex.Escape(filter)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        var options = System.Text.RegularExpressions.RegexOptions.CultureInvariant;
        if (Path.DirectorySeparatorChar == '\\')
            options |= System.Text.RegularExpressions.RegexOptions.IgnoreCase;
        return System.Text.RegularExpressions.Regex.IsMatch(fileName, expression, options);
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static string[] FindMissingFiles(
        IEnumerable<string> files,
        IEnumerable<AppStoreConnectScreenshotInfo> existingScreenshots)
    {
        var available = existingScreenshots
            .Where(static screenshot => !string.IsNullOrWhiteSpace(screenshot.SourceFileChecksum))
            .GroupBy(static screenshot => screenshot.SourceFileChecksum!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.Count(),
                StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        foreach (var file in files)
        {
            var checksum = ComputeSourceChecksum(file);
            if (available.TryGetValue(checksum, out var count) && count > 0)
                available[checksum] = count - 1;
            else
                missing.Add(file);
        }

        return missing.ToArray();
    }

    private static string ResolvePath(string baseDirectory, string path)
        => System.IO.Path.IsPathRooted(path)
            ? System.IO.Path.GetFullPath(path)
            : System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDirectory, path));

    private static PreflightedScreenshotSet PreflightScreenshotSet(string baseDirectory, AppStoreConnectScreenshotSetSyncSpec setSpec)
    {
        if (string.IsNullOrWhiteSpace(setSpec.ScreenshotDisplayType))
            throw new InvalidOperationException("ScreenshotDisplayType is required for every screenshot set mapping.");
        if (string.IsNullOrWhiteSpace(setSpec.Path))
            throw new InvalidOperationException($"Path is required for screenshot display type '{setSpec.ScreenshotDisplayType}'.");

        var maxCount = setSpec.MaxCount <= 0 ? AppleScreenshotSetLimit : setSpec.MaxCount;
        if (maxCount > AppleScreenshotSetLimit)
            throw new InvalidOperationException($"MaxCount cannot exceed Apple's {AppleScreenshotSetLimit} screenshots per set limit for '{setSpec.ScreenshotDisplayType}'.");

        var folder = ResolvePath(baseDirectory, setSpec.Path);
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"Screenshot folder was not found: {folder}");

        var filter = string.IsNullOrWhiteSpace(setSpec.Filter) ? "*.png" : setSpec.Filter;
        var files = Directory.GetFiles(folder, filter)
            .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
            .Take(maxCount)
            .ToArray();

        if (files.Length == 0)
            throw new InvalidOperationException($"No screenshots matched '{filter}' in '{folder}'.");

        return new PreflightedScreenshotSet(
            setSpec.ScreenshotDisplayType.Trim(),
            folder,
            filter,
            maxCount,
            files);
    }

    private sealed class PreflightedScreenshotSet
    {
        public PreflightedScreenshotSet(
            string screenshotDisplayType,
            string folder,
            string filter,
            int maxCount,
            string[] files)
        {
            ScreenshotDisplayType = screenshotDisplayType;
            Folder = folder;
            Filter = filter;
            MaxCount = maxCount;
            Files = files;
        }

        public string ScreenshotDisplayType { get; }

        public string Folder { get; }

        public string Filter { get; }

        public int MaxCount { get; }

        public string[] Files { get; }
    }

    private sealed class PlannedScreenshotSet
    {
        public PlannedScreenshotSet(
            PreflightedScreenshotSet preflighted,
            AppStoreConnectScreenshotSetInfo? screenshotSet,
            AppStoreConnectScreenshotInfo[] existingScreenshots)
        {
            Preflighted = preflighted;
            ScreenshotSet = screenshotSet;
            ExistingScreenshots = existingScreenshots;
        }

        public PreflightedScreenshotSet Preflighted { get; }

        public AppStoreConnectScreenshotSetInfo? ScreenshotSet { get; }

        public AppStoreConnectScreenshotInfo[] ExistingScreenshots { get; }
    }

    private sealed class ScreenshotSnapshot : IDisposable
    {
        private readonly string _root;
        private readonly IReadOnlyDictionary<string, string> _sourcePaths;
        private readonly IReadOnlyDictionary<string, string> _sha256;

        public ScreenshotSnapshot(
            string root,
            PreflightedScreenshotSet[] sets,
            IReadOnlyDictionary<string, string> sourcePaths,
            IReadOnlyDictionary<string, string> sha256)
        {
            _root = root;
            Sets = sets;
            _sourcePaths = sourcePaths;
            _sha256 = sha256;
        }

        public PreflightedScreenshotSet[] Sets { get; }

        public string GetSourcePath(string snapshotPath) => _sourcePaths[snapshotPath];

        public string GetSha256(string snapshotPath) => _sha256[snapshotPath];

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        }
    }
}
