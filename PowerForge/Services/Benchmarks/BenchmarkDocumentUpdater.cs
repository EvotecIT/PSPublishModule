using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Updates marker-delimited benchmark blocks inside Markdown documents.
/// </summary>
public sealed class BenchmarkDocumentUpdater
{
    /// <summary>
    /// Replaces a benchmark block in a document.
    /// </summary>
    /// <param name="path">Markdown document path.</param>
    /// <param name="blockId">Block identifier.</param>
    /// <param name="markdown">Replacement Markdown.</param>
    /// <returns>Update result.</returns>
    public BenchmarkDocumentUpdateResult UpdateBlock(string path, string blockId, string markdown)
    {
        var block = ReadBlock(path, blockId);

        var replacement = (markdown ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd();
        var updatedLines = new List<string>();
        updatedLines.AddRange(block.Lines.Take(block.Start + 1));
        if (replacement.Length > 0)
            updatedLines.AddRange(replacement.Split('\n'));
        updatedLines.AddRange(block.Lines.Skip(block.End));

        var updated = string.Join("\n", updatedLines);
        if (block.Original.Contains("\r\n", StringComparison.Ordinal))
            updated = updated.Replace("\n", "\r\n");

        var changed = !string.Equals(block.Original, updated, StringComparison.Ordinal);
        if (changed)
            File.WriteAllText(path, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return new BenchmarkDocumentUpdateResult
        {
            Path = Path.GetFullPath(path),
            BlockId = blockId,
            Changed = changed
        };
    }

    /// <summary>
    /// Validates that a benchmark block exists without modifying the document.
    /// </summary>
    /// <param name="path">Markdown document path.</param>
    /// <param name="blockId">Block identifier.</param>
    public void ValidateBlock(string path, string blockId)
        => _ = ReadBlock(path, blockId);

    private static BenchmarkBlockLocation ReadBlock(string path, string blockId)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Document path is required.", nameof(path));
        if (string.IsNullOrWhiteSpace(blockId)) throw new ArgumentException("Block id is required.", nameof(blockId));
        if (!File.Exists(path)) throw new FileNotFoundException($"Benchmark document was not found: {path}", path);

        var original = File.ReadAllText(path);
        var normalized = original.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var start = -1;
        var end = -1;

        for (var i = 0; i < lines.Length; i++)
        {
            if (start < 0 && IsMarker(lines[i], blockId, "START"))
            {
                start = i;
                continue;
            }

            if (start >= 0 && IsMarker(lines[i], blockId, "END"))
            {
                end = i;
                break;
            }
        }

        if (start < 0 || end < 0 || end <= start)
            throw new InvalidOperationException($"Benchmark block '{blockId}' was not found in '{path}'.");

        return new BenchmarkBlockLocation(original, lines, start, end);
    }

    private static bool IsMarker(string line, string blockId, string side)
    {
        var text = (line ?? string.Empty).Trim();
        if (!text.StartsWith("<!--", StringComparison.Ordinal) || !text.EndsWith("-->", StringComparison.Ordinal))
            return false;

        var body = text.Substring(4, text.Length - 7).Trim();
        var escaped = Regex.Escape(blockId.Trim());
        var sidePattern = Regex.Escape(side.Trim());
        return Regex.IsMatch(body, $"(^|:)({escaped})(:|$).*(:{sidePattern}$|^{sidePattern}$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
               || Regex.IsMatch(body, $"(^|:){escaped}:{sidePattern}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private sealed class BenchmarkBlockLocation
    {
        internal BenchmarkBlockLocation(string original, string[] lines, int start, int end)
        {
            Original = original;
            Lines = lines;
            Start = start;
            End = end;
        }

        internal string Original { get; }

        internal string[] Lines { get; }

        internal int Start { get; }

        internal int End { get; }
    }
}
