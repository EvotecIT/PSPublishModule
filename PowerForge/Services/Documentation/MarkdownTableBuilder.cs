using System.Text;

namespace PowerForge;

internal sealed class MarkdownTableBuilder
{
    private readonly string[] _headers;
    private readonly MarkdownTableAlignment[] _alignments;
    private readonly List<string[]> _rows = new();

    public MarkdownTableBuilder(IReadOnlyList<string> headers, IReadOnlyList<MarkdownTableAlignment>? alignments = null)
    {
        if (headers is null || headers.Count == 0)
            throw new ArgumentException("At least one markdown table header is required.", nameof(headers));

        _headers = headers.Select(EscapeCell).ToArray();
        _alignments = Enumerable.Range(0, _headers.Length)
            .Select(index => alignments is not null && index < alignments.Count ? alignments[index] : MarkdownTableAlignment.Left)
            .ToArray();
    }

    public void AddRow(params string[] cells)
    {
        if (cells.Length != _headers.Length)
            throw new ArgumentException("Markdown table row cell count must match the header count.", nameof(cells));

        _rows.Add(cells.Select(EscapeCell).ToArray());
    }

    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.Append("| ");
        builder.Append(string.Join(" | ", _headers));
        builder.AppendLine(" |");
        builder.Append("| ");
        builder.Append(string.Join(" | ", _alignments.Select(ToSeparator)));
        builder.AppendLine(" |");

        foreach (var row in _rows)
        {
            builder.Append("| ");
            builder.Append(string.Join(" | ", row));
            builder.AppendLine(" |");
        }

        return builder.ToString();
    }

    private static string ToSeparator(MarkdownTableAlignment alignment)
        => alignment switch
        {
            MarkdownTableAlignment.Center => ":---:",
            MarkdownTableAlignment.Right => "---:",
            _ => "---"
        };

    private static string EscapeCell(string? value)
        => (value ?? string.Empty).Replace("|", "\\|");
}

internal enum MarkdownTableAlignment
{
    Left = 0,
    Center = 1,
    Right = 2
}
