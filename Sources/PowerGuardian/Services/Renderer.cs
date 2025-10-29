using System;
using System.IO;
using Spectre.Console;

namespace PowerGuardian;

internal sealed class Renderer
{
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
            var panel = new Panel(new Markup(Markup.Escape(content)))
            {
                Border = BoxBorder.Rounded,
                Header = new PanelHeader(Path.GetFileName(path), Justify.Center)
            };
            AnsiConsole.Write(panel);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine("[red]Failed to read '{0}': {1}[/]", Markup.Escape(path), Markup.Escape(ex.Message));
        }
    }
}

