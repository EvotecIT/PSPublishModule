using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge;

internal sealed partial class StoreSubmissionService
{
    private static string[] ResolvePackagedAppFilesFromManifest(StoreSubmissionTarget target, string baseDirectory)
    {
        var manifestPathRaw = NormalizeNullable(target.ManifestPath);
        if (string.IsNullOrWhiteSpace(manifestPathRaw))
            return Array.Empty<string>();

        var manifestPath = ResolveFilePath(baseDirectory, manifestPathRaw!);
        var manifestRootRaw = NormalizeNullable(target.ManifestRoot);
        var manifestRoot = string.IsNullOrWhiteSpace(manifestRootRaw)
            ? baseDirectory
            : ResolveDirectoryPath(baseDirectory, manifestRootRaw!);
        if (!Directory.Exists(manifestRoot))
            throw new DirectoryNotFoundException($"Store submission manifest root not found: {manifestRoot}");

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath, Encoding.UTF8));
        var entries = EnumerateManifestEntries(document.RootElement).ToArray();
        if (entries.Length == 0)
            throw new InvalidOperationException($"Store submission manifest does not define any entries: {manifestPath}");

        var files = entries
            .Where(entry => ManifestEntryMatchesTarget(entry, target))
            .SelectMany(entry => EnumerateManifestOutputFiles(entry, manifestRoot))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        files = ApplyPackageSelection(files, target.PackagePatterns);
        if (files.Length == 0)
        {
            throw new InvalidOperationException(
                $"Store submission target '{target.Name}' did not resolve any package files from manifest '{manifestPath}'.");
        }

        return files;
    }

    private static IEnumerable<JsonElement> EnumerateManifestEntries(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in root.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Object)
                    yield return entry;
            }

            yield break;
        }

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("Entries", out var entries) &&
            entries.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Object)
                    yield return entry;
            }
        }
    }

    private static bool ManifestEntryMatchesTarget(JsonElement entry, StoreSubmissionTarget target)
    {
        return MatchesProperty(entry, "Category", "StorePackage") &&
               MatchesOptionalProperty(entry, "StorePackageId", target.StorePackageId) &&
               MatchesOptionalProperty(entry, "Target", target.SourceTarget) &&
               MatchesOptionalProperty(entry, "Runtime", target.Runtime) &&
               MatchesOptionalProperty(entry, "Framework", target.Framework) &&
               MatchesOptionalProperty(entry, "Style", target.Style);
    }

    private static bool MatchesOptionalProperty(JsonElement entry, string propertyName, string? expected)
    {
        var normalized = NormalizeNullable(expected);
        return string.IsNullOrWhiteSpace(normalized) || MatchesProperty(entry, propertyName, normalized!);
    }

    private static bool MatchesProperty(JsonElement entry, string propertyName, string expected)
    {
        return entry.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               string.Equals(property.GetString(), expected, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateManifestOutputFiles(JsonElement entry, string manifestRoot)
    {
        if (!entry.TryGetProperty("OutputFiles", out var outputFiles) || outputFiles.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var file in outputFiles.EnumerateArray())
        {
            if (file.ValueKind != JsonValueKind.String)
                continue;

            var path = NormalizeNullable(file.GetString());
            if (string.IsNullOrWhiteSpace(path))
                continue;

            yield return Path.GetFullPath(Path.IsPathRooted(path!) ? path! : Path.Combine(manifestRoot, path!));
        }
    }

    private static string[] ApplyPackageSelection(IEnumerable<string> files, string[]? configuredPatterns)
    {
        var candidates = (files ?? Array.Empty<string>())
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var patterns = (configuredPatterns ?? Array.Empty<string>())
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Select(pattern => pattern.Trim())
            .ToArray();
        if (patterns.Length > 0)
        {
            return candidates
                .Where(path => patterns.Any(pattern => FileNameMatchesPattern(path, pattern)))
                .ToArray();
        }

        var uploadFiles = candidates
            .Where(path => UploadPackagePatterns.Any(pattern => FileNameMatchesPattern(path, pattern)))
            .ToArray();
        if (uploadFiles.Length > 0)
            return uploadFiles;

        return candidates
            .Where(path => StorePackagePatterns.Any(pattern => FileNameMatchesPattern(path, pattern)))
            .ToArray();
    }

    private static bool FileNameMatchesPattern(string path, string pattern)
    {
        var fileName = Path.GetFileName(path);
        var regex = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        return Regex.IsMatch(fileName, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
