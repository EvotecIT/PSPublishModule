using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PowerForge.Web.Cli;

internal static class WebVerifyBaselineStore
{
    private const long MaxBaselineFileSizeBytes = 10 * 1024 * 1024;
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    /// <summary>
    /// Normalizes a warning string into a stable baseline key.
    /// Today this strips any leading "[CODE]" prefix so baseline keys don't churn when codes are added/updated.
    /// </summary>
    internal static string NormalizeWarningKey(string? warning)
    {
        if (string.IsNullOrWhiteSpace(warning))
            return string.Empty;

        var trimmed = warning.Trim();
        if (trimmed.Length < 3 || trimmed[0] != '[')
            return trimmed;

        var end = trimmed.IndexOf(']');
        if (end <= 0)
            return trimmed;

        return trimmed.Substring(end + 1).TrimStart();
    }

    internal static string Write(
        string siteRoot,
        string? baselinePath,
        string[] warnings,
        bool mergeWithExisting,
        WebConsoleLogger? logger)
    {
        var resolvedPath = ResolveBaselinePath(siteRoot, baselinePath);
        try
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (mergeWithExisting)
            {
                foreach (var key in LoadWarningKeys(resolvedPath))
                {
                    var normalized = NormalizeWarningKey(key);
                    if (!string.IsNullOrWhiteSpace(normalized))
                        keys.Add(normalized);
                }
            }

            foreach (var warning in warnings ?? Array.Empty<string>())
            {
                var normalized = NormalizeWarningKey(warning);
                if (!string.IsNullOrWhiteSpace(normalized))
                    keys.Add(normalized);
            }

            var payload = new
            {
                version = 1,
                generatedAtUtc = DateTimeOffset.UtcNow,
                warningCount = keys.Count,
                warningKeys = keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray()
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
            logger?.Warn($"Verify baseline write failed: {ex.Message}");
            return resolvedPath;
        }
    }

    internal static string ResolveBaselinePath(string siteRoot, string? baselinePath)
    {
        var candidate = string.IsNullOrWhiteSpace(baselinePath) ? ".powerforge/verify-baseline.json" : baselinePath.Trim();
        var normalizedRoot = NormalizeDirectoryPath(siteRoot);
        var resolvedPath = Path.IsPathRooted(candidate)
            ? Path.GetFullPath(candidate)
            : Path.GetFullPath(Path.Combine(normalizedRoot, candidate));
        if (!IsWithinRoot(normalizedRoot, resolvedPath))
            throw new InvalidOperationException($"Baseline path must resolve under site root: {candidate}");
        return resolvedPath;
    }

    internal static string[] LoadWarningKeysSafe(string siteRoot, string? baselinePath)
    {
        try
        {
            var resolved = ResolveBaselinePath(siteRoot, baselinePath);
            return LoadWarningKeys(resolved).ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    internal static bool TryLoadWarningKeys(string siteRoot, string? baselinePath, out string resolvedPath, out string[] keys)
    {
        resolvedPath = string.Empty;
        keys = Array.Empty<string>();
        try
        {
            resolvedPath = ResolveBaselinePath(siteRoot, baselinePath);
            if (!File.Exists(resolvedPath))
                return false;

            var info = new FileInfo(resolvedPath);
            if (info.Length > MaxBaselineFileSizeBytes)
                return false;

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var stream = File.OpenRead(resolvedPath);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            if (TryGetPropertyIgnoreCase(root, "warningKeys", out var warningKeys) && warningKeys.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in warningKeys.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String) continue;
                    var value = item.GetString();
                    var normalized = NormalizeWarningKey(value);
                    if (!string.IsNullOrWhiteSpace(normalized))
                        set.Add(normalized);
                }
            }

            if (TryGetPropertyIgnoreCase(root, "warnings", out var warnings) && warnings.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in warnings.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String) continue;
                    var value = item.GetString();
                    var normalized = NormalizeWarningKey(value);
                    if (!string.IsNullOrWhiteSpace(normalized))
                        set.Add(normalized);
                }
            }

            keys = set.ToArray();
            return true;
        }
        catch
        {
            resolvedPath = string.Empty;
            keys = Array.Empty<string>();
            return false;
        }
    }

    private static IEnumerable<string> LoadWarningKeys(string path)
    {
        if (!File.Exists(path))
            return Array.Empty<string>();

        var info = new FileInfo(path);
        if (info.Length > MaxBaselineFileSizeBytes)
            return Array.Empty<string>();

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            if (TryGetPropertyIgnoreCase(root, "warningKeys", out var warningKeys) && warningKeys.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in warningKeys.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String) continue;
                    var value = item.GetString();
                    var normalized = NormalizeWarningKey(value);
                    if (!string.IsNullOrWhiteSpace(normalized))
                        keys.Add(normalized);
                }
            }

            if (TryGetPropertyIgnoreCase(root, "warnings", out var warnings) && warnings.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in warnings.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String) continue;
                    var value = item.GetString();
                    var normalized = NormalizeWarningKey(value);
                    if (!string.IsNullOrWhiteSpace(normalized))
                        keys.Add(normalized);
                }
            }
        }
        catch
        {
            return Array.Empty<string>();
        }

        return keys.ToArray();
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
