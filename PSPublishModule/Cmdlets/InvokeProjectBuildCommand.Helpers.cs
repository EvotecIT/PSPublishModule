using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using PowerForge;
using Spectre.Console;

namespace PSPublishModule;

public sealed partial class InvokeProjectBuildCommand
{
    private static bool? ResolveRequestedAction(IDictionary? boundParameters, string parameterName)
    {
        if (boundParameters?.Contains(parameterName) != true)
            return null;

        return ProjectBuildSupportService.IsTrue(boundParameters[parameterName]);
    }

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
        var summary = new ProjectBuildGitHubPublishSummary
        {
            PerProject = perProject,
            SummaryTag = tag,
            SummaryReleaseUrl = releaseUrl,
            SummaryAssetsCount = assetsCount
        };
        foreach (var result in results)
            summary.Results.Add(result);

        var display = new ProjectBuildGitHubDisplayService().CreateSummary(summary);
        var title = unicode ? $"✅ {display.Title}" : display.Title;
        AnsiConsole.Write(new Rule($"[green]{title}[/]").LeftJustified());

        var table = new Table()
            .Border(border)
            .AddColumn(new TableColumn("Item").NoWrap())
            .AddColumn(new TableColumn("Value"));

        foreach (var row in display.Rows)
            table.AddRow(row.Label, Markup.Escape(row.Value));

        AnsiConsole.Write(table);
    }
}
