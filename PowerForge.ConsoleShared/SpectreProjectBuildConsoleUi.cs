using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using PowerForge;
using Spectre.Console;

namespace PowerForge.ConsoleShared;

internal sealed class ProjectBuildConsolePlan
{
    public string ConfigPath { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public string? StagingPath { get; set; }
    public string? OutputPath { get; set; }
    public string? PlanOutputPath { get; set; }
    public bool PlanOnly { get; set; }
    public bool UpdateVersions { get; set; }
    public bool Build { get; set; }
    public bool SignPackages { get; set; }
    public bool PublishNuGet { get; set; }
    public bool PublishGitHub { get; set; }
}

internal static class SpectreProjectBuildConsoleUi
{
    public static ProjectBuildWorkflowResult RunInteractive(
        ProjectBuildConsolePlan plan,
        Func<IProjectBuildProgressReporter, ProjectBuildWorkflowResult> run)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (run is null) throw new ArgumentNullException(nameof(run));

        WriteHeader(plan);
        var phases = ResolvePhases(plan);
        ProjectBuildWorkflowResult? result = null;
        Exception? failure = null;

        AnsiConsole.Progress()
            .AutoRefresh(true)
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn { CompletedStyle = new Style(Color.Green), FinishedStyle = new Style(Color.Green) },
                new PercentageColumn(),
                new ElapsedTimeColumn(),
                new SpinnerColumn())
            .Start(context =>
            {
                var tasks = phases.ToDictionary(
                    phase => phase,
                    phase => context.AddTask(BuildPendingLabel(phase), maxValue: 100, autoStart: false));
                var reporter = new SpectreProjectBuildProgressReporter(tasks);

                try
                {
                    result = run(reporter);
                    reporter.FinishRemaining(result.Result.Success);
                }
                catch (Exception exception)
                {
                    failure = exception;
                    reporter.FinishRemaining(success: false);
                }
            });

        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();

        return result!;
    }

    private static ProjectBuildProgressPhase[] ResolvePhases(ProjectBuildConsolePlan plan)
    {
        var phases = new List<ProjectBuildProgressPhase> { ProjectBuildProgressPhase.Plan };
        if (plan.PlanOnly) return phases.ToArray();

        phases.Add(ProjectBuildProgressPhase.Versioning);
        if (plan.Build) phases.Add(ProjectBuildProgressPhase.PackageBuild);
        if (plan.SignPackages) phases.Add(ProjectBuildProgressPhase.PackageSigning);
        if (plan.PublishNuGet) phases.Add(ProjectBuildProgressPhase.NuGetPublish);
        if (plan.PublishGitHub) phases.Add(ProjectBuildProgressPhase.GitHubPublish);
        return phases.ToArray();
    }

    private static void WriteHeader(ProjectBuildConsolePlan plan)
    {
        static string Esc(string? value) => Markup.Escape(value ?? string.Empty);
        var unicode = ConsoleEncoding.ShouldRenderUnicode(AnsiConsole.Profile.Capabilities.Unicode);
        var title = unicode ? "🛠️ PowerForge • Project build" : "PowerForge • Project build";
        AnsiConsole.Write(new Rule($"[yellow bold underline]{Esc(title)}[/]") { Justification = Justify.Left });

        var actions = new List<string>();
        if (plan.UpdateVersions) actions.Add("versions");
        if (plan.Build) actions.Add("packages");
        if (plan.SignPackages) actions.Add("signing");
        if (plan.PublishNuGet) actions.Add("NuGet");
        if (plan.PublishGitHub) actions.Add("GitHub");

        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(new TableColumn("Item").NoWrap())
            .AddColumn(new TableColumn("Value"));
        table.AddRow("[grey]Mode[/]", plan.PlanOnly ? "[yellow]Plan only[/]" : "[green]Release execution[/]");
        table.AddRow("[grey]Config[/]", Esc(plan.ConfigPath));
        table.AddRow("[grey]Root[/]", Esc(plan.RootPath));
        table.AddRow("[grey]Actions[/]", Esc(actions.Count == 0 ? "plan" : string.Join(" → ", actions)));
        if (!string.IsNullOrWhiteSpace(plan.StagingPath)) table.AddRow("[grey]Staging[/]", Esc(plan.StagingPath));
        if (!string.IsNullOrWhiteSpace(plan.OutputPath)) table.AddRow("[grey]Packages[/]", Esc(plan.OutputPath));
        if (!string.IsNullOrWhiteSpace(plan.PlanOutputPath)) table.AddRow("[grey]Plan file[/]", Esc(plan.PlanOutputPath));
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private static string BuildPendingLabel(ProjectBuildProgressPhase phase)
        => $"[grey]{Markup.Escape(GetPhaseName(phase))} — pending[/]";

    private static string GetPhaseName(ProjectBuildProgressPhase phase)
        => phase switch
        {
            ProjectBuildProgressPhase.Plan => "Prepare build plan",
            ProjectBuildProgressPhase.Versioning => "Resolve versions",
            ProjectBuildProgressPhase.PackageBuild => "Build packages and archives",
            ProjectBuildProgressPhase.PackageSigning => "Sign NuGet packages",
            ProjectBuildProgressPhase.NuGetPublish => "Publish NuGet packages",
            ProjectBuildProgressPhase.GitHubPublish => "Publish GitHub release",
            _ => phase.ToString()
        };

    private sealed class SpectreProjectBuildProgressReporter : IProjectBuildProgressReporter
    {
        private readonly IReadOnlyDictionary<ProjectBuildProgressPhase, ProgressTask> _tasks;
        private readonly HashSet<ProjectBuildProgressPhase> _failed = new();

        public SpectreProjectBuildProgressReporter(IReadOnlyDictionary<ProjectBuildProgressPhase, ProgressTask> tasks)
            => _tasks = tasks;

        public void PhaseStarted(ProjectBuildProgressPhase phase, int totalItems, string? detail = null)
        {
            if (!_tasks.TryGetValue(phase, out var task)) return;
            if (!task.IsStarted) task.StartTask();
            task.Value = 0;
            task.Description = BuildLabel(phase, detail, 0, totalItems, "cyan");
        }

        public void PhaseUpdated(ProjectBuildProgressPhase phase, int completedItems, int totalItems, string? detail = null)
        {
            if (!_tasks.TryGetValue(phase, out var task)) return;
            if (!task.IsStarted) task.StartTask();
            var total = Math.Max(1, totalItems);
            task.Value = Math.Min(100, Math.Max(0, completedItems) * 100d / total);
            task.Description = BuildLabel(phase, detail, completedItems, totalItems, "cyan");
        }

        public void PhaseCompleted(ProjectBuildProgressPhase phase, string? detail = null)
        {
            if (!_tasks.TryGetValue(phase, out var task)) return;
            if (!task.IsStarted) task.StartTask();
            task.Description = BuildLabel(phase, detail, null, null, "green", "✓");
            task.Value = 100;
            task.StopTask();
        }

        public void PhaseFailed(ProjectBuildProgressPhase phase, string? detail = null)
        {
            if (!_tasks.TryGetValue(phase, out var task)) return;
            _failed.Add(phase);
            if (!task.IsStarted) task.StartTask();
            task.Description = BuildLabel(phase, detail, null, null, "red", "x");
            task.Value = 100;
            task.StopTask();
        }

        public void FinishRemaining(bool success)
        {
            foreach (var entry in _tasks)
            {
                var task = entry.Value;
                if (task.IsFinished || _failed.Contains(entry.Key)) continue;

                if (task.IsStarted && !success)
                {
                    PhaseFailed(entry.Key, "workflow stopped");
                    continue;
                }

                task.StartTask();
                task.Description = BuildLabel(entry.Key, success ? "not required" : "skipped after failure", null, null, "grey", "–");
                task.Value = 100;
                task.StopTask();
            }
        }

        private static string BuildLabel(
            ProjectBuildProgressPhase phase,
            string? detail,
            int? completed,
            int? total,
            string color,
            string? status = null)
        {
            var prefix = string.IsNullOrWhiteSpace(status) ? string.Empty : status + " ";
            var count = completed.HasValue && total.GetValueOrDefault() > 0
                ? $" {completed}/{total}"
                : string.Empty;
            var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" — {detail}";
            return $"[{color}]{Markup.Escape(prefix + GetPhaseName(phase) + count + suffix)}[/]";
        }
    }
}
