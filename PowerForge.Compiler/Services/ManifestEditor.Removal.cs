using System;
using System.Management.Automation.Language;

namespace PowerForge;

public static partial class ManifestEditor
{
    private static bool RemoveKeyValue(HashtableAst hash, string content, string filePath, string key)
    {
        foreach (var pair in hash.KeyValuePairs)
        {
            if (!string.Equals(GetKeyName(pair.Item1), key, StringComparison.OrdinalIgnoreCase))
                continue;

            var start = pair.Item1.Extent.StartOffset;
            var end = pair.Item2.Extent.EndOffset;
            if (start < 0 || end <= start || end > content.Length)
                return false;

            // Consume an optional statement separator, never a sibling or a closing brace.
            var afterValue = end;
            while (afterValue < content.Length && IsHorizontalWhitespace(content[afterValue]))
                afterValue++;
            if (afterValue < content.Length && content[afterValue] == ';')
                end = afterValue + 1;

            var lineStart = start;
            while (lineStart > 0 && IsHorizontalWhitespace(content[lineStart - 1]))
                lineStart--;
            var lineEnd = end;
            while (lineEnd < content.Length && IsHorizontalWhitespace(content[lineEnd]))
                lineEnd++;

            // Remove the complete line only when the entry owns it. Keep inline comments.
            var ownsLineStart = lineStart == 0 || content[lineStart - 1] == '\n' || content[lineStart - 1] == '\r';
            var ownsLineEnd = lineEnd == content.Length || content[lineEnd] == '\n' || content[lineEnd] == '\r';
            if (ownsLineStart && ownsLineEnd)
            {
                start = lineStart;
                end = lineEnd;
                if (end < content.Length && content[end] == '\r') end++;
                if (end < content.Length && content[end] == '\n') end++;
            }

            var updated = content.Remove(start, end - start);
            Parser.ParseInput(updated, out _, out var errors);
            if (errors.Length > 0)
                return false;

            WriteManifest(filePath, content, updated);
            return true;
        }
        return false;
    }

    private static bool IsHorizontalWhitespace(char value) => value == ' ' || value == '\t';
}
