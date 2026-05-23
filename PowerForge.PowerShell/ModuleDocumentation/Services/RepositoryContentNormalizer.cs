using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Normalizes repository-authored markdown so relative links and assets resolve correctly when rendered outside GitHub.
/// </summary>
internal static class RepositoryContentNormalizer
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex MarkdownLinkRegex = new(
        @"(!?\[[^\]]*\]\()([^\)]+)(\))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex HtmlUrlAttributeRegex = new(
        @"(?<prefix>\b(?:src|href)\s*=\s*)(?<quote>['""])(?<url>.*?)(\k<quote>)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexTimeout);
    private static readonly Regex HtmlLinkSafetyAttributeRegex = new(
        @"\s+(?:target|rel)\s*=\s*(?:(['""]).*?\1|[^\s>]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexTimeout);
    private static readonly Regex FenceRegex = new(
        @"^\s*(?<marker>`{3,}|~{3,})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex FrontMatterRegex = new(
        @"\A---\s*\r?\n.*?\r?\n---\s*(?:\r?\n|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline,
        RegexTimeout);
    private static readonly Regex DateNamedDocRegex = new(
        @"^\d{4}(?:-\d{2}){0,2}\.m(?:arkdown|d)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexTimeout);

    internal static string? BuildRawBase(string? projectUri, string? refName)
    {
        var normalizedProjectUri = projectUri?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedProjectUri))
            return null;

        try
        {
            var info = RepoUrlParser.Parse(normalizedProjectUri!);
            if (info.Host == RepoHost.GitHub && !string.IsNullOrEmpty(info.Owner) && !string.IsNullOrEmpty(info.Repo))
            {
                var reference = refName;
                if (string.IsNullOrWhiteSpace(reference))
                    reference = "main";
                else
                    reference = reference!.Trim();
                return $"https://raw.githubusercontent.com/{info.Owner}/{info.Repo}/{reference.Trim('/')}/";
            }
            if (info.Host == RepoHost.AzureDevOps && !string.IsNullOrEmpty(info.Organization) && !string.IsNullOrEmpty(info.Project) && !string.IsNullOrEmpty(info.Repository))
            {
                var reference = string.IsNullOrWhiteSpace(refName) ? "main" : refName!.Trim();
                return $"https://dev.azure.com/{EscapeSegment(info.Organization!)}/{EscapeSegment(info.Project!)}/_apis/git/repositories/{EscapeSegment(info.Repository!)}/items?api-version=7.1&versionDescriptor.version={Uri.EscapeDataString(reference)}&path={{path}}";
            }
        }
        catch
        {
            // Fall through to null for unsupported/invalid repository URLs.
        }

        return null;
    }

    internal static string? BuildBlobBase(string? projectUri, string? refName)
    {
        var normalizedProjectUri = projectUri?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedProjectUri))
            return null;

        try
        {
            var info = RepoUrlParser.Parse(normalizedProjectUri!);
            if (info.Host == RepoHost.GitHub && !string.IsNullOrEmpty(info.Owner) && !string.IsNullOrEmpty(info.Repo))
            {
                var reference = refName;
                if (string.IsNullOrWhiteSpace(reference))
                    reference = "main";
                else
                    reference = reference!.Trim();
                return $"https://github.com/{info.Owner}/{info.Repo}/blob/{reference.Trim('/')}/";
            }
            if (info.Host == RepoHost.AzureDevOps && !string.IsNullOrEmpty(info.Organization) && !string.IsNullOrEmpty(info.Project) && !string.IsNullOrEmpty(info.Repository))
            {
                var reference = string.IsNullOrWhiteSpace(refName) ? "main" : refName!.Trim();
                return $"https://dev.azure.com/{EscapeSegment(info.Organization!)}/{EscapeSegment(info.Project!)}/_git/{EscapeSegment(info.Repository!)}?version=GB{Uri.EscapeDataString(reference)}&path={{path}}";
            }
        }
        catch
        {
            // Fall through to null for unsupported/invalid repository URLs.
        }

        return null;
    }

    internal static string RewriteRelativeUris(string markdown, string? rawBaseUri)
        => RewriteRelativeUris(markdown, rawBaseUri, null);

    internal static string RewriteRelativeUris(string markdown, string? rawBaseUri, string? blobBaseUri)
        => RewriteRelativeUris(markdown, rawBaseUri, blobBaseUri, null);

    internal static string RewriteRelativeUris(string markdown, string? rawBaseUri, string? blobBaseUri, string? documentPath)
    {
        if (string.IsNullOrWhiteSpace(markdown) || string.IsNullOrWhiteSpace(rawBaseUri))
            return markdown ?? string.Empty;

        var resolvedRawBaseUri = rawBaseUri!;
        var resolvedBlobBaseUri = blobBaseUri;
        var usesCrLf = markdown.Contains("\r\n", StringComparison.Ordinal);
        var normalized = markdown.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');
        var builder = new StringBuilder(normalized.Length + 64);

        var inFence = false;
        char fenceChar = '\0';
        var fenceLength = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();
            var fenceMatch = FenceRegex.Match(trimmed);
            if (fenceMatch.Success)
            {
                var marker = fenceMatch.Groups["marker"].Value;
                var markerChar = marker[0];
                var markerLength = marker.Length;

                if (!inFence)
                {
                    inFence = true;
                    fenceChar = markerChar;
                    fenceLength = markerLength;
                }
                else if (markerChar == fenceChar && markerLength >= fenceLength)
                {
                    inFence = false;
                    fenceChar = '\0';
                    fenceLength = 0;
                }

                builder.Append(line);
            }
            else if (inFence)
            {
                builder.Append(line);
            }
            else
            {
                builder.Append(SanitizeHtmlLinkAttributes(RewriteHtmlAttributes(RewriteMarkdownLinks(line, resolvedRawBaseUri, resolvedBlobBaseUri, documentPath), resolvedRawBaseUri, resolvedBlobBaseUri, documentPath)));
            }

            if (i < lines.Length - 1)
                builder.Append('\n');
        }

        var rewritten = builder.ToString();
        return usesCrLf ? rewritten.Replace("\n", "\r\n") : rewritten;
    }

    internal static bool ContainsRelativeUriCandidate(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return false;

        foreach (Match match in MarkdownLinkRegex.Matches(markdown!))
        {
            if (ShouldRewriteUrl(match.Groups[2].Value))
                return true;
        }

        foreach (Match match in HtmlUrlAttributeRegex.Matches(markdown!))
        {
            if (ShouldRewriteUrl(match.Groups["url"].Value))
                return true;
        }

        return false;
    }

    internal static bool IsLikelyTemplateSource(string? fileName, string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        var normalizedName = Path.GetFileName(fileName ?? string.Empty);
        var hasLiquidToken =
            content.Contains("{%", StringComparison.Ordinal) ||
            content.Contains("{{", StringComparison.Ordinal) ||
            content.Contains("{#", StringComparison.Ordinal);

        if (!hasLiquidToken)
            return false;

        var hasFrontMatter = FrontMatterRegex.IsMatch(content);
        var hasJekyllHints =
            content.Contains("site.posts", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("permalink:", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("layout:", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("| date:", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("friendlydate", StringComparison.OrdinalIgnoreCase);

        return hasFrontMatter || hasJekyllHints || DateNamedDocRegex.IsMatch(normalizedName);
    }

    internal static string WrapAsSourceCodeBlock(string content, string language = "markdown")
    {
        var normalizedLanguage = string.IsNullOrWhiteSpace(language) ? "text" : language.Trim();
        return $"~~~{normalizedLanguage}\n{content}\n~~~";
    }

    private static string RewriteMarkdownLinks(string text, string rawBaseUri, string? blobBaseUri, string? documentPath)
    {
        return MarkdownLinkRegex.Replace(text, match =>
        {
            var isImage = match.Groups[1].Value.StartsWith("![", StringComparison.Ordinal);
            var url = match.Groups[2].Value;
            if (!ShouldRewriteUrl(url))
                return match.Value;

            try
            {
                var baseUri = !isImage && IsLikelyViewableDocument(url) && !string.IsNullOrWhiteSpace(blobBaseUri)
                    ? blobBaseUri!
                    : rawBaseUri;
                var absolute = ResolveRelativeUrl(baseUri, url, documentPath);
                return match.Groups[1].Value + absolute + match.Groups[3].Value;
            }
            catch
            {
                return match.Value;
            }
        });
    }

    private static string RewriteHtmlAttributes(string text, string rawBaseUri, string? blobBaseUri, string? documentPath)
    {
        return HtmlUrlAttributeRegex.Replace(text, match =>
        {
            var attributePrefix = match.Groups["prefix"].Value;
            var url = match.Groups["url"].Value;
            if (!ShouldRewriteUrl(url))
                return match.Value;

            try
            {
                var isHref = attributePrefix.Contains("href", StringComparison.OrdinalIgnoreCase);
                var baseUri = isHref && IsLikelyViewableDocument(url) && !string.IsNullOrWhiteSpace(blobBaseUri)
                    ? blobBaseUri!
                    : rawBaseUri;
                var absolute = ResolveRelativeUrl(baseUri, url, documentPath);
                return match.Groups["prefix"].Value + match.Groups["quote"].Value + absolute + match.Groups["quote"].Value;
            }
            catch
            {
                return match.Value;
            }
        });
    }

    private static string SanitizeHtmlLinkAttributes(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        return HtmlLinkSafetyAttributeRegex.Replace(text, string.Empty);
    }

    private static string ResolveRelativeUrl(string baseUri, string url, string? documentPath)
    {
        var resolvedUrl = ResolveAgainstDocumentPath(url, documentPath);
        if (baseUri.Contains("{path}", StringComparison.Ordinal))
        {
            var normalized = (resolvedUrl ?? string.Empty).Replace('\\', '/').TrimStart('/');
            var fragment = string.Empty;
            var fragmentIndex = normalized.IndexOf('#');
            if (fragmentIndex >= 0)
            {
                fragment = normalized.Substring(fragmentIndex);
                normalized = normalized.Substring(0, fragmentIndex);
            }

            var query = string.Empty;
            var queryIndex = normalized.IndexOf('?');
            if (queryIndex >= 0)
            {
                query = normalized.Substring(queryIndex);
                normalized = normalized.Substring(0, queryIndex);
            }

            var encodedPath = Uri.EscapeDataString("/" + normalized);
            return baseUri.Replace("{path}", encodedPath) + query + fragment;
        }

        return new Uri(new Uri(baseUri, UriKind.Absolute), resolvedUrl).ToString();
    }

    private static string ResolveAgainstDocumentPath(string url, string? documentPath)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(documentPath))
            return url;

        var normalizedDocumentPath = documentPath!.Replace('\\', '/').Trim('/');
        var slash = normalizedDocumentPath.LastIndexOf('/');
        if (slash < 0)
            return url;

        var directory = normalizedDocumentPath.Substring(0, slash);
        if (string.IsNullOrWhiteSpace(directory))
            return url;

        var path = url;
        var fragment = string.Empty;
        var fragmentIndex = path.IndexOf('#');
        if (fragmentIndex >= 0)
        {
            fragment = path.Substring(fragmentIndex);
            path = path.Substring(0, fragmentIndex);
        }

        var query = string.Empty;
        var queryIndex = path.IndexOf('?');
        if (queryIndex >= 0)
        {
            query = path.Substring(queryIndex);
            path = path.Substring(0, queryIndex);
        }

        var segments = new System.Collections.Generic.List<string>(
            directory.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries));

        foreach (var segment in path.Replace('\\', '/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
                continue;

            if (segment == "..")
            {
                if (segments.Count > 0)
                    segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return string.Join("/", segments) + query + fragment;
    }

    private static string EscapeSegment(string value)
        => Uri.EscapeDataString(value);

    private static bool ShouldRewriteUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        var trimmed = (url ?? string.Empty).Trim();
        return !trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
               && !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
               && !trimmed.StartsWith("//", StringComparison.OrdinalIgnoreCase)
               && !trimmed.StartsWith("#", StringComparison.OrdinalIgnoreCase)
               && !trimmed.StartsWith("/", StringComparison.OrdinalIgnoreCase)
               && !trimmed.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
               && !trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
               && !trimmed.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
               && !trimmed.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLikelyViewableDocument(string url)
    {
        var withoutQuery = url.Split('?', '#')[0];
        var extension = Path.GetExtension(withoutQuery);
        if (string.IsNullOrWhiteSpace(extension))
            return false;

        return extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".markdown", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".help", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".psm1", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".psd1", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".ps1xml", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".yml", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".xml", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".cs", StringComparison.OrdinalIgnoreCase);
    }
}
