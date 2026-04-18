using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PowerForge.Web;

public static partial class WebLinkService
{
    /// <summary>Promotes reviewed 404 report suggestions into redirect candidates.</summary>
    public static WebLink404PromoteResult Promote404Suggestions(WebLink404PromoteOptions options)
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
        var imported = Build404RedirectCandidates(report, sourcePath, options).ToList();
        var existing = options.MergeWithExisting && File.Exists(outputPath)
            ? ReadExistingRedirects(outputPath)
            : new List<LinkRedirectRule>();

        var existingCount = existing.Count;
        var merged = MergeRedirectCandidates(existing, imported, options.ReplaceExisting, out var skippedDuplicates, out var replaced);
        WriteRedirectJson(outputPath, merged);

        return new WebLink404PromoteResult
        {
            SourcePath = sourcePath,
            OutputPath = outputPath,
            ExistingCount = existingCount,
            CandidateCount = imported.Count,
            WrittenCount = merged.Count,
            SkippedDuplicateCount = skippedDuplicates,
            ReplacedCount = replaced,
            SkippedNoSuggestionCount = report.Suggestions.Count(static item => item.Suggestions.Length == 0),
            SkippedLowCount = report.Suggestions.Count(item => item.Count < Math.Max(1, options.MinimumCount))
        };
    }

    private static IEnumerable<LinkRedirectRule> Build404RedirectCandidates(
        WebLink404ReportResult report,
        string sourcePath,
        WebLink404PromoteOptions options)
    {
        var minimumScore = Math.Clamp(options.MinimumScore, 0d, 1d);
        var minimumCount = Math.Max(1, options.MinimumCount);

        foreach (var suggestion in report.Suggestions ?? Array.Empty<WebLink404Suggestion>())
        {
            if (suggestion.Count < minimumCount)
                continue;

            var target = (suggestion.Suggestions ?? Array.Empty<WebLink404RouteSuggestion>())
                .Where(item => !string.IsNullOrWhiteSpace(item.TargetPath))
                .Where(item => item.Score >= minimumScore)
                .OrderByDescending(static item => item.Score)
                .ThenBy(static item => item.TargetPath, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (target is null)
                continue;

            var source = NormalizeObservationPath(suggestion.Path);
            if (string.IsNullOrWhiteSpace(source))
                continue;

            yield return new LinkRedirectRule
            {
                Id = Build404PromotedId(suggestion.Host, source, target.TargetPath),
                Enabled = options.Enabled,
                SourceHost = NullIfWhiteSpace(suggestion.Host),
                SourcePath = source,
                TargetUrl = NormalizePromoteTarget(target.TargetPath),
                MatchType = LinkRedirectMatchType.Exact,
                Status = options.Status is 301 or 302 or 307 or 308 ? options.Status : 301,
                Group = string.IsNullOrWhiteSpace(options.Group) ? "404-suggestions" : options.Group.Trim(),
                Source = "404-promoted",
                Notes = Build404PromoteNote(suggestion, target, options.Enabled),
                OriginPath = sourcePath
            };
        }
    }

    private static List<LinkRedirectRule> ReadExistingRedirects(string path)
    {
        var redirects = new List<LinkRedirectRule>();
        var usedSources = new List<string>();
        var missingSources = new List<string>();
        LoadRedirectJson(path, redirects, usedSources, missingSources);
        return redirects;
    }

    private static List<LinkRedirectRule> MergeRedirectCandidates(
        List<LinkRedirectRule> existing,
        List<LinkRedirectRule> imported,
        bool replaceExisting,
        out int skippedCount,
        out int replacedCount)
    {
        skippedCount = 0;
        replacedCount = 0;
        var merged = new List<LinkRedirectRule>();
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var redirect in existing.Where(static item => item is not null))
        {
            index[BuildRedirectKey(redirect)] = merged.Count;
            merged.Add(redirect);
        }

        foreach (var redirect in imported.Where(static item => item is not null))
        {
            var key = BuildRedirectKey(redirect);
            if (index.TryGetValue(key, out var existingIndex))
            {
                if (replaceExisting)
                {
                    merged[existingIndex] = redirect;
                    replacedCount++;
                }
                else
                    skippedCount++;
                continue;
            }

            index[key] = merged.Count;
            merged.Add(redirect);
        }

        return merged
            .OrderBy(static item => item.SourceHost ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.SourceQuery ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void WriteRedirectJson(string outputPath, IReadOnlyList<LinkRedirectRule> redirects)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var payload = new { redirects };
        File.WriteAllText(outputPath, JsonSerializer.Serialize(payload, ShortlinkImportJsonOptions), Utf8NoBom);
    }

    private static string Build404PromotedId(string? host, string sourcePath, string targetPath)
    {
        var raw = $"{host}|{sourcePath}|{targetPath}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw.ToLowerInvariant()));
        return "404-" + Convert.ToHexString(hash, 0, 6).ToLowerInvariant();
    }

    private static string NormalizePromoteTarget(string targetPath)
    {
        var target = targetPath.Trim();
        if (!target.StartsWith("/", StringComparison.Ordinal) &&
            !target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            target = "/" + target.TrimStart('/');
        }

        return target;
    }

    private static string Build404PromoteNote(WebLink404Suggestion suggestion, WebLink404RouteSuggestion target, bool enabled)
    {
        var review = enabled ? "enabled during promotion" : "review before enabling";
        var parts = new List<string>
        {
            $"Promoted from 404 report; count={suggestion.Count}; score={target.Score:0.###}; {review}."
        };
        if (!string.IsNullOrWhiteSpace(suggestion.Referrer))
            parts.Add("Referrer: " + suggestion.Referrer.Trim());
        if (!string.IsNullOrWhiteSpace(suggestion.LastSeenAt))
            parts.Add("Last seen: " + suggestion.LastSeenAt.Trim());
        return string.Join(" ", parts);
    }
}

/// <summary>Options for promoting 404 report suggestions into redirect candidates.</summary>
public sealed class WebLink404PromoteOptions
{
    /// <summary>Source 404 suggestion report path.</summary>
    public string SourcePath { get; set; } = string.Empty;
    /// <summary>Output redirects JSON path.</summary>
    public string OutputPath { get; set; } = string.Empty;
    /// <summary>When true, promoted redirects are immediately enabled.</summary>
    public bool Enabled { get; set; }
    /// <summary>Minimum suggestion score to promote.</summary>
    public double MinimumScore { get; set; } = 0.35d;
    /// <summary>Minimum observed 404 count to promote.</summary>
    public int MinimumCount { get; set; } = 1;
    /// <summary>HTTP redirect status for promoted candidates.</summary>
    public int Status { get; set; } = 301;
    /// <summary>Group assigned to promoted candidates.</summary>
    public string? Group { get; set; } = "404-suggestions";
    /// <summary>Merge with existing redirect JSON instead of replacing the file.</summary>
    public bool MergeWithExisting { get; set; } = true;
    /// <summary>Replace existing redirects with the same host/path/query key.</summary>
    public bool ReplaceExisting { get; set; }
}

/// <summary>Result from promoting 404 suggestions into redirect candidates.</summary>
public sealed class WebLink404PromoteResult
{
    /// <summary>Resolved source report path.</summary>
    public string SourcePath { get; set; } = string.Empty;
    /// <summary>Resolved output redirects path.</summary>
    public string OutputPath { get; set; } = string.Empty;
    /// <summary>Existing redirects loaded from the output file.</summary>
    public int ExistingCount { get; set; }
    /// <summary>Redirect candidates selected from the report.</summary>
    public int CandidateCount { get; set; }
    /// <summary>Total redirects written to the output file.</summary>
    public int WrittenCount { get; set; }
    /// <summary>Candidates skipped because an existing redirect had the same key.</summary>
    public int SkippedDuplicateCount { get; set; }
    /// <summary>Existing redirects replaced because replacement was requested.</summary>
    public int ReplacedCount { get; set; }
    /// <summary>Observations skipped because they had no route suggestion.</summary>
    public int SkippedNoSuggestionCount { get; set; }
    /// <summary>Observations skipped because their count was below the configured threshold.</summary>
    public int SkippedLowCount { get; set; }
}
