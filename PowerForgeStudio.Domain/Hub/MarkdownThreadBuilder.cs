using System.Text;

namespace PowerForgeStudio.Domain.Hub;

internal sealed class MarkdownThreadBuilder
{
    private readonly StringBuilder _builder = new();

    public void Paragraph(string text)
    {
        _builder.AppendLine(text ?? string.Empty);
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
}
