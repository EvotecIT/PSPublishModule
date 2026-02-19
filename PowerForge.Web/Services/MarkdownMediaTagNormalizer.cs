using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

internal static class MarkdownMediaTagNormalizer
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex FenceRegex = new("^(?:```|~~~)", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly HashSet<string> MediaTagNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "img",
        "iframe",
        "video",
        "audio",
        "source",
        "picture"
    };

    internal static string NormalizeMultilineMediaTagsOutsideFences(string markdown)
        => NormalizeMultilineMediaTagsOutsideFences(markdown, out _);

    internal static string NormalizeMultilineMediaTagsOutsideFences(string markdown, out int replacementCount)
    {
        var replacements = 0;
        if (string.IsNullOrWhiteSpace(markdown))
        {
            replacementCount = 0;
            return string.Empty;
        }

        var lines = markdown.Split('\n');
        var inFence = false;
        var outsideFence = new StringBuilder();
        var insideFence = new StringBuilder();
        var rebuilt = new StringBuilder(markdown.Length + 64);

        void FlushOutside()
        {
            if (outsideFence.Length == 0) return;
            var normalized = NormalizeMediaTagBlock(outsideFence.ToString(), out var blockReplacements);
            replacements += blockReplacements;
            rebuilt.Append(normalized);
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
            var target = inFence ? insideFence : outsideFence;
            target.Append(rawLine);
            target.Append('\n');

            if (!FenceRegex.IsMatch(rawLine.TrimStart()))
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
        if (!markdown.EndsWith('\n') && updated.EndsWith('\n'))
            updated = updated[..^1];

        replacementCount = replacements;
        return updated;
    }

    private static string NormalizeMediaTagBlock(string content, out int replacements)
    {
        replacements = 0;
        if (string.IsNullOrWhiteSpace(content))
            return content;

        var sb = new StringBuilder(content.Length);
        var index = 0;
        while (index < content.Length)
        {
            var lt = content.IndexOf('<', index);
            if (lt < 0)
            {
                sb.Append(content, index, content.Length - index);
                break;
            }

            if (lt > index)
                sb.Append(content, index, lt - index);

            if (!TryReadMediaTagName(content, lt, out var tagName, out var nameStart, out var nameLength))
            {
                sb.Append(content[lt]);
                index = lt + 1;
                continue;
            }

            if (!TryFindTagEnd(content, nameStart + nameLength, out var end))
            {
                sb.Append(content[lt]);
                index = lt + 1;
                continue;
            }

            var originalTag = content.Substring(lt, end - lt + 1);
            if (originalTag.IndexOf('\n') < 0 && originalTag.IndexOf('\r') < 0)
            {
                sb.Append(originalTag);
                index = end + 1;
                continue;
            }

            var normalizedTag = NormalizeTagWhitespacePreservingQuotedValues(originalTag);
            if (!string.Equals(originalTag, normalizedTag, StringComparison.Ordinal))
                replacements++;
            sb.Append(normalizedTag);
            index = end + 1;
        }

        return sb.ToString();
    }

    private static bool TryReadMediaTagName(
        string content,
        int tagStartIndex,
        out string tagName,
        out int tagNameIndex,
        out int tagNameLength)
    {
        tagName = string.Empty;
        tagNameIndex = -1;
        tagNameLength = 0;

        if (tagStartIndex < 0 || tagStartIndex >= content.Length || content[tagStartIndex] != '<')
            return false;

        var i = tagStartIndex + 1;
        while (i < content.Length && char.IsWhiteSpace(content[i])) i++;
        if (i >= content.Length) return false;
        if (content[i] == '/' || content[i] == '!' || content[i] == '?')
            return false;

        var start = i;
        while (i < content.Length && (char.IsLetterOrDigit(content[i]) || content[i] == '-' || content[i] == ':'))
            i++;
        if (i <= start) return false;

        var name = content.Substring(start, i - start);
        if (!MediaTagNames.Contains(name))
            return false;

        if (i < content.Length)
        {
            var boundary = content[i];
            if (!(char.IsWhiteSpace(boundary) || boundary == '>' || boundary == '/'))
                return false;
        }

        tagName = name;
        tagNameIndex = start;
        tagNameLength = i - start;
        return true;
    }

    private static bool TryFindTagEnd(string content, int fromIndex, out int endIndex)
    {
        endIndex = -1;
        var inSingle = false;
        var inDouble = false;
        for (var i = Math.Max(0, fromIndex); i < content.Length; i++)
        {
            var c = content[i];
            if (c == '\'' && !inDouble)
            {
                inSingle = !inSingle;
                continue;
            }
            if (c == '"' && !inSingle)
            {
                inDouble = !inDouble;
                continue;
            }
            if (c == '>' && !inSingle && !inDouble)
            {
                endIndex = i;
                return true;
            }
        }
        return false;
    }

    private static string NormalizeTagWhitespacePreservingQuotedValues(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return tag;

        var sb = new StringBuilder(tag.Length);
        var inSingle = false;
        var inDouble = false;
        var previousWasWhitespace = false;

        foreach (var c in tag)
        {
            if (c == '\'' && !inDouble)
            {
                inSingle = !inSingle;
                sb.Append(c);
                previousWasWhitespace = false;
                continue;
            }

            if (c == '"' && !inSingle)
            {
                inDouble = !inDouble;
                sb.Append(c);
                previousWasWhitespace = false;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (inSingle || inDouble)
                {
                    sb.Append(c is '\r' or '\n' ? ' ' : c);
                    previousWasWhitespace = false;
                }
                else if (!previousWasWhitespace)
                {
                    sb.Append(' ');
                    previousWasWhitespace = true;
                }

                continue;
            }

            sb.Append(c);
            previousWasWhitespace = false;
        }

        var normalized = sb.ToString().Trim();
        normalized = Regex.Replace(normalized, "\\s+/\\s*>", " />", RegexOptions.CultureInvariant, RegexTimeout);
        normalized = Regex.Replace(normalized, "\\s+>", ">", RegexOptions.CultureInvariant, RegexTimeout);
        normalized = Regex.Replace(normalized, "<\\s+", "<", RegexOptions.CultureInvariant, RegexTimeout);
        return normalized;
    }
}
