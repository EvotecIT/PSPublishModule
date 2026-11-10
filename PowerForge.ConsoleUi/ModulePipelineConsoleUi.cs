using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using ConsoleMonster.Layout;
using ConsoleMonster.Text;
using ConsoleMonster.Widgets;
using PowerForge;

namespace PowerForge.ConsoleUi;

internal static class ModulePipelineConsoleUi
{
    public static bool ShouldUseInteractiveView(
        bool isVerbose,
        bool outputJson = false,
        bool quiet = false,
        bool noColor = false,
        ConsoleView view = ConsoleView.Standard)
    {
        if (isVerbose || outputJson || quiet || noColor) return false;
        if (Console.IsOutputRedirected || Console.IsErrorRedirected) return false;
        if (ConsoleEnvironment.IsCI) return false;

        var live = ConsoleOutput.CanRenderLive;
        var resolved = view == ConsoleView.Auto ? (live ? ConsoleView.Standard : ConsoleView.Ansi) : view;
        return resolved == ConsoleView.Standard && live;
    }

    public static ModulePipelineResult RunInteractive(
        ModulePipelineRunner runner,
        ModulePipelineSpec spec,
        ModulePipelinePlan plan,
        string? configLabel)
    {
        if (runner is null) throw new ArgumentNullException(nameof(runner));
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        return RunInteractive(new ProgressView(), plan, configLabel, progress => runner.Run(spec, plan, progress));
    }

    internal static ModulePipelineResult RunInteractive(
        ProgressView view,
        ModulePipelinePlan plan,
        string? configLabel,
        Func<IModulePipelineProgressReporter, ModulePipelineResult> run)
    {
        if (view is null) throw new ArgumentNullException(nameof(view));
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (run is null) throw new ArgumentNullException(nameof(run));

        var items = ModulePipelineProgressItemFactory.Create(plan);
        WriteHeader(plan, configLabel, items.Length);
        foreach (var item in items)
            view.Step(item.Key, item.Title, item.Kind, item.Target);

        Exception? failure = null;
        ModulePipelineResult? result = null;
        view.Run(() =>
        {
            var reporter = new Reporter(view, items.ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase));
            try
            {
                result = run(reporter);
                view.FinishRemaining(success: true);
            }
            catch (Exception ex)
            {
                failure = ex;
                view.FinishRemaining(success: false);
            }
        });

        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();

        return result!;
    }

    private static void WriteHeader(ModulePipelinePlan plan, string? configLabel, int stepCount)
    {
        var versionText = BuildServices.FormatVersionWithPreRelease(plan.ResolvedVersion, plan.PreRelease);
        var packageLaneCount = (plan.ProjectBuilds?.Length ?? 0) + (plan.PackageBuilds?.Length ?? 0);
        var coordinatedRelease = packageLaneCount > 0 || plan.Release is not null;
        var title = coordinatedRelease
            ? $"PowerForge - Coordinated release - {plan.ModuleName} {versionText}"
            : $"PowerForge - {plan.ModuleName} {versionText}";

        var info = new KeyValueList();
        void Add(string label, string value) => info.Add(label, Markup.Escape(value));
        void AddMarkup(string label, string markup) => info.Add(label, markup);

        Add("Config", string.IsNullOrWhiteSpace(configLabel) ? "(discovered)" : configLabel!);
        Add("Project", plan.ProjectRoot);
        if (coordinatedRelease)
        {
            Add("Scope", packageLaneCount > 0
                ? $"PowerShell module + {packageLaneCount} NuGet package {(packageLaneCount == 1 ? "lane" : "lanes")}"
                : "PowerShell module");
            var buildOrder = plan.Release?.Configuration?.BuildOrder;
            if (buildOrder is { Length: > 0 }) Add("Build order", FormatReleaseOrder(buildOrder));
            var publishOrder = plan.Release?.Configuration?.PublishOrder;
            if (publishOrder is { Length: > 0 }) Add("Publish order", FormatReleaseOrder(publishOrder));
        }

        Add("Staging", string.IsNullOrWhiteSpace(plan.BuildSpec.StagingPath) ? "(temp)" : plan.BuildSpec.StagingPath!);
        Add("Frameworks", plan.BuildSpec.Frameworks is { Length: > 0 } ? string.Join(", ", plan.BuildSpec.Frameworks) : "(auto)");
        AddMarkup("Docs", plan.DocumentationBuild?.Enable == true ? "[pass]Enabled[/]" : "[muted]Disabled[/]");

        var validations = new List<string>();
        if (plan.FileConsistencySettings?.Enable == true) validations.Add("File consistency");
        if (plan.CompatibilitySettings?.Enable == true) validations.Add("Compatibility");
        if (plan.ValidationSettings?.Enable == true) validations.Add("Module validation");
        if (validations.Count == 0) AddMarkup("Validation", "[muted]Disabled[/]");
        else Add("Validation", string.Join(", ", validations));

        Add(coordinatedRelease ? "Module artefacts" : "Artefacts", (plan.Artefacts?.Length ?? 0).ToString());
        Add(coordinatedRelease ? "Module publishes" : "Publishes", (plan.Publishes?.Length ?? 0).ToString());
        if (plan.InstallEnabled) Add("Install", $"{plan.InstallStrategy}, keep {plan.InstallKeepVersions}");
        else AddMarkup("Install", "[muted]Disabled[/]");
        Add(coordinatedRelease ? "Coordinated steps" : "Steps", stepCount.ToString());

        ConsoleOutput.Write(new Rows(new Rule($"[bold accent]{Markup.Escape(title)}[/]"), info, new TextBlock(string.Empty)));
    }

    private static string FormatReleaseOrder(IEnumerable<string> values)
        => string.Join(
            " → ",
            values.Select(value => string.Equals(value, "PowerShellGallery", StringComparison.OrdinalIgnoreCase)
                ? "PowerShell Gallery"
                : value));

    // Engine steps are the ledger rows; nested package and signing work is listed under the step that runs it.
    private sealed class Reporter(ProgressView view, IReadOnlyDictionary<string, PowerForgeReleaseProgressItem> steps)
        : IModulePipelineProgressReporterV4
    {
        public void StepStarting(ModulePipelineStep step) => Update(step, ProgressState.Started);

        public void StepCompleted(ModulePipelineStep step) => Update(step, ProgressState.Completed);

        public void StepFailed(ModulePipelineStep step, Exception error)
            => Update(step, ProgressState.Failed, error?.GetBaseException().Message);

        public void StepSkipped(ModulePipelineStep step) => Update(step, ProgressState.Skipped);

        public void StepProgress(ModulePipelineStep step, double value, double maximum, string? detail = null)
        {
            if (step is null || !steps.ContainsKey(step.Key)) return;
            ProgressLedger.Apply(view, step.Key, ProgressState.Started, detail, Math.Max(0, value), Math.Max(0, maximum));
        }

        public void ItemsPlanned(PowerForgeReleaseProgressPhase phase, IReadOnlyList<PowerForgeReleaseProgressItem> items)
        {
            if (items is null) return;
            foreach (var item in items.Where(static item => item is not null))
                Plan(item);
        }

        public void ItemUpdated(PowerForgeReleaseProgressItem item, PowerForgeReleaseProgressItemState state, string? detail = null)
        {
            if (item is null) return;
            var key = Plan(item);
            ProgressLedger.Apply(view, key, ProgressLedger.From(state), detail, item.ProgressValue, item.ProgressMaximum, item.Duration);
        }

        private void Update(ModulePipelineStep step, ProgressState state, string? detail = null)
        {
            if (step is null || !steps.ContainsKey(step.Key)) return;
            ProgressLedger.Apply(view, step.Key, state, detail);
        }

        // Nested keys start with the key of the step that owns them ("<step>:project:PackageSigning:<package>").
        private string Plan(PowerForgeReleaseProgressItem item)
        {
            if (steps.ContainsKey(item.Key)) return item.Key;
            if (view.Contains(item.Key)) return item.Key;

            var owner = steps.Keys
                .Where(key => item.Key.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(key => key.Length)
                .FirstOrDefault()
                ?? view.RunningStep;
            var label = ProgressLedger.Label(item, item.CounterLabel ?? CounterLabel(item));
            if (owner is null) view.Step(item.Key, label, item.Kind, item.Target);
            else view.Item(owner, item.Key, label, item.Kind, item.Target);
            return item.Key;
        }

        private static string? CounterLabel(PowerForgeReleaseProgressItem item)
            => item.Kind switch
            {
                nameof(ProjectBuildProgressPhase.PackageSigning) => "Package",
                nameof(ProjectBuildProgressPhase.NuGetPublish) => "Package",
                "GitHubAsset" => "Asset",
                _ => null
            };
    }
}
