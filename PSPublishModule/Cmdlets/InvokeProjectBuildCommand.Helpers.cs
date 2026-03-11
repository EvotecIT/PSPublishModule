using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using PowerForge;
using Spectre.Console;

namespace PSPublishModule;

public sealed partial class InvokeProjectBuildCommand
{
    private string ResolveConfigPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new PSArgumentException("ConfigPath is required.");

        try
        {
            return SessionState.Path.GetUnresolvedProviderPathFromPSPath(path);
        }
        catch
        {
            return Path.GetFullPath(path);
        }
    }

    private static void WriteGitHubSummary(
        bool perProject,
        string? tag,
        string? releaseUrl,
        int assetsCount,
        IReadOnlyList<ProjectBuildGitHubResult> results)
    {
        var unicode = AnsiConsole.Profile.Capabilities.Unicode;
        var border = unicode ? TableBorder.Rounded : TableBorder.Simple;
        var title = unicode ? "✅ GitHub Summary" : "GitHub Summary";
        AnsiConsole.Write(new Rule($"[green]{title}[/]").LeftJustified());

        var table = new Table()
            .Border(border)
            .AddColumn(new TableColumn("Item").NoWrap())
            .AddColumn(new TableColumn("Value"));

        if (!perProject)
        {
            table.AddRow("Mode", "Single");
            table.AddRow("Tag", Markup.Escape(tag ?? string.Empty));
            table.AddRow("Assets", assetsCount.ToString());
            if (!string.IsNullOrWhiteSpace(releaseUrl))
                table.AddRow("Release", Markup.Escape(releaseUrl!));
        }
        else
        {
            var ok = results.Count(result => result.Success);
            var fail = results.Count(result => !result.Success);
            table.AddRow("Mode", "PerProject");
            table.AddRow("Projects", results.Count.ToString());
            table.AddRow("Succeeded", ok.ToString());
            table.AddRow("Failed", fail.ToString());
        }

        AnsiConsole.Write(table);
    }
}
