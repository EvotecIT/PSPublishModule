using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal static class WebSeoDoctorBaselineStore
{
    private const long MaxBaselineFileSizeBytes = 10 * 1024 * 1024;
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    internal static string Write(
        string baselineRoot,
        string? baselinePath,
        WebSeoDoctorResult result,
        bool mergeWithExisting,
        WebConsoleLogger? logger)
    {
        var resolvedPath = ResolveBaselinePath(baselineRoot, baselinePath);
        try
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (mergeWithExisting)
            {
                foreach (var key in LoadIssueHashes(resolvedPath))
                    keys.Add(key);
            }

            foreach (var issue in result.Issues)
            {
                if (string.IsNullOrWhiteSpace(issue.Key))
                    continue;
                var hashed = WebAuditKeyHasher.Hash(issue.Key);
                if (!string.IsNullOrWhiteSpace(hashed))
                    keys.Add(hashed);
            }

            var payload = new
            {
                version = 1,
                generatedAtUtc = DateTimeOffset.UtcNow,
                issueCount = keys.Count,
                keyFormat = WebAuditKeyHasher.DefaultFormat,
                issueKeyHashes = keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToArray()
            };

            var directory = Path.GetDirectoryName(resolvedPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(resolvedPath, json);
            return resolvedPath;
        }
        catch (Exception ex)
        {
            logger?.Warn($"SEO doctor baseline write failed: {ex.Message}");
            return resolvedPath;
        }
    }

    internal static bool TryLoadIssueHashes(string baselineRoot, string? baselinePath, out string resolvedPath, out string[] issueHashes)
    {
        resolvedPath = string.Empty;
        issueHashes = Array.Empty<string>();
        try
        {
            resolvedPath = ResolveBaselinePath(baselineRoot, baselinePath);
            if (!File.Exists(resolvedPath))
                return false;

            var info = new FileInfo(resolvedPath);
            if (info.Length > MaxBaselineFileSizeBytes)
                return false;

            issueHashes = LoadIssueHashes(resolvedPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return true;
        }
        catch
        {
            resolvedPath = string.Empty;
            issueHashes = Array.Empty<string>();
            return false;
        }
    }

    internal static string ResolveBaselinePath(string baselineRoot, string? baselinePath)
    {
        var candidate = string.IsNullOrWhiteSpace(baselinePath) ? ".powerforge/seo-baseline.json" : baselinePath.Trim();
        if (Path.IsPathRooted(candidate))
            return Path.GetFullPath(candidate);

        var normalizedRoot = NormalizeDirectoryPath(baselineRoot);
        var resolvedPath = Path.GetFullPath(Path.Combine(normalizedRoot, candidate));
        if (!IsWithinRoot(normalizedRoot, resolvedPath))
            throw new InvalidOperationException($"Baseline path must resolve under baseline root: {candidate}");
        return resolvedPath;
    }

    private static IEnumerable<string> LoadIssueHashes(string path)
    {
        if (!File.Exists(path))
            return Array.Empty<string>();

        var info = new FileInfo(path);
        if (info.Length > MaxBaselineFileSizeBytes)
            return Array.Empty<string>();

        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            if (TryGetPropertyIgnoreCase(root, "issueKeyHashes", out var issueKeyHashes) &&
                issueKeyHashes.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in issueKeyHashes.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String) continue;
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        hashes.Add(value);
                }
            }
            else if (TryGetPropertyIgnoreCase(root, "issueKeys", out var issueKeys) &&
                     issueKeys.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in issueKeys.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String) continue;
                    var value = item.GetString();
                    var hashed = WebAuditKeyHasher.Hash(value);
                    if (!string.IsNullOrWhiteSpace(hashed))
                        hashes.Add(hashed);
                }
            }

            if (TryGetPropertyIgnoreCase(root, "issues", out var issues) && issues.ValueKind == JsonValueKind.Array)
            {
                foreach (var issue in issues.EnumerateArray())
                {
                    if (issue.ValueKind != JsonValueKind.Object) continue;
                    if (!TryGetPropertyIgnoreCase(issue, "key", out var keyElement) || keyElement.ValueKind != JsonValueKind.String) continue;
                    var value = keyElement.GetString();
                    var hashed = WebAuditKeyHasher.Hash(value);
                    if (!string.IsNullOrWhiteSpace(hashed))
                        hashes.Add(hashed);
                }
            }
        }
        catch
        {
            return Array.Empty<string>();
        }

        return hashes;
    }

    private static string NormalizeDirectoryPath(string path)
    {
        var full = Path.GetFullPath(path);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    private static bool IsWithinRoot(string rootPath, string candidatePath)
    {
        var full = Path.GetFullPath(candidatePath);
        return full.StartsWith(rootPath, PathComparison);
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(propertyName, out value))
                return true;

            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}

