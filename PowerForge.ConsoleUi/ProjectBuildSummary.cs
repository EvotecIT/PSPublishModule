using System;
using System.Linq;
using ConsoleMonster.Frames;
using ConsoleMonster.Text;
using ConsoleMonster.Widgets;
using PowerForge;

namespace PowerForge.ConsoleUi;

/// <summary>The project build summary: projects that failed or warned need attention; every project is listed below.</summary>
internal static class ProjectBuildSummary
{
    public static void Write(DotNetRepositoryReleaseDisplayModel display)
    {
        if (display is null) return;

        var summary = new SummaryFrame(display.Title, display.Success, display.IsPlan ? "planned" : null);
        var failing = 0;
        var warning = 0;
        foreach (var project in display.Projects)
        {
            var state = State(project.StatusColor);
            if (state == HealthState.Failing) failing++;
            if (state == HealthState.Warning) warning++;
            if (state != HealthState.Healthy)
                summary.Attention.Add(state, string.IsNullOrWhiteSpace(project.ErrorPreview) ? project.StatusText : project.ErrorPreview, project.ProjectName);
        }

        if (display.Projects.Count > 0)
            summary.Meter(Meter.Composition("Projects", display.Projects.Count - failing - warning, warning, failing));
        foreach (var row in display.Totals) summary.Line(row.Label, row.Value);

        var table = new Table()
            .AddColumn("Project")
            .AddColumn("Packable")
            .AddColumn("Version")
            .AddColumn(display.IsPlan ? "Planned packages" : "Packages")
            .AddColumn("Duration", ConsoleMonster.Layout.HorizontalAlignment.Right)
            .AddColumn("Status")
            .AddColumn("Error");
        foreach (var project in display.Projects)
        {
            table.AddRow(
                Markup.Escape(project.ProjectName),
                Markup.Escape(project.Packable),
                Markup.Escape(project.VersionDisplay),
                Markup.Escape(project.PackageCount),
                Markup.Escape(project.Duration),
                $"[{Role(State(project.StatusColor))}]{Markup.Escape(project.StatusText)}[/]",
                Markup.Escape(project.ErrorPreview));
        }
        summary.Detail("Projects", table);
        summary.Write();
    }

    private static HealthState State(ConsoleColor? color) => color switch
    {
        ConsoleColor.Red => HealthState.Failing,
        ConsoleColor.Yellow => HealthState.Warning,
        _ => HealthState.Healthy
    };

    private static string Role(HealthState state) => state switch
    {
        HealthState.Failing => "fail",
        HealthState.Warning => "warn",
        _ => "pass"
    };
}
