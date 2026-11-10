using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using ConsoleMonster.Layout;
using ConsoleMonster.Styling;
using ConsoleMonster.Text;
using ConsoleMonster.Widgets;
using PowerForge;
using PowerForge.ConsoleUi;

namespace PSPublishModule;

public sealed partial class InvokeProjectBuildCommand
{
    private void CompleteResult(ProjectBuildResult result)
    {
        if (!result.Success && !ExitCode.IsPresent)
            WriteError(new ErrorRecord(
                new InvalidOperationException(result.ErrorMessage ?? "Project build failed."),
                "InvokeProjectBuildFailed",
                ErrorCategory.InvalidOperation,
                ConfigPath));
        WriteObject(result);
        if (ExitCode.IsPresent)
            Host.SetShouldExit(result.Success ? 0 : 1);
    }

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
        var list = new KeyValueList();
        foreach (var row in display.Rows)
            list.Add(row.Label, Markup.Escape(row.Value));

        ConsoleOutput.Write(new Panel(list, Markup.Escape(display.Title)) { Verdict = true, Status = StatusKind.Pass });
    }
}
