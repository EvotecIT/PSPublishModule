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
    /// <summary>True when run in dry-run mode.</summary>
    public bool DryRun { get; set; }
    /// <summary>Changed files relative to root path.</summary>
    public string[] ChangedFiles { get; set; } = Array.Empty<string>();
    /// <summary>Warnings collected while running fixer.</summary>
    public string[] Warnings { get; set; } = Array.Empty<string>();
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
        var fileList = ResolveFiles(rootPath, options.Files, options.Include, options.Exclude, warnings);
        var totalReplacements = 0;

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

            var (updated, replacements) = ConvertSimpleHtmlToMarkdown(original);
            if (replacements <= 0 || string.Equals(original, updated, StringComparison.Ordinal))
                continue;

            totalReplacements += replacements;
            changedFiles.Add(ToRelative(rootPath, file));

            if (!options.ApplyChanges)
                continue;

            try
            {
                File.WriteAllText(file, updated);
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
            DryRun = !options.ApplyChanges,
            ChangedFiles = changedFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
            Warnings = warnings.ToArray()
        };
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

    private static (string Updated, int Replacements) ConvertSimpleHtmlToMarkdown(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return (content, 0);

        var lines = content.Split('\n');
        var inFence = false;
        var outsideFence = new System.Text.StringBuilder();
        var insideFence = new System.Text.StringBuilder();
        var rebuilt = new System.Text.StringBuilder(content.Length + 64);
        var replacements = 0;

        void FlushOutside()
        {
            if (outsideFence.Length == 0) return;
            var (updated, count) = ReplaceSimpleTags(outsideFence.ToString());
            replacements += count;
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

        return (updated, replacements);
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
}
