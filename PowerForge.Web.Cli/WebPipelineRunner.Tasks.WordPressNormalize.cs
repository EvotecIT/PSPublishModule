using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static readonly Regex WordPressGeneratedMarkerRegex = new(
        @"meta\.generated_by:\s*import-wordpress-snapshot",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FrontMatterSplitRegex = new(
        @"^(?<fm>---\r?\n[\s\S]*?\r?\n---\r?\n?)(?<body>[\s\S]*)$",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static void ExecuteWordPressNormalize(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root")) ?? baseDir;
        siteRoot = Path.GetFullPath(siteRoot);

        var strictHtmlCleanup = GetBool(step, "strictHtmlCleanup") ?? GetBool(step, "strict-html-cleanup") ?? false;
        var whatIf = GetBool(step, "whatIf") ?? GetBool(step, "what-if") ?? false;
        var translationKeyMapPath = ResolvePath(baseDir,
            GetString(step, "translationKeyMapPath")
            ?? GetString(step, "translation-key-map-path")
            ?? GetString(step, "translationMapPath")
            ?? GetString(step, "translation-map-path")
            ?? GetString(step, "translationOverridesPath")
            ?? GetString(step, "translation-overrides-path"));
        var translationKeyMap = LoadWordPressTranslationKeyMap(translationKeyMapPath);
        var summaryPath = ResolvePath(baseDir, GetString(step, "summaryPath") ?? GetString(step, "summary-path"))
                          ?? Path.Combine(siteRoot, "Build", "normalize-wordpress-last-run.json");

        var targetRoots = (GetArrayOfStrings(step, "targets") ?? GetArrayOfStrings(step, "paths") ?? Array.Empty<string>())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(value => ResolvePath(baseDir, value))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => Path.GetFullPath(value!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (targetRoots.Count == 0)
        {
            targetRoots.Add(Path.Combine(siteRoot, "content", "blog"));
            targetRoots.Add(Path.Combine(siteRoot, "content", "pages"));
        }

        var processed = 0;
        var changed = 0;
        var changedFiles = new List<string>();

        foreach (var target in targetRoots)
        {
            if (!Directory.Exists(target))
                continue;

            foreach (var filePath in Directory.EnumerateFiles(target, "*.md", SearchOption.AllDirectories))
            {
                if (!IsImportedMarkdownFile(filePath))
                    continue;

                processed++;
                var original = SafeReadAllText(filePath);
                if (string.IsNullOrWhiteSpace(original))
                    continue;

                var parts = SplitFrontMatter(original);
                var frontMatter = parts.FrontMatter;
                var body = parts.Body;
                var currentPath = filePath;

                var slug = GetFrontMatterValue(frontMatter, "slug");
                var translationKey = GetFrontMatterValue(frontMatter, "translation_key");
                var wpId = GetFrontMatterValue(frontMatter, "meta.wp_id");
                var languageCode = GetFrontMatterValue(frontMatter, "language");
                var translationPrefix = NormalizeWordPressTranslationPrefix(translationKey, DetectWordPressTranslationPrefixFromPath(currentPath));
                var normalizedTranslationKey = ResolveWordPressTranslationKeyOverride(
                    translationKeyMap,
                    languageCode,
                    string.IsNullOrWhiteSpace(translationKey) && !string.IsNullOrWhiteSpace(wpId) && !string.IsNullOrWhiteSpace(translationPrefix)
                        ? translationPrefix + wpId
                        : (translationKey ?? string.Empty),
                    wpId,
                    translationPrefix);

                if (!string.IsNullOrWhiteSpace(slug))
                {
                    var expectedName = slug.Trim() + ".md";
                    var currentName = Path.GetFileName(currentPath);
                    if (!string.Equals(currentName, expectedName, StringComparison.OrdinalIgnoreCase))
                    {
                        var canonicalPath = Path.Combine(Path.GetDirectoryName(currentPath) ?? string.Empty, expectedName);
                        if (!File.Exists(canonicalPath))
                        {
                            changed++;
                            changedFiles.Add(currentPath);
                            if (!whatIf)
                                File.Move(currentPath, canonicalPath, overwrite: true);
                            currentPath = canonicalPath;
                        }
                        else
                        {
                            var canonicalRaw = SafeReadAllText(canonicalPath);
                            var canonicalParts = SplitFrontMatter(canonicalRaw);
                            var canonicalTranslationKey = GetFrontMatterValue(canonicalParts.FrontMatter, "translation_key");
                            var canonicalWpId = GetFrontMatterValue(canonicalParts.FrontMatter, "meta.wp_id");
                            var sameIdentity =
                                (!string.IsNullOrWhiteSpace(translationKey) &&
                                 string.Equals(translationKey, canonicalTranslationKey, StringComparison.OrdinalIgnoreCase)) ||
                                (!string.IsNullOrWhiteSpace(wpId) &&
                                 string.Equals(wpId, canonicalWpId, StringComparison.OrdinalIgnoreCase));
                            if (!sameIdentity)
                                continue;
                        }
                    }
                }

                var normalizedBody = NormalizeImportedBody(body, strictHtmlCleanup);
                var normalizedFrontMatter = NormalizeImportedFrontMatter(frontMatter, normalizedBody, normalizedTranslationKey);
                var updated = normalizedFrontMatter + normalizedBody;

                if (!string.Equals(updated, original, StringComparison.Ordinal))
                {
                    changed++;
                    changedFiles.Add(currentPath);
                    if (!whatIf)
                        File.WriteAllText(currentPath, updated, new UTF8Encoding(false));
                }
            }
        }

        var summary = new
        {
            generatedOn = DateTimeOffset.UtcNow.ToString("O"),
            siteRoot,
            strictHtmlCleanup,
            whatIf,
            processedFiles = processed,
            changedFiles = changed,
            changedFilePaths = changedFiles.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static p => p, StringComparer.OrdinalIgnoreCase).ToArray()
        };

        if (!whatIf)
        {
            var directory = Path.GetDirectoryName(summaryPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(summaryPath, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
        }

        stepResult.Success = true;
        stepResult.Message = $"wordpress-normalize ok: processed={processed}; changed={changed}";
    }

    private static bool IsImportedMarkdownFile(string path)
    {
        if (!File.Exists(path))
            return false;

        try
        {
            var head = File.ReadLines(path).Take(80);
            return WordPressGeneratedMarkerRegex.IsMatch(string.Join('\n', head));
        }
        catch
        {
            return false;
        }
    }

    private static (string FrontMatter, string Body) SplitFrontMatter(string content)
    {
        var match = FrontMatterSplitRegex.Match(content ?? string.Empty);
        if (!match.Success)
            return (string.Empty, content ?? string.Empty);
        return (match.Groups["fm"].Value, match.Groups["body"].Value);
    }

    private static string? GetFrontMatterValue(string? frontMatter, string name)
    {
        if (string.IsNullOrWhiteSpace(frontMatter))
            return null;

        var pattern = @"(?im)^" + Regex.Escape(name) + @"\s*:\s*(?:""(?<value>[^""]*)""|(?<value>[^\r\n]+))\s*$";
        var match = Regex.Match(frontMatter, pattern);
        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    private static string? DetectWordPressTranslationPrefixFromPath(string path)
    {
        var normalizedPath = path?.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalizedPath))
            return null;
        if (normalizedPath.Contains("/content/blog/", StringComparison.OrdinalIgnoreCase))
            return "wp-post-";
        if (normalizedPath.Contains("/content/pages/", StringComparison.OrdinalIgnoreCase))
            return "wp-page-";
        return null;
    }

    private static string NormalizeImportedFrontMatter(string frontMatter, string body, string? normalizedTranslationKey)
    {
        if (string.IsNullOrWhiteSpace(frontMatter))
            return frontMatter ?? string.Empty;

        var normalized = frontMatter;
        if (!string.IsNullOrWhiteSpace(normalizedTranslationKey))
        {
            var existingTranslationKey = GetFrontMatterValue(frontMatter, "translation_key");
            if (!string.Equals(existingTranslationKey, normalizedTranslationKey, StringComparison.Ordinal))
            {
                var escapedTranslationKey = normalizedTranslationKey.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
                if (Regex.IsMatch(normalized, @"(?im)^translation_key:\s*(?:""[^""]*""|[^\r\n]+)\s*$"))
                {
                    normalized = Regex.Replace(
                        normalized,
                        @"(?im)^translation_key:\s*(?:""[^""]*""|[^\r\n]+)\s*$",
                        $"translation_key: \"{escapedTranslationKey}\"");
                }
                else
                {
                    var languageMatch = Regex.Match(normalized, @"(?im)^language:\s*(?:""[^""]*""|[^\r\n]+)\s*$");
                    if (languageMatch.Success)
                    {
                        var insertAt = languageMatch.Index + languageMatch.Length;
                        var lineBreak = languageMatch.Value.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                        normalized = normalized.Insert(insertAt, lineBreak + $"translation_key: \"{escapedTranslationKey}\"");
                    }
                    else
                    {
                        normalized = Regex.Replace(
                            normalized,
                            @"\A---\r?\n",
                            $"---{Environment.NewLine}translation_key: \"{escapedTranslationKey}\"{Environment.NewLine}");
                    }
                }
            }
        }

        var description = GetFrontMatterValue(frontMatter, "description");
        if (!string.IsNullOrWhiteSpace(description))
        {
            var cleanDescription = description;
            var needsCleanup =
                Regex.IsMatch(cleanDescription, @"(?im)\[(?:\/)?(?:vc_|cq_vc_)") ||
                Regex.IsMatch(cleanDescription, @"(?is)<[^>]+>") ||
                cleanDescription.Contains("\\\"", StringComparison.Ordinal);
            if (needsCleanup)
            {
                cleanDescription = Regex.Replace(cleanDescription, @"(?im)\[(?:\/)?(?:vc_|cq_vc_)[^\]]*(?:\]|$)", " ");
                cleanDescription = Regex.Replace(cleanDescription, @"(?is)<[^>]+>", " ");
                cleanDescription = WebUtility.HtmlDecode(cleanDescription);
                cleanDescription = Regex.Replace(cleanDescription, @"\s+", " ").Trim();
                if (string.IsNullOrWhiteSpace(cleanDescription))
                    cleanDescription = GetDescriptionFromBody(body);
                if (!string.Equals(cleanDescription, description, StringComparison.Ordinal))
                {
                    var escaped = cleanDescription.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
                    normalized = Regex.Replace(
                        normalized,
                        @"(?im)^description:\s*(?:""[^""]*""|[^\r\n]+)\s*$",
                        $"description: \"{escaped}\"");
                }
            }
        }

        if (Regex.IsMatch(normalized, @"(?im)^meta\.raw_html:\s*true\s*$"))
        {
            var hasHtmlBlocks = Regex.IsMatch(body, @"(?is)<(?:article|section|div|span|p|h[1-6]|ul|ol|li|table|tr|td|th|img|a|iframe|blockquote|pre|code)\b");
            var hasMarkdownSignals =
                Regex.IsMatch(body, @"(?m)^#{1,6}\s+") ||
                Regex.IsMatch(body, @"(?m)^```") ||
                Regex.IsMatch(body, @"(?m)^\s*[-*]\s+") ||
                Regex.IsMatch(body, @"(?m)^\s*\d+\.\s+");

            if (hasMarkdownSignals || !hasHtmlBlocks)
                normalized = Regex.Replace(normalized, @"(?im)^meta\.raw_html:\s*true\s*\r?\n?", string.Empty);
        }

        return normalized;
    }

    private static string NormalizeImportedBody(string body, bool strictHtmlCleanup)
    {
        var normalized = body ?? string.Empty;

        normalized = Regex.Replace(normalized, @"(?is)<pre\b(?<attrs>[^>]*)>(?<code>.*?)</pre>", static match =>
        {
            var attrs = match.Groups["attrs"].Value;
            var codeRaw = match.Groups["code"].Value;
            var langMatch = Regex.Match(attrs, @"(?i)data-enlighter-language\s*=\s*""(?<lang>[^""]+)""");
            var lang = langMatch.Success ? Regex.Replace(langMatch.Groups["lang"].Value.Trim(), @"[^A-Za-z0-9#+._-]", string.Empty) : string.Empty;
            var decoded = WebUtility.HtmlDecode(codeRaw).Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal).Trim('\n');
            var fence = string.IsNullOrWhiteSpace(lang) ? "```" : "```" + lang;
            return $"\n{fence}\n{decoded}\n```\n";
        });

        normalized = Regex.Replace(normalized, @"(?is)<h(?<level>[1-6])\b[^>]*>(?<inner>.*?)</h\k<level>>", static match =>
        {
            var level = int.TryParse(match.Groups["level"].Value, out var parsed) ? parsed : 2;
            var text = Regex.Replace(match.Groups["inner"].Value, @"(?is)<[^>]+>", string.Empty);
            text = WebUtility.HtmlDecode(text);
            text = Regex.Replace(text, @"\s+", " ").Trim();
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;
            return $"\r\n{new string('#', Math.Clamp(level, 1, 6))} {text}\r\n\r\n";
        });

        normalized = Regex.Replace(normalized, @"(?is)<p\b[^>]*>\s*(?<img><img\b[^>]*>)\s*</p>", static match =>
            "\r\n\r\n" + ConvertImageHtmlToMarkdown(match.Groups["img"].Value) + "\r\n\r\n");
        normalized = Regex.Replace(normalized, @"(?is)<img\b[^>]*>", static match => ConvertImageHtmlToMarkdown(match.Value));

        normalized = Regex.Replace(normalized, @"(?is)</?(strong|b)\b[^>]*>", "**");
        normalized = Regex.Replace(normalized, @"(?is)</?(em|i)\b[^>]*>", "*");
        normalized = Regex.Replace(normalized, @"(?is)<br\s*/?>", "\r\n");
        normalized = Regex.Replace(normalized, @"(?is)<p\b[^>]*>", string.Empty);
        normalized = Regex.Replace(normalized, @"(?is)</p>", "\r\n\r\n");
        normalized = Regex.Replace(normalized, @"(?is)</?div\b[^>]*>", string.Empty);
        normalized = Regex.Replace(normalized, @"(?im)\[(?:\/)?(?:vc_[^\]]+|cq_vc_[^\]]+)\]", string.Empty);

        normalized = Regex.Replace(
            normalized,
            @"\[[^\]\r\n]*\]\((?<url>https?://(?:www\.)?(?:facebook\.com/sharer/sharer\.php|twitter\.com/intent/tweet|pinterest\.com/pin/create/button|tumblr\.com/widgets/share/tool|linkedin\.com/shareArticle|reddit\.com/submit)[^)]+)\)",
            string.Empty);
        normalized = Regex.Replace(normalized, @"https?://(?:www\.)?evotec\.(?:xyz|pl)/wp-content/uploads/", "/wp-content/uploads/");
        normalized = Regex.Replace(normalized, @"(?im)^[ \t]*\[(?:Next|\d+)\]\(https?://[^)\r\n]*/wp-json/wp/v2/pages/page/[^)\r\n]*\)[ \t]*\r?$", string.Empty);
        normalized = Regex.Replace(normalized, @"(?im)^[ \t]*\.\.\.[ \t]*\r?$", string.Empty);
        normalized = Regex.Replace(normalized, @"(?im)^[ \t]*…[ \t]*\r?$", string.Empty);
        normalized = Regex.Replace(normalized, @"(?im)(\[(?:Read More|Czytaj więcej)\]\([^)]+\))[ \t]+(?=!\[)", "$1\r\n\r\n");
        normalized = Regex.Replace(normalized, @"(?im)(!\[[^\]]*\]\([^)]+\))(?=\S)", "$1\r\n\r\n");

        if (strictHtmlCleanup)
            normalized = StripHtmlOutsideCodeFences(normalized);

        normalized = Regex.Replace(normalized, @"(?m)^`\$lang\s*$", "```powershell");
        normalized = Regex.Replace(normalized, @"(?m)^`\s*$", "```");
        normalized = normalized.Replace('\u00A0', ' ');
        normalized = Regex.Replace(normalized, @"[\t ]+\r?\n", "\r\n");
        normalized = Regex.Replace(normalized, @"(?:\r?\n){4,}", "\r\n\r\n\r\n");

        return normalized.TrimEnd() + "\r\n";
    }

    private static string StripHtmlOutsideCodeFences(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var lines = Regex.Split(text, @"\r?\n");
        var output = new List<string>();
        var block = new List<string>();
        var inFence = false;

        void FlushBlock()
        {
            if (block.Count == 0)
                return;
            var joined = string.Join("\r\n", block);
            joined = Regex.Replace(joined, @"(?is)<[^>]+>", string.Empty);
            output.Add(joined);
            block.Clear();
        }

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                if (!inFence)
                {
                    FlushBlock();
                    output.Add(line);
                    inFence = true;
                }
                else
                {
                    output.Add(line);
                    inFence = false;
                }

                continue;
            }

            if (inFence)
                output.Add(line);
            else
                block.Add(line);
        }

        FlushBlock();
        return string.Join("\r\n", output);
    }

    private static string ConvertImageHtmlToMarkdown(string imageHtml)
    {
        if (string.IsNullOrWhiteSpace(imageHtml))
            return imageHtml;

        var imgMatch = Regex.Match(imageHtml, @"(?is)<img\b(?<attrs>[^>]*)>");
        if (!imgMatch.Success)
            return imageHtml;

        var attrs = ParseHtmlAttributes(imgMatch.Groups["attrs"].Value);
        var src = attrs.TryGetValue("src", out var srcValue) && !string.IsNullOrWhiteSpace(srcValue)
            ? srcValue
            : attrs.TryGetValue("data-src", out var dataSrcValue) ? dataSrcValue : string.Empty;
        if (string.IsNullOrWhiteSpace(src))
            return imageHtml;

        src = WebUtility.HtmlDecode(src.Trim());
        var alt = attrs.TryGetValue("alt", out var altValue) ? WebUtility.HtmlDecode(altValue).Replace("]", "\\]", StringComparison.Ordinal).Trim() : string.Empty;
        var title = attrs.TryGetValue("title", out var titleValue) ? WebUtility.HtmlDecode(titleValue).Trim() : string.Empty;
        var titleSuffix = string.IsNullOrWhiteSpace(title) ? string.Empty : $" \"{title.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
        return $"![{alt}]({src}{titleSuffix})";
    }

    private static Dictionary<string, string> ParseHtmlAttributes(string raw)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw))
            return result;

        var matches = Regex.Matches(raw, @"(?<name>[A-Za-z_:][-A-Za-z0-9_:.]*)(?:\s*=\s*(?:""(?<dq>[^""]*)""|'(?<sq>[^']*)'|(?<bare>[^\s""'=<>`]+)))?");
        foreach (Match match in matches)
        {
            var name = match.Groups["name"].Value;
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var value = match.Groups["dq"].Success
                ? match.Groups["dq"].Value
                : match.Groups["sq"].Success
                    ? match.Groups["sq"].Value
                    : match.Groups["bare"].Success
                        ? match.Groups["bare"].Value
                        : string.Empty;
            result[name] = value;
        }

        return result;
    }

    private static string GetDescriptionFromBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;

        var plain = body;
        plain = Regex.Replace(plain, @"(?s)```.*?```", " ");
        plain = Regex.Replace(plain, @"(?is)!\[[^\]]*\]\([^)]+\)", " ");
        plain = Regex.Replace(plain, @"(?m)^#{1,6}\s*", string.Empty);
        plain = Regex.Replace(plain, @"(?is)<[^>]+>", " ");
        plain = WebUtility.HtmlDecode(plain);
        plain = plain.Replace('\u00A0', ' ');
        plain = Regex.Replace(plain, @"\s+", " ").Trim();
        if (plain.Length <= 240)
            return plain;
        return plain.Substring(0, 240).TrimEnd() + "...";
    }

    private static string SafeReadAllText(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch
        {
            return string.Empty;
        }
    }
}
