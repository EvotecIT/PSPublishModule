using System;
using System.Text;
using System.Text.Json;
using Spectre.Console;

namespace PSMaintenance.Rendering.Highlighters;

/// <summary>
/// JSON highlighter using System.Text.Json to colorize tokens without regex.
/// </summary>
internal sealed class JsonHighlighter : IHighlighter
{
    private static readonly string[] _langs = new[] { "json", "jsonc" };
    public bool CanHandle(string language) => Array.IndexOf(_langs, (language ?? string.Empty).ToLowerInvariant()) >= 0;

    public string? Highlight(string code, string language)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(code)) return string.Empty;
            using var doc = JsonDocument.Parse(code, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var sb = new StringBuilder();
            WriteElement(doc.RootElement, sb, 0);
            return sb.ToString();
        }
        catch { return null; }
    }

    private static void WriteElement(JsonElement el, StringBuilder sb, int indent)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                sb.Append("[grey]{[/]");
                if (!el.EnumerateObject().MoveNext()) { sb.Append("[grey]}[/]"); return; }
                sb.AppendLine();
                int i = 0; int count = 0; foreach (var _ in el.EnumerateObject()) count++;
                foreach (var prop in el.EnumerateObject())
                {
                    Indent(sb, indent + 1);
                    sb.Append("[cyan]\"").Append(Markup.Escape(prop.Name)).Append("\"[/]");
                    sb.Append(" [grey]:[/] ");
                    WriteElement(prop.Value, sb, indent + 1);
                    if (i < count - 1) sb.Append("[grey],[/]");
                    sb.AppendLine();
                    i++;
                }
                Indent(sb, indent); sb.Append("[grey]}[/]");
                break;

            case JsonValueKind.Array:
                sb.Append("[grey][[/]");
                var arrEnum = el.EnumerateArray();
                if (!arrEnum.MoveNext()) { sb.Append("[grey]][/]"); return; }
                sb.AppendLine();
                var items = el.EnumerateArray();
                int idx = 0; int total = 0; foreach (var _ in el.EnumerateArray()) total++;
                foreach (var item in items)
                {
                    Indent(sb, indent + 1);
                    WriteElement(item, sb, indent + 1);
                    if (idx < total - 1) sb.Append("[grey],[/]");
                    sb.AppendLine();
                    idx++;
                }
                Indent(sb, indent); sb.Append("[grey]][/]");
                break;

            case JsonValueKind.String:
                sb.Append("[orange1]\"").Append(Markup.Escape(el.GetString() ?? string.Empty)).Append("\"[/]");
                break;

            case JsonValueKind.Number:
                sb.Append("[orchid]").Append(Markup.Escape(el.GetRawText())).Append("[/]");
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
                sb.Append("[deepskyblue2]").Append(el.GetRawText().ToLowerInvariant()).Append("[/]");
                break;

            case JsonValueKind.Null:
                sb.Append("[deepskyblue2]null[/]");
                break;

            default:
                sb.Append(Markup.Escape(el.GetRawText()));
                break;
        }
    }

    private static void Indent(StringBuilder sb, int indent)
    {
        if (indent <= 0) return;
        sb.Append(' ', indent * 2);
    }
}
