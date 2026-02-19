using System.Text.RegularExpressions;

namespace PowerForge.Web;

/// <summary>Options for markdown hygiene fixer.</summary>
public sealed class WebMarkdownFixOptions
{
    /// <summary>Root directory used for relative path reporting and file discovery.</summary>
    public string RootPath { get; set; } = ".";
    /// <summary>Optional explicit list of markdown files to process.</summary>
    public string[] Files { get; set; } = Array.Empty<string>();
    /// <summary>Optional include glob patterns relative to root path.</summary>
    public string[] Include { get; set; } = Array.Empty<string>();
    /// <summary>Optional exclude glob patterns relative to root path.</summary>
    public string[] Exclude { get; set; } = Array.Empty<string>();
    /// <summary>When true, writes fixes to disk; otherwise dry-run only.</summary>
    public bool ApplyChanges { get; set; }
}

/// <summary>Result payload for markdown hygiene fixer.</summary>
public sealed class WebMarkdownFixResult
{
    /// <summary>True when fixer completed without fatal errors.</summary>
    public bool Success { get; set; }
    /// <summary>Total files scanned.</summary>
    public int FileCount { get; set; }
    /// <summary>Total files with at least one change.</summary>
    public int ChangedFileCount { get; set; }
    /// <summary>Total replacement operations across all files.</summary>
    public int ReplacementCount { get; set; }
    /// <summary>Total multiline media tag normalization operations.</summary>
    public int MediaTagReplacementCount { get; set; }
    /// <summary>Total simple HTML-to-markdown replacement operations.</summary>
    public int SimpleHtmlReplacementCount { get; set; }
    /// <summary>True when run in dry-run mode.</summary>
    public bool DryRun { get; set; }
    /// <summary>Changed files relative to root path.</summary>
    public string[] ChangedFiles { get; set; } = Array.Empty<string>();
    /// <summary>Per-file change breakdown.</summary>
    public WebMarkdownFixFileChange[] FileChanges { get; set; } = Array.Empty<WebMarkdownFixFileChange>();
    /// <summary>Aggregated media-tag replacement counts by tag name.</summary>
    public WebMarkdownFixTagStat[] MediaTagStats { get; set; } = Array.Empty<WebMarkdownFixTagStat>();
    /// <summary>Warnings collected while running fixer.</summary>
    public string[] Warnings { get; set; } = Array.Empty<string>();
}

/// <summary>Per-file markdown fixer change breakdown.</summary>
public sealed class WebMarkdownFixFileChange
{
    /// <summary>Changed file path relative to fixer root.</summary>
    public string Path { get; set; } = string.Empty;
    /// <summary>Total replacements in this file.</summary>
    public int Replacements { get; set; }
    /// <summary>Multiline media tag normalization replacements in this file.</summary>
    public int MediaTagReplacements { get; set; }
    /// <summary>Simple HTML-to-markdown replacements in this file.</summary>
    public int HtmlTagReplacements { get; set; }
    /// <summary>Media-tag breakdown for this file.</summary>
    public WebMarkdownFixTagStat[] MediaTagStats { get; set; } = Array.Empty<WebMarkdownFixTagStat>();
}

/// <summary>Aggregated markdown fixer count for a specific tag.</summary>
public sealed class WebMarkdownFixTagStat
{
    /// <summary>Tag name.</summary>
    public string Tag { get; set; } = string.Empty;
    /// <summary>Replacement count for the tag.</summary>
    public int Count { get; set; }
}

/// <summary>Converts simple raw HTML tags in markdown to markdown equivalents.</summary>
public static class WebMarkdownHygieneFixer
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    private static readonly Regex FenceRegex = new("^```", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex HeadingRegex = new("<h([1-6])>(.*?)</h\\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex StrongRegex = new("<strong>(.*?)</strong>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex BoldRegex = new("<b>(.*?)</b>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex EmRegex = new("<em>(.*?)</em>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex ItalicRegex = new("<i>(.*?)</i>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex BreakRegex = new("<br\\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex ParagraphRegex = new("<p>(.*?)</p>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);

    /// <summary>Runs markdown fixer.</summary>
    /// <param name="options">Fixer options.</param>
    /// <returns>Fixer result.</returns>
    public static WebMarkdownFixResult Fix(WebMarkdownFixOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.RootPath))
            throw new ArgumentException("RootPath is required.", nameof(options));

        var rootPath = NormalizeRootPath(options.RootPath);
        if (!Directory.Exists(rootPath))
            throw new DirectoryNotFoundException($"Root path not found: {rootPath}");

        var warnings = new List<string>();
        var changedFiles = new List<string>();
        var fileChanges = new List<WebMarkdownFixFileChange>();
        var mediaTagTotals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var fileList = ResolveFiles(rootPath, options.Files, options.Include, options.Exclude, warnings);
        var totalReplacements = 0;
        var totalMediaTagReplacements = 0;
        var totalSimpleTagReplacements = 0;

        foreach (var file in fileList)
        {
            string original;
            try
            {
                original = File.ReadAllText(file);
            }
            catch (Exception ex)
            {
                warnings.Add($"{ToRelative(rootPath, file)}: read failed ({ex.Message}).");
                continue;
            }

            if (string.IsNullOrWhiteSpace(original))
                continue;

            var pass = ConvertSimpleHtmlToMarkdown(original);
            if (pass.Replacements <= 0 || string.Equals(original, pass.Updated, StringComparison.Ordinal))
                continue;

            totalReplacements += pass.Replacements;
            totalMediaTagReplacements += pass.MediaTagReplacements;
            totalSimpleTagReplacements += pass.HtmlTagReplacements;
            var relativePath = ToRelative(rootPath, file);
            changedFiles.Add(relativePath);
            fileChanges.Add(new WebMarkdownFixFileChange
            {
                Path = relativePath,
                Replacements = pass.Replacements,
                MediaTagReplacements = pass.MediaTagReplacements,
                HtmlTagReplacements = pass.HtmlTagReplacements,
                MediaTagStats = pass.MediaTagStats
            });
            AddTagStats(mediaTagTotals, pass.MediaTagStats);

            if (!options.ApplyChanges)
                continue;

            try
            {
                File.WriteAllText(file, pass.Updated);
            }
            catch (Exception ex)
            {
                warnings.Add($"{ToRelative(rootPath, file)}: write failed ({ex.Message}).");
            }
        }

        return new WebMarkdownFixResult
        {
            Success = true,
            FileCount = fileList.Count,
            ChangedFileCount = changedFiles.Count,
            ReplacementCount = totalReplacements,
            MediaTagReplacementCount = totalMediaTagReplacements,
            SimpleHtmlReplacementCount = totalSimpleTagReplacements,
            DryRun = !options.ApplyChanges,
            ChangedFiles = changedFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
            FileChanges = fileChanges.OrderBy(change => change.Path, StringComparer.OrdinalIgnoreCase).ToArray(),
            MediaTagStats = ToTagStats(mediaTagTotals),
            Warnings = warnings.ToArray()
        };
    }

    /// <summary>Builds a human-friendly markdown summary for fixer results.</summary>
    public static string BuildSummary(WebMarkdownFixResult? result, int maxFiles = 50)
    {
        if (result is null)
            return "# Markdown Fix Summary" + Environment.NewLine + Environment.NewLine + "- No result payload." + Environment.NewLine;

        var safeMaxFiles = maxFiles <= 0 ? 50 : Math.Min(maxFiles, 500);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Markdown Fix Summary");
        sb.AppendLine();
        sb.AppendLine($"- Result: {(result.Success ? "pass" : "fail")}");
        sb.AppendLine($"- Mode: {(result.DryRun ? "dry-run" : "apply")}");
        sb.AppendLine($"- Files scanned: {result.FileCount}");
        sb.AppendLine($"- Files changed: {result.ChangedFileCount}");
        sb.AppendLine($"- Replacements: {result.ReplacementCount}");
        sb.AppendLine($"- Media-tag replacements: {result.MediaTagReplacementCount}");
        sb.AppendLine($"- Simple HTML replacements: {result.SimpleHtmlReplacementCount}");
        sb.AppendLine();

        if (result.MediaTagStats is { Length: > 0 })
        {
            sb.AppendLine("## Media Tags");
            foreach (var stat in result.MediaTagStats.OrderByDescending(static x => x.Count).ThenBy(static x => x.Tag, StringComparer.OrdinalIgnoreCase))
                sb.AppendLine($"- `{stat.Tag}`: {stat.Count}");
            sb.AppendLine();
        }

        if (result.FileChanges is { Length: > 0 })
        {
            sb.AppendLine("## Changed Files");
            sb.AppendLine("| File | Replacements | Media | HTML |");
            sb.AppendLine("| --- | ---: | ---: | ---: |");
            foreach (var change in result.FileChanges
                         .OrderByDescending(static x => x.Replacements)
                         .ThenBy(static x => x.Path, StringComparer.OrdinalIgnoreCase)
                         .Take(safeMaxFiles))
            {
                var path = string.IsNullOrWhiteSpace(change.Path) ? "(unknown)" : change.Path.Replace("|", "/");
                sb.AppendLine($"| {path} | {change.Replacements} | {change.MediaTagReplacements} | {change.HtmlTagReplacements} |");
            }

            var remaining = result.FileChanges.Length - Math.Min(result.FileChanges.Length, safeMaxFiles);
            if (remaining > 0)
                sb.AppendLine($"| ... | +{remaining} more file(s) |  |  |");
            sb.AppendLine();
        }

        if (result.Warnings is { Length: > 0 })
        {
            sb.AppendLine("## Warnings");
            foreach (var warning in result.Warnings)
                sb.AppendLine($"- {warning}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static List<string> ResolveFiles(
        string rootPath,
        string[] explicitFiles,
        string[] includePatterns,
        string[] excludePatterns,
        List<string> warnings)
    {
        if (explicitFiles.Length > 0)
        {
            var list = new List<string>();
            foreach (var file in explicitFiles)
            {
                if (string.IsNullOrWhiteSpace(file))
                    continue;
                var resolved = Path.IsPathRooted(file) ? file : Path.Combine(rootPath, file);
                var full = Path.GetFullPath(resolved);
                if (!IsPathWithinRoot(rootPath, full))
                {
                    warnings.Add($"Skipping file outside root: {file}");
                    continue;
                }

                if (!File.Exists(full))
                {
                    warnings.Add($"File not found: {file}");
                    continue;
                }

                if (!full.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                    continue;
                list.Add(full);
            }
            return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        var includes = NormalizePatterns(includePatterns);
        var excludes = NormalizePatterns(excludePatterns);
        var allFiles = Directory.EnumerateFiles(rootPath, "*.md", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (includes.Length == 0 && excludes.Length == 0)
            return allFiles;

        var results = new List<string>();
        foreach (var file in allFiles)
        {
            var relative = ToRelative(rootPath, file);
            if (excludes.Length > 0 && MatchesAny(excludes, relative))
                continue;
            if (includes.Length > 0 && !MatchesAny(includes, relative))
                continue;
            results.Add(file);
        }
        return results;
    }

    private static MarkdownFixPassResult ConvertSimpleHtmlToMarkdown(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return new MarkdownFixPassResult(content, 0, 0, Array.Empty<WebMarkdownFixTagStat>());

        content = MarkdownMediaTagNormalizer.NormalizeMultilineMediaTagsOutsideFences(content, out MarkdownMediaNormalizationStats mediaStats);
        var mediaTagStats = ToTagStats(mediaStats.TagCounts);

        var lines = content.Split('\n');
        var inFence = false;
        var outsideFence = new System.Text.StringBuilder();
        var insideFence = new System.Text.StringBuilder();
        var rebuilt = new System.Text.StringBuilder(content.Length + 64);
        var simpleHtmlReplacements = 0;

        void FlushOutside()
        {
            if (outsideFence.Length == 0) return;
            var (updated, count) = ReplaceSimpleTags(outsideFence.ToString());
            simpleHtmlReplacements += count;
            rebuilt.Append(updated);
            outsideFence.Clear();
        }

        void FlushInside()
        {
            if (insideFence.Length == 0) return;
            rebuilt.Append(insideFence);
            insideFence.Clear();
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine;
            var target = inFence ? insideFence : outsideFence;
            target.Append(line);
            target.Append('\n');

            if (!FenceRegex.IsMatch(line.TrimStart()))
                continue;

            if (inFence)
            {
                FlushInside();
                inFence = false;
            }
            else
            {
                FlushOutside();
                inFence = true;
            }
        }

        if (inFence)
            FlushInside();
        else
            FlushOutside();

        var updated = rebuilt.ToString();
        if (!content.EndsWith('\n') && updated.EndsWith('\n'))
            updated = updated.Substring(0, updated.Length - 1);

        return new MarkdownFixPassResult(
            updated,
            mediaStats.ReplacementCount,
            simpleHtmlReplacements,
            mediaTagStats);
    }

    private static (string Updated, int Replacements) ReplaceSimpleTags(string input)
    {
        var replacements = 0;

        string Apply(Regex regex, string value, MatchEvaluator evaluator)
        {
            return regex.Replace(value, match =>
            {
                var replaced = evaluator(match);
                if (!string.Equals(replaced, match.Value, StringComparison.Ordinal))
                    replacements++;
                return replaced;
            });
        }

        var updated = input;
        updated = Apply(HeadingRegex, updated, match =>
        {
            var levelText = match.Groups[1].Value;
            var body = CleanInline(match.Groups[2].Value);
            if (!int.TryParse(levelText, out var level))
                return match.Value;
            if (level < 1 || level > 6)
                return match.Value;
            return $"{new string('#', level)} {body}{Environment.NewLine}{Environment.NewLine}";
        });
        updated = Apply(StrongRegex, updated, match => $"**{CleanInline(match.Groups[1].Value)}**");
        updated = Apply(BoldRegex, updated, match => $"**{CleanInline(match.Groups[1].Value)}**");
        updated = Apply(EmRegex, updated, match => $"*{CleanInline(match.Groups[1].Value)}*");
        updated = Apply(ItalicRegex, updated, match => $"*{CleanInline(match.Groups[1].Value)}*");
        updated = Apply(ParagraphRegex, updated, match => $"{CleanInline(match.Groups[1].Value)}{Environment.NewLine}");
        updated = Apply(BreakRegex, updated, _ => "  " + Environment.NewLine);

        return (updated, replacements);
    }

    private static string CleanInline(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        return value
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    private static string[] NormalizePatterns(string[] patterns)
    {
        return patterns
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Select(pattern => pattern.Replace('\\', '/').Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool MatchesAny(string[] patterns, string value)
    {
        foreach (var pattern in patterns)
        {
            if (GlobMatch(pattern, value))
                return true;
        }
        return false;
    }

    private static bool GlobMatch(string pattern, string value)
    {
        var regex = "^" + Regex.Escape(pattern)
            .Replace("\\*\\*", ".*")
            .Replace("\\*", "[^/]*")
            .Replace("\\?", ".") + "$";
        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
    }

    private static string ToRelative(string root, string path)
    {
        return Path.GetRelativePath(root, path).Replace('\\', '/');
    }

    private static string NormalizeRootPath(string rootPath)
    {
        var full = Path.GetFullPath(rootPath);
        var trimmed = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed + Path.DirectorySeparatorChar;
    }

    private static bool IsPathWithinRoot(string rootPath, string path)
    {
        var full = Path.GetFullPath(path);
        return full.StartsWith(rootPath, PathComparison);
    }

    private static void AddTagStats(Dictionary<string, int> target, IEnumerable<WebMarkdownFixTagStat> stats)
    {
        if (target is null || stats is null)
            return;

        foreach (var stat in stats)
        {
            if (stat is null || string.IsNullOrWhiteSpace(stat.Tag) || stat.Count <= 0)
                continue;
            target.TryGetValue(stat.Tag, out var current);
            target[stat.Tag] = current + stat.Count;
        }
    }

    private static WebMarkdownFixTagStat[] ToTagStats(IReadOnlyDictionary<string, int>? source)
    {
        if (source is null || source.Count == 0)
            return Array.Empty<WebMarkdownFixTagStat>();

        return source
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value > 0)
            .Select(pair => new WebMarkdownFixTagStat
            {
                Tag = pair.Key,
                Count = pair.Value
            })
            .OrderByDescending(static stat => stat.Count)
            .ThenBy(static stat => stat.Tag, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private readonly struct MarkdownFixPassResult
    {
        public MarkdownFixPassResult(string updated, int mediaTagReplacements, int htmlTagReplacements, WebMarkdownFixTagStat[] mediaTagStats)
        {
            Updated = updated;
            MediaTagReplacements = mediaTagReplacements;
            HtmlTagReplacements = htmlTagReplacements;
            MediaTagStats = mediaTagStats ?? Array.Empty<WebMarkdownFixTagStat>();
        }

        public string Updated { get; }
        public int MediaTagReplacements { get; }
        public int HtmlTagReplacements { get; }
        public int Replacements => MediaTagReplacements + HtmlTagReplacements;
        public WebMarkdownFixTagStat[] MediaTagStats { get; }
    }
}
