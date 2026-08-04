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
        => RunInteractive(AnsiConsole.Console, plan, run);

    internal static ProjectBuildWorkflowResult RunInteractive(
        IAnsiConsole console,
        ProjectBuildConsolePlan plan,
        Func<IProjectBuildProgressReporter, ProjectBuildWorkflowResult> run)
    {
        if (console is null) throw new ArgumentNullException(nameof(console));
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (run is null) throw new ArgumentNullException(nameof(run));

        WriteHeader(console, plan);
        var phases = ResolvePhases(plan);
        ProjectBuildWorkflowResult? result = null;
        Exception? failure = null;
        SpectreProjectBuildProgressReporter? reporter = null;

        SpectreProgressDisplay.Run(
            console,
            SpectreBuildProgressColumns.CreateStandard(),
            context =>
            {
                var tasks = phases.ToDictionary(
                    phase => phase,
                    phase => context.AddTask(BuildPendingLabel(phase), maxValue: 100, autoStart: false));
                reporter = new SpectreProjectBuildProgressReporter(context, tasks);

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

        reporter?.WriteLedger(console);
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

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

    private static void WriteHeader(IAnsiConsole console, ProjectBuildConsolePlan plan)
    {
        static string Esc(string? value) => Markup.Escape(value ?? string.Empty);
        var unicode = ConsoleEncoding.ShouldRenderUnicode(console.Profile.Capabilities.Unicode);
        var title = unicode ? "🛠️ PowerForge • Project build" : "PowerForge • Project build";
        console.Write(new Rule($"[yellow bold underline]{Esc(title)}[/]") { Justification = Justify.Left });

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
        console.Write(table);
        console.WriteLine();
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

    private sealed class SpectreProjectBuildProgressReporter : IProjectBuildProgressReporterV2
    {
        private readonly ProgressContext _context;
        private readonly IReadOnlyDictionary<ProjectBuildProgressPhase, ProgressTask> _tasks;
        private readonly HashSet<ProjectBuildProgressPhase> _failed = new();
        private readonly Dictionary<ProjectBuildProgressPhase, (int Completed, int Total)> _phaseCounts = new();
        private readonly SpectreProgressLedger _ledger;

        public SpectreProjectBuildProgressReporter(
            ProgressContext context,
            IReadOnlyDictionary<ProjectBuildProgressPhase, ProgressTask> tasks)
        {
            _context = context;
            _tasks = tasks;
            _ledger = new SpectreProgressLedger(context);
        }

        public void PhaseStarted(ProjectBuildProgressPhase phase, int totalItems, string? detail = null)
        {
            if (!_tasks.TryGetValue(phase, out var task)) return;
            RememberCount(phase, 0, totalItems);
            if (!task.IsStarted) task.StartTask();
            task.Value = 0;
            task.Description = BuildLabel(phase, detail, 0, totalItems, "cyan");
        }

        public void PhaseUpdated(ProjectBuildProgressPhase phase, int completedItems, int totalItems, string? detail = null)
        {
            if (!_tasks.TryGetValue(phase, out var task)) return;
            RememberCount(phase, completedItems, totalItems);
            if (!task.IsStarted) task.StartTask();
            var total = Math.Max(1, totalItems);
            task.Value = Math.Min(100, Math.Max(0, completedItems) * 100d / total);
            task.Description = BuildLabel(phase, detail, completedItems, totalItems, "cyan");
        }

        public void PhaseCompleted(ProjectBuildProgressPhase phase, string? detail = null)
        {
            if (!_tasks.TryGetValue(phase, out var task)) return;
            var count = GetTerminalCount(phase, completed: true);
            if (!task.IsStarted) task.StartTask();
            task.Description = BuildLabel(phase, detail, count.Completed, count.Total, "green", "✓");
            task.Value = 100;
            task.StopTask();
        }

        public void PhaseFailed(ProjectBuildProgressPhase phase, string? detail = null)
        {
            if (!_tasks.TryGetValue(phase, out var task)) return;
            _failed.Add(phase);
            var count = GetTerminalCount(phase, completed: false);
            if (!task.IsStarted) task.StartTask();
            task.Description = BuildLabel(phase, detail, count.Completed, count.Total, "red", "x");
            task.Value = 100;
            task.StopTask();
        }

        public void ItemsPlanned(
            ProjectBuildProgressPhase phase,
            IReadOnlyList<ProjectBuildProgressItem> items)
            => _ledger.Plan(items.Select(item => ToLedgerItem(item)));

        public void ItemUpdated(
            ProjectBuildProgressItem item,
            ProjectBuildProgressItemState state,
            string? detail = null)
            => _ledger.Update(
                ToLedgerItem(item),
                state switch
                {
                    ProjectBuildProgressItemState.Started => SpectreProgressLedgerState.Started,
                    ProjectBuildProgressItemState.Completed => SpectreProgressLedgerState.Completed,
                    ProjectBuildProgressItemState.Failed => SpectreProgressLedgerState.Failed,
                    ProjectBuildProgressItemState.Skipped => SpectreProgressLedgerState.Skipped,
                    _ => SpectreProgressLedgerState.Planned
                },
                detail);

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

            _ledger.FinishRemaining(success);
            _ledger.ClearLiveTasks();
            _context.Refresh();
        }

        public void WriteLedger(IAnsiConsole console)
            => SpectreProgressLedger.WriteLedger(
                console,
                _ledger.GetSnapshots(),
                "Project build details");

        private void RememberCount(
            ProjectBuildProgressPhase phase,
            int completed,
            int total)
        {
            if (total <= 0) {
                return;
            }

            _phaseCounts[phase] = (Math.Min(Math.Max(0, completed), total), total);
        }

        private (int? Completed, int? Total) GetTerminalCount(
            ProjectBuildProgressPhase phase,
            bool completed)
        {
            if (!_phaseCounts.TryGetValue(phase, out var count)) {
                return (null, null);
            }

            return completed
                ? (count.Total, count.Total)
                : (count.Completed, count.Total);
        }

        private static SpectreProgressLedgerItem ToLedgerItem(ProjectBuildProgressItem item)
            => new()
            {
                Key = $"{item.Phase}:{item.Key}",
                GroupKey = item.Phase.ToString(),
                GroupTitle = GetPhaseName(item.Phase),
                GroupOrder = (int)item.Phase,
                Title = item.Title,
                Kind = item.Kind,
                CounterLabel = ProgressCounterFormatter.GetProjectBuildScope(item.Phase),
                Position = item.Position,
                Total = item.Total,
                Duration = item.Duration
            };

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
                ? ProgressCounterFormatter.Format(
                    ProgressCounterFormatter.GetProjectBuildScope(phase),
                    completed.GetValueOrDefault(),
                    total.GetValueOrDefault()) + " — "
                : string.Empty;
            var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" — {detail}";
            return $"[{color}]{Markup.Escape(prefix + count + GetPhaseName(phase) + suffix)}[/]";
        }
    }
}
