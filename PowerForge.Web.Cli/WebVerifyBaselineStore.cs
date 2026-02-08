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
                    keys.Add(key);
            }

            foreach (var warning in warnings ?? Array.Empty<string>())
            {
                var normalized = warning?.Trim();
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
                    if (!string.IsNullOrWhiteSpace(value))
                        keys.Add(value.Trim());
                }
            }

            if (TryGetPropertyIgnoreCase(root, "warnings", out var warnings) && warnings.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in warnings.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String) continue;
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        keys.Add(value.Trim());
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
