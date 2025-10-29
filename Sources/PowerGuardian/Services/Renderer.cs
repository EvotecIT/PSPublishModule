using System;
using System.IO;
using Spectre.Console;
using Spectre.Console.Json;
using OfficeIMO.Markdown;
using PowerGuardian.Rendering;
using System.Linq;

namespace PowerGuardian;

internal sealed class Renderer
{
    private readonly JsonRendererPreference _jsonPreference;
    private readonly string? _defaultLanguage;

    public Renderer(JsonRendererPreference jsonPreference = JsonRendererPreference.Auto, string? defaultLanguage = null)
    {
        _jsonPreference = jsonPreference;
        _defaultLanguage = string.IsNullOrWhiteSpace(defaultLanguage) ? null : defaultLanguage.Trim().ToLowerInvariant();
    }
    public void WriteHeading(string title)
    {
        var rule = new Rule(Markup.Escape(title)) { Justification = Justify.Left };
        AnsiConsole.Write(rule);
    }

    public void ShowFile(string title, string path, bool raw)
    {
        if (raw)
        {
            Console.WriteLine(File.ReadAllText(path));
            return;
        }
        WriteHeading(title);
        try
        {
            var content = File.ReadAllText(path);
            var doc = MarkdownReader.Parse(content, new MarkdownReaderOptions { Tables = true, Callouts = true, FrontMatter = true });
            RenderMarkdownDoc(doc);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine("[red]Failed to read '{0}': {1}[/]", Markup.Escape(path), Markup.Escape(ex.Message));
        }
    }

    private void RenderMarkdownDoc(MarkdownDoc doc)
    {
        foreach (var block in doc.Blocks)
        {
            switch (block)
            {
                case HeadingBlock h:
                    RenderHeading(h);
                    break;
                case ParagraphBlock p:
                    AnsiConsole.MarkupLine(RenderInlines(p.Inlines));
                    break;
                case CodeBlock c:
                    RenderCodeBlock(c);
                    break;
                case QuoteBlock q:
                    RenderQuote(q);
                    break;
                case UnorderedListBlock ul:
                    RenderUnorderedList(ul);
                    break;
                case OrderedListBlock ol:
                    RenderOrderedList(ol);
                    break;
                case TableBlock tb:
                    RenderTable(tb);
                    break;
                case HorizontalRuleBlock:
                    AnsiConsole.Write(new Rule());
                    break;
                default:
                    // Fallback to markdown text for unknown block types
                    var raw = block.RenderMarkdown();
                    if (!string.IsNullOrWhiteSpace(raw))
                        AnsiConsole.MarkupLine(Markup.Escape(raw));
                    break;
            }
        }
    }

    private void RenderHeading(HeadingBlock h)
    {
        var text = Markup.Escape(h.Text);
        string style = h.Level switch
        {
            1 => "bold underline dodgerblue1",
            2 => "bold yellow",
            3 => "bold springgreen1",
            4 => "deepskyblue2",
            5 => "orchid",
            _ => "grey"
        };
        AnsiConsole.MarkupLine($"[{style}]{text}[/]");
    }

    private void RenderCodeBlock(CodeBlock c)
    {
        var lang = (c.Language ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(lang) && !string.IsNullOrEmpty(_defaultLanguage))
            lang = _defaultLanguage;
        var header = string.IsNullOrWhiteSpace(lang) ? "code" : lang;
        if ((lang == "json" || lang == "jsonc") && _jsonPreference != JsonRendererPreference.System)
        {
            try
            {
                var jt = new JsonText(c.Content);
                var panel = new Panel(jt)
                {
                    Border = BoxBorder.Rounded,
                    Header = new PanelHeader(header, Justify.Left)
                };
                AnsiConsole.Write(panel);
            }
            catch
            {
                // Fallback to tokenizer pipeline if JSON parse fails
                var codeMarkup = new HighlighterPipeline().Highlight(c.Content, lang);
                var p = new Panel(new Markup(codeMarkup)) { Border = BoxBorder.Rounded, Header = new PanelHeader(header, Justify.Left) };
                AnsiConsole.Write(p);
            }
        }
        else
        {
            // Tokenizer pipeline (System/Text.Json-based highlighter or generic)
            var codeMarkup = new HighlighterPipeline().Highlight(c.Content, lang);
            var panel = new Panel(new Markup(codeMarkup))
            {
                Border = BoxBorder.Rounded,
                Header = new PanelHeader(header, Justify.Left)
            };
            AnsiConsole.Write(panel);
        }
        if (!string.IsNullOrWhiteSpace(c.Caption))
        {
            AnsiConsole.MarkupLine($"[dim]{Markup.Escape(c.Caption)}[/]");
        }
    }

    private void RenderQuote(QuoteBlock q)
    {
        // Quote may have multiple lines; render as dim indented panel
        var text = string.Join("\n", q.Lines.Select(l => Markup.Escape(l)));
        var panel = new Panel(new Markup(text))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Grey),
        };
        AnsiConsole.Write(panel);
    }

    private void RenderUnorderedList(UnorderedListBlock ul)
    {
        foreach (var item in ul.Items)
        {
            var indent = new string(' ', item.Level * 2);
            var bullet = item.IsTask ? (item.Checked ? "[green]✓[/]" : "[grey]-[/]") : "•";
            var line = RenderInlines(item.Content);
            AnsiConsole.MarkupLine($"{indent}{bullet} {line}");
        }
    }

    private void RenderOrderedList(OrderedListBlock ol)
    {
        int idx = 1;
        foreach (var item in ol.Items)
        {
            var indent = new string(' ', item.Level * 2);
            var line = RenderInlines(item.Content);
            AnsiConsole.MarkupLine($"{indent}[bold]{idx}.[/] {line}");
            idx++;
        }
    }

    private void RenderTable(TableBlock tb)
    {
        var table = new Spectre.Console.Table().Border(Spectre.Console.TableBorder.Rounded).BorderColor(Color.Grey);
        if (tb.Headers.Count > 0)
        {
            foreach (var h in tb.Headers)
            {
                table.AddColumn(new Spectre.Console.TableColumn(Markup.Escape(h)));
            }
        }
        foreach (var row in tb.Rows)
        {
            var cells = row.Select(c => new Markup(RenderInlineCell(c))).ToArray();
            table.AddRow(cells);
        }
        AnsiConsole.Write(table);
    }

    private static string RenderInlineCell(string cell)
    {
        if (string.IsNullOrEmpty(cell)) return string.Empty;
        try
        {
            var inline = MarkdownReader.ParseInlineText(cell);
            // reuse RenderInlines logic by creating a new Renderer temporarily
            var r = new Renderer();
            return r.RenderInlines(inline);
        }
        catch
        {
            return Markup.Escape(cell);
        }
    }

    public string RenderInlines(InlineSequence inlines)
    {
        var parts = inlines.Items.Select(RenderInline).Where(s => s != null).ToArray();
        return string.Join(" ", parts);
    }

    private string RenderInline(object node)
    {
        switch (node)
        {
            case TextRun t:
                return Markup.Escape(t.Text);
            case BoldInline b:
                return $"[bold]{Markup.Escape(b.Text)}[/]";
            case BoldItalicInline bi:
                return $"[bold italic]{Markup.Escape(bi.Text)}[/]";
            case ItalicInline it:
                return $"[italic]{Markup.Escape(it.Text)}[/]";
            case UnderlineInline un:
                return $"[underline]{Markup.Escape(un.Text)}[/]";
            case StrikethroughInline st:
                return $"[strike]{Markup.Escape(st.Text)}[/]";
            case CodeSpanInline cs:
                return $"[grey]{Markup.Escape(cs.Text)}[/]";
            case LinkInline l:
                var text = string.IsNullOrEmpty(l.Text) ? l.Url : l.Text;
                return $"[link={Markup.Escape(l.Url)}]{Markup.Escape(text)}[/]";
            case ImageInline im:
                return $"[dim]{Markup.Escape(im.Alt ?? "image")}[/]";
            case ImageLinkInline il:
                var alt = string.IsNullOrEmpty(il.Alt) ? il.ImageUrl : il.Alt;
                return $"[link={Markup.Escape(il.LinkUrl)}]{Markup.Escape(alt)}[/]";
            case HardBreakInline:
                return "\n";
            case FootnoteRefInline fn:
                return $"[dim][^ {Markup.Escape(fn.Label)}][/]";
            default:
                return null;
        }
    }
}
