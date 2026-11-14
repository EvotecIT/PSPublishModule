using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using ConsoleMonster.Layout;
using ConsoleMonster.Text;
using ConsoleMonster.Widgets;
using PowerForge;

namespace PowerForge.ConsoleUi;

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

internal static class ProjectBuildConsoleUi
{
    public static ProjectBuildWorkflowResult RunInteractive(
        ProjectBuildConsolePlan plan,
        Func<IProjectBuildProgressReporter, ProjectBuildWorkflowResult> run)
        => RunInteractive(new ProgressView(), plan, run);

    internal static ProjectBuildWorkflowResult RunInteractive(
        ProgressView view,
        ProjectBuildConsolePlan plan,
        Func<IProjectBuildProgressReporter, ProjectBuildWorkflowResult> run)
    {
        if (view is null) throw new ArgumentNullException(nameof(view));
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (run is null) throw new ArgumentNullException(nameof(run));

        WriteHeader(plan);
        foreach (var phase in ResolvePhases(plan))
            view.Step(phase.ToString(), GetPhaseName(phase), phase.ToString());

        ProjectBuildWorkflowResult? result = null;
        Exception? failure = null;
        view.Run(() =>
        {
            var reporter = new Reporter(view);
            try
            {
                result = run(reporter);
                view.FinishRemaining(result.Result.Success);
            }
            catch (Exception exception)
            {
                failure = exception;
                view.FinishRemaining(success: false);
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
        var actions = new List<string>();
        if (plan.UpdateVersions) actions.Add("versions");
        if (plan.Build) actions.Add("packages");
        if (plan.SignPackages) actions.Add("signing");
        if (plan.PublishNuGet) actions.Add("NuGet");
        if (plan.PublishGitHub) actions.Add("GitHub");

        var info = new KeyValueList();
        info.Add("Mode", plan.PlanOnly ? "[warn]Plan only[/]" : "[pass]Release execution[/]");
        info.Add("Config", Markup.Escape(plan.ConfigPath));
        info.Add("Root", Markup.Escape(plan.RootPath));
        info.Add("Actions", Markup.Escape(actions.Count == 0 ? "plan" : string.Join(" → ", actions)));
        if (!string.IsNullOrWhiteSpace(plan.StagingPath)) info.Add("Staging", Markup.Escape(plan.StagingPath!));
        if (!string.IsNullOrWhiteSpace(plan.OutputPath)) info.Add("Packages", Markup.Escape(plan.OutputPath!));
        if (!string.IsNullOrWhiteSpace(plan.PlanOutputPath)) info.Add("Plan file", Markup.Escape(plan.PlanOutputPath!));
        ConsoleOutput.Write(new Rows(new Rule("[bold accent]PowerForge - Project build[/]"), info, new TextBlock(string.Empty)));
    }

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

    // Phases are the ledger rows; projects and packages are listed under their phase.
    private sealed class Reporter(ProgressView view) : IProjectBuildProgressReporterV2
    {
        private readonly Dictionary<ProjectBuildProgressPhase, (int Completed, int Total)> _counts = new();

        public void PhaseStarted(ProjectBuildProgressPhase phase, int totalItems, string? detail = null)
        {
            Remember(phase, 0, totalItems);
            view.Start(phase.ToString(), WithCount(phase, detail));
        }

        public void PhaseUpdated(ProjectBuildProgressPhase phase, int completedItems, int totalItems, string? detail = null)
        {
            Remember(phase, completedItems, totalItems);
            view.Start(phase.ToString(), WithCount(phase, detail));
        }

        public void PhaseCompleted(ProjectBuildProgressPhase phase, string? detail = null)
        {
            if (_counts.TryGetValue(phase, out var count)) _counts[phase] = (count.Total, count.Total);
            view.Finish(phase.ToString(), ProgressState.Completed, WithCount(phase, detail));
        }

        public void PhaseFailed(ProjectBuildProgressPhase phase, string? detail = null)
            => view.Finish(phase.ToString(), ProgressState.Failed, WithCount(phase, detail));

        public void ItemsPlanned(ProjectBuildProgressPhase phase, IReadOnlyList<ProjectBuildProgressItem> items)
        {
            if (items is null) return;
            foreach (var item in items.Where(static item => item is not null))
                Plan(item);
        }

        public void ItemUpdated(ProjectBuildProgressItem item, ProjectBuildProgressItemState state, string? detail = null)
        {
            if (item is null) return;
            ProgressLedger.Apply(view, Plan(item), ProgressLedger.From(state), detail, duration: item.Duration);
        }

        private string Plan(ProjectBuildProgressItem item)
        {
            var key = $"{item.Phase}:{item.Key}";
            var phase = item.Phase.ToString();
            if (!view.Contains(phase))
            {
                // A phase that was not planned goes in at its place in build order.
                var before = view.StepKeys.Count(step => Enum.TryParse<ProjectBuildProgressPhase>(step, out var planned) && planned < item.Phase);
                view.Step(phase, GetPhaseName(item.Phase), phase, index: before);
            }
            view.Item(phase, key, ProgressLedger.Label(ProgressCounterFormatter.GetProjectBuildScope(item.Phase), item.Position, item.Total, item.Title), item.Kind);
            return key;
        }

        private void Remember(ProjectBuildProgressPhase phase, int completed, int total)
        {
            if (total > 0) _counts[phase] = (Math.Min(Math.Max(0, completed), total), total);
        }

        private string? WithCount(ProjectBuildProgressPhase phase, string? detail)
        {
            if (!_counts.TryGetValue(phase, out var count)) return detail;
            var counter = ProgressCounterFormatter.Format(ProgressCounterFormatter.GetProjectBuildScope(phase), count.Completed, count.Total);
            return string.IsNullOrWhiteSpace(detail) ? counter : counter + " - " + detail;
        }
    }
}
