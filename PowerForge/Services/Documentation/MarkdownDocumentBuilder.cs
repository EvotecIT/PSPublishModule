using System.Text;

namespace PowerForge;

internal sealed class MarkdownDocumentBuilder
{
    private readonly StringBuilder _body = new();
    private readonly StringBuilder _frontMatter = new();
    private readonly YamlTextWriter _frontMatterWriter;
    private readonly bool _blankLineAfterFrontMatter;
    private bool _hasFrontMatter;

    public MarkdownDocumentBuilder(bool blankLineAfterFrontMatter = true)
    {
        _blankLineAfterFrontMatter = blankLineAfterFrontMatter;
        _frontMatterWriter = new YamlTextWriter(_frontMatter);
    }

    public void FrontMatter(string key, string value)
    {
        _frontMatterWriter.WriteScalar(key, value);
        _hasFrontMatter = true;
    }

    public void FrontMatter(string key, IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
            return;

        _frontMatterWriter.WriteSequence(key, values);
        _hasFrontMatter = true;
    }

    public void FrontMatterRaw(string key, string? value = null)
    {
        _frontMatter.Append(key);
        _frontMatter.Append(':');
        if (value is not null)
        {
            _frontMatter.Append(' ');
            _frontMatter.Append(value);
        }
        _frontMatter.AppendLine();
        _hasFrontMatter = true;
    }

    public void Heading(int level, string text)
    {
        if (level < 1 || level > 6)
            throw new ArgumentOutOfRangeException(nameof(level), "Markdown heading level must be between 1 and 6.");

        _body.Append('#', level);
        _body.Append(' ');
        _body.AppendLine(text?.Trim() ?? string.Empty);
        _body.AppendLine();
    }

    public void Paragraph(string text)
    {
        var normalized = Normalize(text);
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        _body.AppendLine(normalized);
        _body.AppendLine();
    }

    public void Bullets(IEnumerable<string> items)
    {
        var wroteAny = false;
        foreach (var item in items)
        {
            var normalized = Normalize(item);
            if (string.IsNullOrWhiteSpace(normalized))
                continue;

            _body.Append("- ");
            _body.AppendLine(normalized);
            wroteAny = true;
        }

        if (wroteAny)
            _body.AppendLine();
    }

    public void CodeFence(string infoString, string content)
    {
        var normalized = NormalizeCodeFence(content);
        var fence = new string('`', Math.Max(3, LongestBacktickRun(normalized) + 1));
        _body.Append(fence);
        _body.AppendLine(infoString?.Trim() ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(normalized))
            _body.AppendLine(normalized);
        _body.AppendLine(fence);
        _body.AppendLine();
    }

    public static string InlineCode(string text)
    {
        var normalized = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Replace('\n', ' ');
        var delimiter = new string('`', Math.Max(1, LongestBacktickRun(normalized) + 1));
        var padding = normalized.StartsWith("`", StringComparison.Ordinal) ||
                      normalized.EndsWith("`", StringComparison.Ordinal)
            ? " "
            : string.Empty;
        return delimiter + padding + normalized + padding + delimiter;
    }

    public static string InlineIdentityCode(string text)
    {
        var encoded = (text ?? string.Empty)
            .Replace("\r", "%u000D")
            .Replace("\n", "%u000A");
        return InlineCode(encoded);
    }

    public void RawLine(string text)
    {
        _body.AppendLine(text ?? string.Empty);
    }

    public void BlankLine()
    {
        _body.AppendLine();
    }

    public override string ToString()
    {
        var builder = new StringBuilder(_body.Length + (_hasFrontMatter ? _frontMatter.Length + 16 : 0));
        if (_hasFrontMatter)
        {
            builder.AppendLine("---");
            builder.Append(_frontMatter);
            builder.AppendLine("---");
            if (_blankLineAfterFrontMatter)
                builder.AppendLine();
        }

        builder.Append(_body);
        return GeneratedTextNormalizer.Normalize(builder.ToString());
    }

    private static string Normalize(string text)
        => (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Trim().Replace("\n", Environment.NewLine);

    private static string NormalizeCodeFence(string text)
        => (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd().Replace("\n", Environment.NewLine);

    private static int LongestBacktickRun(string text)
    {
        var longest = 0;
        var current = 0;
        foreach (var character in text)
        {
            if (character == '`')
            {
                current++;
                longest = Math.Max(longest, current);
            }
            else
            {
                current = 0;
            }
        }
        return longest;
    }
}
