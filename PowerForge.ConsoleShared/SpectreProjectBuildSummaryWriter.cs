using System;
using PowerForge;
using Spectre.Console;

namespace PowerForge.ConsoleShared;

internal static class SpectreProjectBuildSummaryWriter
{
    public static void Write(DotNetRepositoryReleaseDisplayModel display)
    {
        if (display is null) return;

        static string Esc(string? value) => Markup.Escape(value ?? string.Empty);
        static string ColorTag(ConsoleColor? color)
            => color switch
            {
                ConsoleColor.Green => "green",
                ConsoleColor.Gray => "grey",
                ConsoleColor.Red => "red",
                ConsoleColor.Yellow => "yellow",
                _ => "white"
            };

        var unicode = ConsoleEncoding.ShouldRenderUnicode(AnsiConsole.Profile.Capabilities.Unicode);
        var border = unicode ? TableBorder.Rounded : TableBorder.Simple;
        var icon = unicode ? "✅" : "*";

        AnsiConsole.Write(new Rule($"[green]{icon} {Esc(display.Title)}[/]").LeftJustified());

        var table = new Table()
            .Border(border)
            .AddColumn(new TableColumn("Project").NoWrap())
            .AddColumn(new TableColumn("Packable").NoWrap())
            .AddColumn(new TableColumn("Version").NoWrap())
            .AddColumn(new TableColumn("Packages").NoWrap())
            .AddColumn(new TableColumn("Status").NoWrap())
            .AddColumn(new TableColumn("Error"));

        foreach (var project in display.Projects)
        {
            table.AddRow(
                Esc(project.ProjectName),
                project.Packable,
                Esc(project.VersionDisplay),
                project.PackageCount,
                $"[{ColorTag(project.StatusColor)}]{Esc(project.StatusText)}[/]",
                Esc(project.ErrorPreview));
        }

        AnsiConsole.Write(table);

        var totals = new Table()
            .Border(border)
            .AddColumn(new TableColumn("Item").NoWrap())
            .AddColumn(new TableColumn("Value"));

        foreach (var row in display.Totals)
            totals.AddRow(Esc(row.Label), Esc(row.Value));

        AnsiConsole.Write(totals);
    }
}
