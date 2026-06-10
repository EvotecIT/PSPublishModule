namespace PowerForge;

/// <summary>
/// Validates App Store Connect screenshot sync configuration against local files.
/// </summary>
public sealed class AppStoreConnectScreenshotSyncConfigValidator
{
    /// <summary>
    /// Validates a screenshot sync configuration without calling App Store Connect.
    /// </summary>
    /// <param name="spec">Screenshot sync configuration.</param>
    /// <param name="baseDirectory">Base directory used to resolve relative screenshot paths.</param>
    /// <param name="configPath">Optional configuration path used in the result.</param>
    /// <returns>Local validation result.</returns>
    public AppStoreConnectScreenshotSyncValidationResult Validate(
        AppStoreConnectScreenshotSyncSpec spec,
        string baseDirectory,
        string? configPath = null)
    {
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));

        var messages = new List<string>();
        if (string.IsNullOrWhiteSpace(spec.AppId))
            messages.Add("AppId is required.");
        if (string.IsNullOrWhiteSpace(spec.VersionString) && string.IsNullOrWhiteSpace(spec.VersionId))
            messages.Add("VersionString or VersionId is required.");
        if (string.IsNullOrWhiteSpace(spec.Locale))
            messages.Add("Locale is required.");
        if (spec.ScreenshotSets.Length == 0)
            messages.Add("At least one screenshot set mapping is required.");

        var setResults = new List<AppStoreConnectScreenshotSetSyncValidationResult>();
        foreach (var set in spec.ScreenshotSets)
            setResults.Add(ValidateSet(set, baseDirectory));

        messages.AddRange(FindDuplicateDisplayTypes(spec.ScreenshotSets));
        var isValid = messages.Count == 0 && setResults.All(static set => set.IsValid);

        return new AppStoreConnectScreenshotSyncValidationResult
        {
            ConfigPath = configPath ?? string.Empty,
            IsValid = isValid,
            Messages = messages.ToArray(),
            ScreenshotSets = setResults.ToArray()
        };
    }

    private static AppStoreConnectScreenshotSetSyncValidationResult ValidateSet(
        AppStoreConnectScreenshotSetSyncSpec set,
        string baseDirectory)
    {
        var messages = new List<string>();
        var displayType = set.ScreenshotDisplayType?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(displayType))
            messages.Add("ScreenshotDisplayType is required.");
        if (string.IsNullOrWhiteSpace(set.Path))
            messages.Add("Path is required.");

        var folder = string.IsNullOrWhiteSpace(set.Path)
            ? string.Empty
            : ResolvePath(baseDirectory, set.Path);
        var filter = string.IsNullOrWhiteSpace(set.Filter) ? "*.png" : set.Filter.Trim();
        var maxCount = set.MaxCount <= 0 ? 10 : set.MaxCount;
        var files = Array.Empty<string>();

        if (!string.IsNullOrWhiteSpace(folder))
        {
            if (!Directory.Exists(folder))
            {
                messages.Add($"Screenshot folder was not found: {folder}");
            }
            else
            {
                files = Directory.GetFiles(folder, filter)
                    .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
                    .Take(maxCount)
                    .ToArray();
                if (files.Length == 0)
                    messages.Add($"No screenshots matched '{filter}' in '{folder}'.");
                if (files.Length > 10)
                    messages.Add($"Apple accepts at most 10 screenshots per screenshot set; {files.Length} files would be selected.");
            }
        }

        return new AppStoreConnectScreenshotSetSyncValidationResult
        {
            ScreenshotDisplayType = displayType,
            Path = folder,
            Filter = filter,
            FileCount = files.Length,
            Files = files,
            IsValid = messages.Count == 0,
            Messages = messages.ToArray()
        };
    }

    private static string[] FindDuplicateDisplayTypes(AppStoreConnectScreenshotSetSyncSpec[] sets)
    {
        return sets
            .Where(static set => !string.IsNullOrWhiteSpace(set.ScreenshotDisplayType))
            .GroupBy(static set => set.ScreenshotDisplayType.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => $"Duplicate screenshot display type mapping: {group.Key}")
            .ToArray();
    }

    private static string ResolvePath(string baseDirectory, string path)
        => System.IO.Path.IsPathRooted(path)
            ? System.IO.Path.GetFullPath(path)
            : System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDirectory, path));
}
