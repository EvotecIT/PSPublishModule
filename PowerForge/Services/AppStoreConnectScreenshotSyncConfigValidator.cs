namespace PowerForge;

using System.Security.Cryptography;

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
            setResults.Add(ValidateSet(set, spec.Quality ?? new AppStoreConnectScreenshotQualitySpec(), baseDirectory));

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
        AppStoreConnectScreenshotQualitySpec quality,
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
        var dimensions = Array.Empty<string>();
        if (maxCount > 10)
            messages.Add("MaxCount cannot exceed Apple's 10 screenshots per set limit.");

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
                else if (quality.Enabled)
                    dimensions = ValidateImageQuality(files, set, quality, messages);
            }
        }

        return new AppStoreConnectScreenshotSetSyncValidationResult
        {
            ScreenshotDisplayType = displayType,
            Path = folder,
            Filter = filter,
            FileCount = files.Length,
            Files = files,
            Dimensions = dimensions,
            IsValid = messages.Count == 0,
            Messages = messages.ToArray()
        };
    }

    private static string[] ValidateImageQuality(
        string[] files,
        AppStoreConnectScreenshotSetSyncSpec set,
        AppStoreConnectScreenshotQualitySpec quality,
        List<string> messages)
    {
        var dimensions = new List<string>();
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var allowed = (set.AllowedDimensions ?? Array.Empty<string>())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var info = new FileInfo(file);
            if (info.Length < quality.MinimumFileBytes)
                messages.Add($"Screenshot '{info.Name}' is only {info.Length} bytes; minimum is {quality.MinimumFileBytes} bytes.");

            if (!TryReadPngDimensions(file, out var width, out var height))
            {
                messages.Add($"Screenshot '{info.Name}' is not a valid PNG with a readable IHDR header.");
                dimensions.Add("unknown");
                continue;
            }

            var dimension = $"{width}x{height}";
            dimensions.Add(dimension);
            if (allowed.Count > 0 && !allowed.Contains(dimension))
                messages.Add($"Screenshot '{info.Name}' has dimensions {dimension}; allowed: {string.Join(", ", allowed)}.");

            var megapixels = width * (double)height / 1_000_000d;
            var kilobytesPerMegapixel = megapixels <= 0 ? 0 : info.Length / 1024d / megapixels;
            if (quality.MinimumKilobytesPerMegapixel > 0 &&
                kilobytesPerMegapixel < quality.MinimumKilobytesPerMegapixel)
            {
                messages.Add(
                    $"Screenshot '{info.Name}' has unusually low visual detail " +
                    $"({kilobytesPerMegapixel:0.##} KB/MP; minimum {quality.MinimumKilobytesPerMegapixel:0.##} KB/MP).");
            }

            if (!quality.RejectDuplicates)
                continue;
            var hash = ComputeSha256(file);
            if (hashes.TryGetValue(hash, out var existing))
                messages.Add($"Screenshot '{info.Name}' duplicates '{existing}' in the same display set.");
            else
                hashes[hash] = info.Name;
        }

        if (quality.RequireConsistentDimensions &&
            dimensions.Where(static value => value != "unknown").Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any())
        {
            messages.Add("Screenshots in the same display set must use consistent dimensions.");
        }

        return dimensions.ToArray();
    }

    private static bool TryReadPngDimensions(string path, out int width, out int height)
    {
        width = 0;
        height = 0;
        var header = new byte[24];
        using var stream = File.OpenRead(path);
        if (stream.Read(header, 0, header.Length) != header.Length)
            return false;
        var signature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        if (!header.Take(8).SequenceEqual(signature) ||
            header[12] != (byte)'I' ||
            header[13] != (byte)'H' ||
            header[14] != (byte)'D' ||
            header[15] != (byte)'R')
        {
            return false;
        }

        width = ReadBigEndianInt32(header, 16);
        height = ReadBigEndianInt32(header, 20);
        return width > 0 && height > 0;
    }

    private static int ReadBigEndianInt32(byte[] value, int offset)
        => value[offset] << 24 |
           value[offset + 1] << 16 |
           value[offset + 2] << 8 |
           value[offset + 3];

    private static string ComputeSha256(string path)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(path);
        return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty);
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
