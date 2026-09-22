namespace PowerForgeStudio.Avalonia.Controls;

/// <summary>Formats bounded visual previews without modifying the original patch text.</summary>
public static class DiffPreviewPresentation
{
    public const int MaximumStyledLines = 400;
    public const int MaximumStyledLineLength = 2000;

    public static (IReadOnlyList<DiffPreviewLine> Lines, bool Truncated) Create(string? text)
    {
        var source = (text ?? "").Replace("\r\n", "\n");
        var lines = new List<string>(Math.Min(MaximumStyledLines + 1, source.Length / 32 + 1));
        var position = 0;
        while (position < source.Length && lines.Count <= MaximumStyledLines)
        {
            var end = source.IndexOf('\n', position);
            if (end < 0) { lines.Add(source[position..]); position = source.Length; break; }
            lines.Add(source[position..end]);
            position = end + 1;
        }
        if (source.Length == 0) lines.Add("");
        var truncated = lines.Count > MaximumStyledLines || position < source.Length;
        var patch = lines.Take(Math.Min(lines.Count, 12)).Any(line => line.StartsWith("diff --git ", StringComparison.Ordinal)
            || line.StartsWith("@@ ", StringComparison.Ordinal) || line.StartsWith("@@@ ", StringComparison.Ordinal));
        var preview = new List<DiffPreviewLine>(Math.Min(lines.Count, MaximumStyledLines));
        for (var index = 0; index < Math.Min(lines.Count, MaximumStyledLines); index++)
        {
            var line = lines[index];
            if (line.Length > MaximumStyledLineLength) truncated = true;
            preview.Add(new DiffPreviewLine(line.Length > MaximumStyledLineLength ? line[..MaximumStyledLineLength] + " …" : line,
                Classify(line, patch)));
        }
        return (preview, truncated);
    }

    private static DiffPreviewLineKind Classify(string line, bool patch)
    {
        if (!patch) return DiffPreviewLineKind.Context;
        if (line.StartsWith("@@", StringComparison.Ordinal)) return DiffPreviewLineKind.Hunk;
        if (line.StartsWith("diff --git ", StringComparison.Ordinal) || line.StartsWith("index ", StringComparison.Ordinal)
            || line.StartsWith("--- ", StringComparison.Ordinal) || line.StartsWith("+++ ", StringComparison.Ordinal)
            || line.StartsWith("\\ No newline", StringComparison.Ordinal) || line.StartsWith("new file mode ", StringComparison.Ordinal)
            || line.StartsWith("deleted file mode ", StringComparison.Ordinal) || line.StartsWith("rename ", StringComparison.Ordinal)
            || line.StartsWith("similarity index ", StringComparison.Ordinal)) return DiffPreviewLineKind.Metadata;
        if (line.StartsWith('+')) return DiffPreviewLineKind.Addition;
        if (line.StartsWith('-')) return DiffPreviewLineKind.Removal;
        return DiffPreviewLineKind.Context;
    }
}
