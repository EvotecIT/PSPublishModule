using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PowerForge.Web;

public static partial class WebLinkService
{
    /// <summary>Adds reviewed 404 observations to an ignored-404 rules file.</summary>
    public static WebLink404IgnoreResult Ignore404Suggestions(WebLink404IgnoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.SourcePath))
            throw new ArgumentException("SourcePath is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.OutputPath))
            throw new ArgumentException("OutputPath is required.", nameof(options));

        var sourcePath = Path.GetFullPath(options.SourcePath);
        var outputPath = Path.GetFullPath(options.OutputPath);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"404 suggestion report not found: {sourcePath}", sourcePath);

        var report = JsonSerializer.Deserialize<WebLink404ReportResult>(File.ReadAllText(sourcePath), WebJson.Options)
                     ?? new WebLink404ReportResult();
        var imported = BuildIgnored404Candidates(report, options).ToList();
        var existing = options.MergeWithExisting && File.Exists(outputPath)
            ? LoadIgnored404Rules(outputPath).ToList()
            : new List<Ignored404Rule>();

        var existingCount = existing.Count;
        var merged = MergeIgnored404Rules(existing, imported, options.ReplaceExisting, out var skippedDuplicates, out var replaced);
        WriteIgnored404Json(outputPath, merged);

        return new WebLink404IgnoreResult
        {
            SourcePath = sourcePath,
            OutputPath = outputPath,
            ExistingCount = existingCount,
            CandidateCount = imported.Count,
            WrittenCount = merged.Count,
            SkippedDuplicateCount = skippedDuplicates,
            ReplacedCount = replaced
        };
    }

    private static IEnumerable<Ignored404Rule> BuildIgnored404Candidates(WebLink404ReportResult report, WebLink404IgnoreOptions options)
    {
        var requestedPaths = (options.Paths ?? Array.Empty<string>())
            .Select(NormalizeObservationPath)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var suggestion in report.Suggestions ?? Array.Empty<WebLink404Suggestion>())
        {
            var path = NormalizeObservationPath(suggestion.Path);
            if (string.IsNullOrWhiteSpace(path))
                continue;

            var suggestions = suggestion.Suggestions ?? Array.Empty<WebLink404RouteSuggestion>();
            var selected = options.IncludeAll ||
                           (options.OnlyWithoutSuggestions && suggestions.Length == 0) ||
                           requestedPaths.Contains(path, StringComparer.OrdinalIgnoreCase);
            if (!selected)
                continue;

            yield return new Ignored404Rule
            {
                Path = path,
                Host = NullIfWhiteSpace(suggestion.Host),
                Reason = string.IsNullOrWhiteSpace(options.Reason)
                    ? BuildDefaultIgnored404Reason(suggestion)
                    : options.Reason.Trim(),
                CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
                CreatedBy = NullIfWhiteSpace(options.CreatedBy)
            };
        }
    }

    private static List<Ignored404Rule> MergeIgnored404Rules(
        List<Ignored404Rule> existing,
        List<Ignored404Rule> imported,
        bool replaceExisting,
        out int skippedCount,
        out int replacedCount)
    {
        skippedCount = 0;
        replacedCount = 0;
        var merged = new List<Ignored404Rule>();
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in existing.Where(static item => item is not null))
        {
            index[BuildIgnored404Key(rule)] = merged.Count;
            merged.Add(rule);
        }

        foreach (var rule in imported.Where(static item => item is not null))
        {
            var key = BuildIgnored404Key(rule);
            if (index.TryGetValue(key, out var existingIndex))
            {
                if (replaceExisting)
                {
                    merged[existingIndex] = rule;
                    replacedCount++;
                }
                else
                    skippedCount++;
                continue;
            }

            index[key] = merged.Count;
            merged.Add(rule);
        }

        return merged
            .OrderBy(static item => item.Host ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void WriteIgnored404Json(string outputPath, IReadOnlyList<Ignored404Rule> ignored404)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var payload = new { ignored404 };
        File.WriteAllText(outputPath, JsonSerializer.Serialize(payload, ShortlinkImportJsonOptions), Utf8NoBom);
    }

    private static string BuildIgnored404Key(Ignored404Rule rule)
        => string.Join("|", rule.Host ?? string.Empty, NormalizeObservationPath(rule.Path));

    private static string BuildDefaultIgnored404Reason(WebLink404Suggestion suggestion)
        => (suggestion.Suggestions ?? Array.Empty<WebLink404RouteSuggestion>()).Length == 0
            ? "No generated route suggestion in 404 report."
            : "Reviewed noisy 404 observation.";
}

/// <summary>Options for adding 404 report observations to ignored-404 rules.</summary>
public sealed class WebLink404IgnoreOptions
{
    /// <summary>Source 404 suggestion report path.</summary>
    public string SourcePath { get; set; } = string.Empty;
    /// <summary>Output ignored-404 JSON path.</summary>
    public string OutputPath { get; set; } = string.Empty;
    /// <summary>Specific missing paths to ignore.</summary>
    public string[] Paths { get; set; } = Array.Empty<string>();
    /// <summary>When true, ignore every observation in the report.</summary>
    public bool IncludeAll { get; set; }
    /// <summary>When true, ignore only observations without suggestions.</summary>
    public bool OnlyWithoutSuggestions { get; set; }
    /// <summary>Reason stored on each ignored 404 rule.</summary>
    public string? Reason { get; set; }
    /// <summary>Creator identifier for review workflows.</summary>
    public string? CreatedBy { get; set; }
    /// <summary>Merge with existing ignored-404 JSON instead of replacing the file.</summary>
    public bool MergeWithExisting { get; set; } = true;
    /// <summary>Replace existing ignored rules with the same host/path key.</summary>
    public bool ReplaceExisting { get; set; }
}

/// <summary>Result from adding 404 observations to ignored-404 rules.</summary>
public sealed class WebLink404IgnoreResult
{
    /// <summary>Resolved source report path.</summary>
    public string SourcePath { get; set; } = string.Empty;
    /// <summary>Resolved output ignored-404 path.</summary>
    public string OutputPath { get; set; } = string.Empty;
    /// <summary>Existing ignored rules loaded from the output file.</summary>
    public int ExistingCount { get; set; }
    /// <summary>Ignored rules selected from the report.</summary>
    public int CandidateCount { get; set; }
    /// <summary>Total ignored rules written to the output file.</summary>
    public int WrittenCount { get; set; }
    /// <summary>Candidates skipped because an existing ignored rule had the same key.</summary>
    public int SkippedDuplicateCount { get; set; }
    /// <summary>Existing ignored rules replaced because replacement was requested.</summary>
    public int ReplacedCount { get; set; }
}
