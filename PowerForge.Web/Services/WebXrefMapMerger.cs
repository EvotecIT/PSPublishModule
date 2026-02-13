using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PowerForge.Web;

/// <summary>Options for merging multiple xref maps into one map.</summary>
public sealed class WebXrefMergeOptions
{
    /// <summary>Output path for merged xref map JSON.</summary>
    public string OutputPath { get; set; } = string.Empty;
    /// <summary>Input xref files or directories. Directories are scanned for JSON files.</summary>
    public List<string> Inputs { get; } = new();
    /// <summary>Directory scan pattern for input directories.</summary>
    public string Pattern { get; set; } = "*.json";
    /// <summary>When true, scans input directories recursively.</summary>
    public bool Recursive { get; set; } = true;
    /// <summary>When true, later entries replace earlier duplicates for name/href.</summary>
    public bool PreferLast { get; set; }
    /// <summary>When true, throws when duplicate UIDs are found.</summary>
    public bool FailOnDuplicateIds { get; set; }
    /// <summary>Maximum allowed merged references. 0 disables the check.</summary>
    public int MaxReferences { get; set; }
    /// <summary>Maximum allowed duplicate UIDs. 0 disables the check.</summary>
    public int MaxDuplicates { get; set; }
    /// <summary>Maximum allowed growth in merged references versus previous output. 0 disables the check.</summary>
    public int MaxReferenceGrowthCount { get; set; }
    /// <summary>Maximum allowed growth percent in merged references versus previous output. 0 disables the check.</summary>
    public double MaxReferenceGrowthPercent { get; set; }
}

/// <summary>Merges xref maps from API docs and site docs.</summary>
public static class WebXrefMapMerger
{
    private sealed class XrefRef
    {
        public string Uid { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Href { get; set; } = string.Empty;
        public HashSet<string> Aliases { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Merges xref map files into a single DocFX-compatible references payload.</summary>
    /// <param name="options">Merge options.</param>
    /// <returns>Merge result payload.</returns>
    public static WebXrefMergeResult Merge(WebXrefMergeOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.OutputPath))
            throw new ArgumentException("OutputPath is required.", nameof(options));
        if (options.Inputs.Count == 0)
            throw new ArgumentException("At least one input path is required.", nameof(options));
        if (options.MaxReferences < 0)
            throw new ArgumentOutOfRangeException(nameof(options.MaxReferences), "MaxReferences cannot be negative.");
        if (options.MaxDuplicates < 0)
            throw new ArgumentOutOfRangeException(nameof(options.MaxDuplicates), "MaxDuplicates cannot be negative.");
        if (options.MaxReferenceGrowthCount < 0)
            throw new ArgumentOutOfRangeException(nameof(options.MaxReferenceGrowthCount), "MaxReferenceGrowthCount cannot be negative.");
        if (double.IsNaN(options.MaxReferenceGrowthPercent) || double.IsInfinity(options.MaxReferenceGrowthPercent) || options.MaxReferenceGrowthPercent < 0)
            throw new ArgumentOutOfRangeException(nameof(options.MaxReferenceGrowthPercent), "MaxReferenceGrowthPercent cannot be negative.");

        var outputPath = Path.GetFullPath(options.OutputPath);
        var warnings = new List<string>();
        var inputFiles = ResolveInputFiles(options, warnings);
        if (inputFiles.Count == 0)
            throw new InvalidOperationException("xref merge found no readable input map files.");

        var merged = new Dictionary<string, XrefRef>(StringComparer.OrdinalIgnoreCase);
        var duplicateCount = 0;

        foreach (var file in inputFiles)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                foreach (var reference in ParseReferences(doc.RootElement, file, warnings))
                {
                    if (string.IsNullOrWhiteSpace(reference.Uid) || string.IsNullOrWhiteSpace(reference.Href))
                        continue;

                    if (!merged.TryGetValue(reference.Uid, out var existing))
                    {
                        merged[reference.Uid] = reference;
                        continue;
                    }

                    duplicateCount++;
                    if (options.PreferLast)
                    {
                        if (!string.IsNullOrWhiteSpace(reference.Name))
                            existing.Name = reference.Name;
                        if (!string.IsNullOrWhiteSpace(reference.Href))
                            existing.Href = reference.Href;
                    }

                    foreach (var alias in reference.Aliases)
                        existing.Aliases.Add(alias);
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"Xref merge: failed to parse '{file}' ({ex.GetType().Name}: {ex.Message})");
            }
        }

        if (options.FailOnDuplicateIds && duplicateCount > 0)
            throw new InvalidOperationException($"xref merge found {duplicateCount} duplicate uid entries.");

        var references = merged.Values
            .OrderBy(static value => value.Uid, StringComparer.OrdinalIgnoreCase)
            .Select(value => new Dictionary<string, object?>
            {
                ["uid"] = value.Uid,
                ["name"] = string.IsNullOrWhiteSpace(value.Name) ? value.Uid : value.Name,
                ["href"] = value.Href,
                ["aliases"] = value.Aliases
                    .Where(alias => !string.IsNullOrWhiteSpace(alias) && !alias.Equals(value.Uid, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(static alias => alias, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            })
            .ToArray();

        var payload = new Dictionary<string, object?>
        {
            ["generatedAtUtc"] = DateTime.UtcNow.ToString("O"),
            ["sourceCount"] = inputFiles.Count,
            ["referenceCount"] = references.Length,
            ["duplicateCount"] = duplicateCount,
            ["references"] = references
        };
        var previousReferenceCount = TryReadPreviousReferenceCount(outputPath, warnings);
        int? referenceDeltaCount = null;
        double? referenceDeltaPercent = null;
        if (previousReferenceCount.HasValue)
        {
            referenceDeltaCount = references.Length - previousReferenceCount.Value;
            if (previousReferenceCount.Value > 0)
                referenceDeltaPercent = (double)referenceDeltaCount.Value * 100d / previousReferenceCount.Value;

            payload["previousReferenceCount"] = previousReferenceCount.Value;
            payload["referenceDeltaCount"] = referenceDeltaCount.Value;
            if (referenceDeltaPercent.HasValue)
                payload["referenceDeltaPercent"] = Math.Round(referenceDeltaPercent.Value, 4);
        }

        if (options.MaxReferences > 0 && references.Length > options.MaxReferences)
        {
            warnings.Add($"Xref merge: reference count {references.Length} exceeds maxReferences {options.MaxReferences}.");
        }
        if (options.MaxDuplicates > 0 && duplicateCount > options.MaxDuplicates)
        {
            warnings.Add($"Xref merge: duplicate count {duplicateCount} exceeds maxDuplicates {options.MaxDuplicates}.");
        }
        if (options.MaxReferenceGrowthCount > 0 &&
            referenceDeltaCount.HasValue &&
            referenceDeltaCount.Value > options.MaxReferenceGrowthCount)
        {
            warnings.Add($"Xref merge: reference growth {referenceDeltaCount.Value} exceeds maxReferenceGrowthCount {options.MaxReferenceGrowthCount}.");
        }
        if (options.MaxReferenceGrowthPercent > 0 &&
            referenceDeltaPercent.HasValue &&
            referenceDeltaPercent.Value > options.MaxReferenceGrowthPercent)
        {
            warnings.Add($"Xref merge: reference growth {referenceDeltaPercent.Value.ToString("0.##", CultureInfo.InvariantCulture)}% exceeds maxReferenceGrowthPercent {options.MaxReferenceGrowthPercent.ToString("0.##", CultureInfo.InvariantCulture)}%.");
        }

        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDir))
            Directory.CreateDirectory(outputDir);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));

        return new WebXrefMergeResult
        {
            OutputPath = outputPath,
            SourceCount = inputFiles.Count,
            ReferenceCount = references.Length,
            DuplicateCount = duplicateCount,
            PreviousReferenceCount = previousReferenceCount,
            ReferenceDeltaCount = referenceDeltaCount,
            ReferenceDeltaPercent = referenceDeltaPercent,
            Warnings = warnings
                .Where(static warning => !string.IsNullOrWhiteSpace(warning))
                .ToArray()
        };
    }

    private static int? TryReadPreviousReferenceCount(string outputPath, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(outputPath) || !File.Exists(outputPath))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(outputPath));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var property in root.EnumerateObject())
            {
                if (!property.Name.Equals("referenceCount", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var count))
                    return count;
                if (property.Value.ValueKind == JsonValueKind.String && int.TryParse(property.Value.GetString(), out count))
                    return count;
            }

            if (TryGetArray(root, "references", out var references))
                return references.GetArrayLength();
        }
        catch (Exception ex)
        {
            warnings.Add($"Xref merge: failed to read previous output '{outputPath}' ({ex.GetType().Name}: {ex.Message})");
        }

        return null;
    }

    private static List<string> ResolveInputFiles(WebXrefMergeOptions options, List<string> warnings)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var searchOption = options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var pattern = string.IsNullOrWhiteSpace(options.Pattern) ? "*.json" : options.Pattern;

        foreach (var input in options.Inputs.Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            var fullPath = Path.GetFullPath(input);
            if (File.Exists(fullPath))
            {
                files.Add(fullPath);
                continue;
            }

            if (Directory.Exists(fullPath))
            {
                try
                {
                    foreach (var file in Directory.EnumerateFiles(fullPath, pattern, searchOption))
                        files.Add(Path.GetFullPath(file));
                }
                catch (Exception ex)
                {
                    warnings.Add($"Xref merge: failed to enumerate '{fullPath}' ({ex.GetType().Name}: {ex.Message})");
                }
                continue;
            }

            warnings.Add($"Xref merge: input path was not found: {fullPath}");
        }

        return files
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<XrefRef> ParseReferences(JsonElement root, string sourcePath, List<string> warnings)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (TryGetArray(root, "references", out var references) ||
                TryGetArray(root, "refs", out references) ||
                TryGetArray(root, "entries", out references))
            {
                foreach (var item in references.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                        continue;

                    var parsed = ParseReferenceObject(item, fallbackUid: null);
                    if (parsed is not null)
                        yield return parsed;
                }

                yield break;
            }

            foreach (var property in root.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    var uid = NormalizeId(property.Name);
                    var href = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(uid) && !string.IsNullOrWhiteSpace(href))
                    {
                        yield return new XrefRef
                        {
                            Uid = uid,
                            Name = uid,
                            Href = href!.Trim()
                        };
                    }
                    continue;
                }

                if (property.Value.ValueKind != JsonValueKind.Object)
                    continue;

                var fallbackUid = NormalizeId(property.Name);
                var parsed = ParseReferenceObject(property.Value, fallbackUid);
                if (parsed is not null)
                    yield return parsed;
            }

            yield break;
        }

        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var parsed = ParseReferenceObject(item, fallbackUid: null);
                if (parsed is not null)
                    yield return parsed;
            }
            yield break;
        }

        warnings.Add($"Xref merge: unsupported JSON root in '{sourcePath}'.");
    }

    private static XrefRef? ParseReferenceObject(JsonElement item, string? fallbackUid)
    {
        var uid = NormalizeId(ReadString(item, "uid") ??
                              ReadString(item, "id") ??
                              ReadString(item, "xref") ??
                              fallbackUid);
        if (string.IsNullOrWhiteSpace(uid))
            return null;

        var href = (ReadString(item, "href") ??
                    ReadString(item, "url"))?.Trim();
        if (string.IsNullOrWhiteSpace(href))
            return null;

        var name = (ReadString(item, "name") ??
                    ReadString(item, "title") ??
                    uid).Trim();

        var reference = new XrefRef
        {
            Uid = uid,
            Name = string.IsNullOrWhiteSpace(name) ? uid : name,
            Href = href
        };

        foreach (var alias in ReadStringValues(item, "aliases"))
        {
            var normalized = NormalizeId(alias);
            if (!string.IsNullOrWhiteSpace(normalized) &&
                !normalized.Equals(uid, StringComparison.OrdinalIgnoreCase))
            {
                reference.Aliases.Add(normalized);
            }
        }

        return reference;
    }

    private static bool TryGetArray(JsonElement element, string name, out JsonElement array)
    {
        array = default;
        if (element.ValueKind != JsonValueKind.Object)
            return false;
        foreach (var property in element.EnumerateObject())
        {
            if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;
            if (property.Value.ValueKind != JsonValueKind.Array)
                return false;
            array = property.Value;
            return true;
        }
        return false;
    }

    private static string NormalizeId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        if (trimmed.StartsWith("xref:", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed.Substring("xref:".Length).Trim();

        return trimmed;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var property in element.EnumerateObject())
        {
            if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;
            return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.ToString();
        }
        return null;
    }

    private static IEnumerable<string> ReadStringValues(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return Array.Empty<string>();
        JsonElement value = default;
        var found = false;
        foreach (var property in element.EnumerateObject())
        {
            if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;
            value = property.Value;
            found = true;
            break;
        }
        if (!found)
            return Array.Empty<string>();

        if (value.ValueKind == JsonValueKind.Array)
        {
            return value.EnumerateArray()
                .Select(static item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Select(static item => item!.Trim())
                .ToArray();
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (string.IsNullOrWhiteSpace(text))
                return Array.Empty<string>();

            return text
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();
        }

        return Array.Empty<string>();
    }
}
