using System.Text.Json;

namespace PowerForge.Web;

/// <summary>Generates version switcher metadata for multi-version documentation sites.</summary>
public static class WebVersionHubGenerator
{
    /// <summary>Generates version hub JSON output.</summary>
    /// <param name="options">Generation options.</param>
    /// <returns>Result payload.</returns>
    public static WebVersionHubResult Generate(WebVersionHubOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.OutputPath))
            throw new ArgumentException("OutputPath is required.", nameof(options));

        var warnings = new List<string>();
        var baseDir = ResolveBaseDirectory(options.BaseDirectory);
        var outputPath = ResolvePath(options.OutputPath, baseDir, warnings, "OutputPath");
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("OutputPath is invalid.", nameof(options));

        var entries = new List<WebVersionHubEntry>();
        AddExplicitEntries(entries, options.Entries, warnings);
        AddDiscoveredEntries(entries, options, baseDir, warnings);

        if (entries.Count == 0)
            throw new InvalidOperationException("version-hub requires at least one version entry (versions/entries or discoverRoot).");

        var ordered = OrderEntries(entries);
        NormalizeFlags(ordered, options.SetLatestFromNewest);

        var latest = ordered.FirstOrDefault(static entry => entry.Latest);
        var lts = ordered.FirstOrDefault(static entry => entry.Lts);

        var document = new WebVersionHubDocument
        {
            Title = string.IsNullOrWhiteSpace(options.Title) ? "Version Hub" : options.Title!.Trim(),
            GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
            LatestPath = latest?.Path,
            LtsPath = lts?.Path,
            Versions = ordered,
            Warnings = warnings
        };

        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDir))
            Directory.CreateDirectory(outputDir);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(document, WebJson.Options));

        return new WebVersionHubResult
        {
            OutputPath = outputPath,
            VersionCount = ordered.Count,
            LatestVersion = latest?.Version,
            Warnings = warnings.ToArray()
        };
    }

    private static void AddExplicitEntries(List<WebVersionHubEntry> target, IEnumerable<WebVersionHubEntryInput>? inputs, List<string> warnings)
    {
        if (inputs is null)
            return;

        foreach (var input in inputs)
        {
            if (input is null)
                continue;

            var version = NormalizeVersion(input.Version ?? input.Id);
            if (string.IsNullOrWhiteSpace(version))
            {
                warnings.Add("Version entry skipped: missing version/id.");
                continue;
            }

            var path = NormalizeRoute(input.Path);
            if (string.IsNullOrWhiteSpace(path))
            {
                warnings.Add($"Version '{version}' skipped: missing path.");
                continue;
            }

            var id = string.IsNullOrWhiteSpace(input.Id) ? version : input.Id!.Trim();
            if (target.Any(existing => string.Equals(existing.Id, id, StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(existing.Version, version, StringComparison.OrdinalIgnoreCase)))
            {
                warnings.Add($"Version entry skipped: duplicate version/id '{version}'.");
                continue;
            }

            target.Add(new WebVersionHubEntry
            {
                Id = id,
                Version = version,
                Label = string.IsNullOrWhiteSpace(input.Label) ? version : input.Label!.Trim(),
                Path = path,
                Channel = Clean(input.Channel),
                Support = Clean(input.Support),
                Latest = input.Latest,
                Lts = input.Lts,
                Deprecated = input.Deprecated,
                Aliases = input.Aliases
                    .Where(static alias => !string.IsNullOrWhiteSpace(alias))
                    .Select(static alias => alias.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            });
        }
    }

    private static void AddDiscoveredEntries(List<WebVersionHubEntry> target, WebVersionHubOptions options, string? baseDir, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(options.DiscoverRoot))
            return;

        var rootPath = ResolvePath(options.DiscoverRoot, baseDir, warnings, "DiscoverRoot");
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            warnings.Add($"DiscoverRoot not found: {options.DiscoverRoot}");
            return;
        }

        var pattern = string.IsNullOrWhiteSpace(options.DiscoverPattern) ? "v*" : options.DiscoverPattern;
        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(rootPath, pattern, SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex)
        {
            warnings.Add($"Failed to enumerate DiscoverRoot '{rootPath}': {ex.GetType().Name}: {ex.Message}");
            return;
        }

        var basePath = NormalizeBasePath(options.BasePath);
        foreach (var directory in directories)
        {
            var folder = Path.GetFileName(directory);
            if (string.IsNullOrWhiteSpace(folder))
                continue;

            var version = NormalizeVersion(folder);
            var id = version;
            var path = CombineRoute(basePath, folder);

            if (target.Any(existing => string.Equals(existing.Id, id, StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(existing.Version, version, StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(existing.Path, path, StringComparison.OrdinalIgnoreCase)))
                continue;

            target.Add(new WebVersionHubEntry
            {
                Id = id,
                Version = version,
                Label = version,
                Path = path,
                Channel = InferChannel(version),
                Support = null,
                Latest = false,
                Lts = false,
                Deprecated = false
            });
        }
    }

    private static List<WebVersionHubEntry> OrderEntries(IEnumerable<WebVersionHubEntry> entries)
    {
        return entries
            .Select(entry => new { Entry = entry, Info = ParseVersionInfo(entry.Version) })
            .OrderByDescending(item => item.Info.IsSemantic)
            .ThenByDescending(item => item.Info.Major)
            .ThenByDescending(item => item.Info.Minor)
            .ThenByDescending(item => item.Info.Patch)
            .ThenByDescending(item => item.Info.Revision)
            .ThenBy(item => item.Info.IsPrerelease ? 1 : 0)
            .ThenByDescending(item => item.Entry.Version, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Entry)
            .ToList();
    }

    private static void NormalizeFlags(List<WebVersionHubEntry> entries, bool setLatestFromNewest)
    {
        if (entries.Count == 0)
            return;

        if (!entries.Any(static entry => entry.Latest) && setLatestFromNewest)
            entries[0].Latest = true;

        var latest = entries.FirstOrDefault(static entry => entry.Latest);
        if (latest is not null)
        {
            foreach (var entry in entries)
            {
                if (!ReferenceEquals(entry, latest))
                    entry.Latest = false;
            }

            if (!latest.Aliases.Contains("latest", StringComparer.OrdinalIgnoreCase))
                latest.Aliases.Add("latest");
        }
    }

    private static (bool IsSemantic, bool IsPrerelease, int Major, int Minor, int Patch, int Revision) ParseVersionInfo(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return (false, false, -1, -1, -1, -1);

        var normalized = NormalizeVersion(value);
        var prerelease = normalized.Contains('-', StringComparison.Ordinal);
        var core = prerelease ? normalized.Substring(0, normalized.IndexOf('-', StringComparison.Ordinal)) : normalized;
        var parts = core.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Length > 4)
            return (false, prerelease, -1, -1, -1, -1);

        var numeric = new int[] { 0, 0, 0, 0 };
        for (var index = 0; index < parts.Length; index++)
        {
            if (!int.TryParse(parts[index], out numeric[index]))
                return (false, prerelease, -1, -1, -1, -1);
        }

        return (true, prerelease, numeric[0], numeric[1], numeric[2], numeric[3]);
    }

    private static string NormalizeVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase) && trimmed.Length > 1 && char.IsDigit(trimmed[1]))
            return trimmed.Substring(1);
        return trimmed;
    }

    private static string? Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string NormalizeBasePath(string? basePath)
    {
        if (string.IsNullOrWhiteSpace(basePath))
            return "/docs/";

        var normalized = basePath.Trim().Replace('\\', '/');
        if (!normalized.StartsWith("/", StringComparison.Ordinal))
            normalized = "/" + normalized;
        if (!normalized.EndsWith("/", StringComparison.Ordinal))
            normalized += "/";
        return normalized;
    }

    private static string CombineRoute(string basePath, string segment)
    {
        var cleanSegment = segment.Trim().Trim('/').Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(cleanSegment))
            return basePath;

        return $"{basePath.TrimEnd('/')}/{cleanSegment}/";
    }

    private static string? NormalizeRoute(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim().Replace('\\', '/');
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             absolute.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            return absolute.ToString();

        if (!trimmed.StartsWith("/", StringComparison.Ordinal))
            trimmed = "/" + trimmed;

        var leaf = Path.GetFileName(trimmed);
        if (!trimmed.EndsWith("/", StringComparison.Ordinal) && !leaf.Contains('.', StringComparison.Ordinal))
            trimmed += "/";

        return trimmed;
    }

    private static string? InferChannel(string version)
    {
        if (version.Contains("preview", StringComparison.OrdinalIgnoreCase) ||
            version.Contains("alpha", StringComparison.OrdinalIgnoreCase) ||
            version.Contains("beta", StringComparison.OrdinalIgnoreCase) ||
            version.Contains("rc", StringComparison.OrdinalIgnoreCase))
            return "preview";
        return "stable";
    }

    private static string? ResolveBaseDirectory(string? baseDir)
    {
        if (string.IsNullOrWhiteSpace(baseDir))
            return null;
        try
        {
            return Path.GetFullPath(baseDir.Trim().Trim('\"'));
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolvePath(string path, string? baseDir, List<string> warnings, string label)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var trimmed = path.Trim().Trim('\"');
        string full;
        try
        {
            full = Path.IsPathRooted(trimmed) || string.IsNullOrWhiteSpace(baseDir)
                ? Path.GetFullPath(trimmed)
                : Path.GetFullPath(Path.Combine(baseDir, trimmed));
        }
        catch (Exception ex)
        {
            warnings.Add($"{label} could not be resolved: {ex.GetType().Name}: {ex.Message}");
            return null;
        }

        if (!string.IsNullOrWhiteSpace(baseDir) && !IsUnderRoot(full, baseDir))
            warnings.Add($"{label} resolves outside base directory: {full}");

        return full;
    }

    private static bool IsUnderRoot(string path, string root)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(path, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return true;
        normalizedRoot += Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }
}
