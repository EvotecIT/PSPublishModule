using System.Text;

namespace PowerForgeStudio.Domain.Hub;

internal sealed class MarkdownThreadBuilder
{
    private readonly StringBuilder _builder = new();

    public void Paragraph(string text)
    {
        var normalized = Normalize(text);
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        _builder.AppendLine(normalized);
        _builder.AppendLine();
    }

    public void BlankLine()
    {
        _builder.AppendLine();
    }

    public void HorizontalRule()
    {
        _builder.AppendLine("---");
    }

    public void Heading(int level, string text)
    {
        if (level < 1 || level > 6)
            throw new ArgumentOutOfRangeException(nameof(level), "Markdown heading level must be between 1 and 6.");

        // Thread output keeps headings tight so timeline entries do not accumulate extra vertical gaps.
        _builder.Append('#', level);
        _builder.Append(' ');
        _builder.AppendLine(text ?? string.Empty);
    }

    public void InlineLine(string prefix, string value, string suffix = "")
    {
        _builder.Append(prefix);
        _builder.Append(value);
        _builder.AppendLine(suffix);
    }

    public override string ToString()
        => _builder.ToString();

    private static string Normalize(string text)
        => (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Trim().Replace("\n", Environment.NewLine);
}
