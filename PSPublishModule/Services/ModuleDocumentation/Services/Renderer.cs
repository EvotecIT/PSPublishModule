using System;
using System.IO;
using System.Linq;
using ConsoleMonster.Highlighting;
using ConsoleMonster.Layout;
using ConsoleMonster.Markdown;
using ConsoleMonster.Text;
using ConsoleMonster.Widgets;
using PowerForge;
using PowerForge.ConsoleUi;

namespace PSPublishModule;

/// <summary>
/// Renders Markdown documents to the console with ConsoleMonster.
/// </summary>
internal sealed class Renderer
{
    // MarkdownView labels unlabelled fences "text" and gives them no highlighter.
    private const string UnlabelledFence = "text";

    private readonly JsonRendererPreference _jsonPreference;
    private readonly string? _defaultLanguage;

    public Renderer(JsonRendererPreference jsonPreference = JsonRendererPreference.Auto, string? defaultLanguage = null)
    {
        _jsonPreference = jsonPreference;
        _defaultLanguage = string.IsNullOrWhiteSpace(defaultLanguage) ? null : defaultLanguage!.Trim().ToLowerInvariant();
    }

    public void ShowFile(string title, string path, bool raw)
    {
        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            ConsoleOutput.WriteMarkupLine($"[fail]Failed to read '{Markup.Escape(path)}': {Markup.Escape(ex.Message)}[/]");
            return;
        }

        ShowContent(title, content, raw);
    }

    public void ShowContent(string title, string markdown, bool raw)
    {
        if (raw)
        {
            ConsoleOutput.WriteLine(markdown);
            return;
        }

        ConsoleOutput.Write(new Rows(new Rule(Markup.Escape(title ?? string.Empty)), Build(markdown ?? string.Empty)) { Gap = 1 });
    }

    private IWidget Build(string markdown)
    {
        MarkdownView view;
        try
        {
            view = MarkdownConsole.Parse(markdown);
        }
        catch (NotSupportedException ex)
        {
            // An unsupported block should not hide the document: show it as written.
            return new Rows(
                new TextBlock($"[warn]Showing the source because it could not be rendered: {Markup.Escape(ex.Message)}[/]"),
                TextBlock.Plain(markdown)) { Gap = 1 };
        }

        return new Rows(view.Children.Select(Adjust).ToArray()) { Gap = 1 };
    }

    private IWidget Adjust(IWidget block)
    {
        if (block is not CodeView code)
            return block;

        if (code.Highlighter is null && _defaultLanguage is not null && code.Title == UnlabelledFence)
            return Code(code.Text, _defaultLanguage, Highlighters.Get(_defaultLanguage));

        if (_jsonPreference == JsonRendererPreference.System && code.Highlighter is JsonHighlighter && TryFormatJson(code.Text, out var formatted))
            return Code(formatted, code.Title, highlighter: null);

        return block;
    }

    private static CodeView Code(string text, string? title, ITextHighlighter? highlighter)
        => new(text, highlighter) { Title = title, Wrap = true, MaxLines = int.MaxValue };

    private static bool TryFormatJson(string content, out string formatted)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(content);
            formatted = System.Text.Json.JsonSerializer.Serialize(
                document.RootElement,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            formatted = content;
            return false;
        }
    }
}
