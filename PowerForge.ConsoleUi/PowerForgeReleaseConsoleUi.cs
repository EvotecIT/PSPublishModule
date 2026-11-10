using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using ConsoleMonster.Layout;
using ConsoleMonster.Text;
using ConsoleMonster.Widgets;
using PowerForge;

namespace PowerForge.ConsoleUi;

internal static partial class PowerForgeReleaseConsoleUi
{
    public static PowerForgeReleaseResult RunInteractive(
        PowerForgeReleaseSpec spec,
        PowerForgeReleaseRequest request,
        Func<IPowerForgeReleaseProgressReporter, PowerForgeReleaseResult> run)
        => RunInteractive(new ProgressView(), spec, request, run);

    internal static PowerForgeReleaseResult RunInteractive(
        ProgressView view,
        PowerForgeReleaseSpec spec,
        PowerForgeReleaseRequest request,
        Func<IPowerForgeReleaseProgressReporter, PowerForgeReleaseResult> run)
    {
        if (view is null) throw new ArgumentNullException(nameof(view));
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (run is null) throw new ArgumentNullException(nameof(run));

        var phases = ResolvePhases(spec, request);
        var names = phases.ToDictionary(phase => phase, phase => GetPhaseName(phase, spec, request));
        WriteHeader(spec, request, phases, names);
        foreach (var phase in phases)
            view.Step(phase.ToString(), names[phase], phase.ToString());

        PowerForgeReleaseResult? result = null;
        Exception? failure = null;
        var reporter = new Reporter(view, phases);
        view.Run(() =>
        {
            try
            {
                result = run(reporter);
                reporter.FinishRemaining(result.Success);
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

    private static PowerForgeReleaseProgressPhase[] ResolvePhases(PowerForgeReleaseSpec spec, PowerForgeReleaseRequest request)
    {
        var hasTargetAwareSelection =
            request.Targets.Any(static target => !string.IsNullOrWhiteSpace(target)) &&
            (spec.Tools is not null || spec.AppleApps is not null);
        var runModule = !request.AppleOnly &&
                        !hasTargetAwareSelection &&
                        spec.Module is not null &&
                        (!request.PackagesOnly && !request.ToolsOnly || request.ModuleOnly);
        var moduleIncludesPackages = runModule &&
                                     spec.Module?.IncludesPackages == true &&
                                     !request.PlanOnly &&
                                     !request.ValidateOnly;
        var runPackages = !request.AppleOnly &&
                          (spec.Packages is not null || moduleIncludesPackages) &&
                          !request.ModuleOnly &&
                          !request.ToolsOnly;
        if (hasTargetAwareSelection)
            runPackages = false;
        var runTools = PowerForgeReleaseService.ShouldRunToolsForProgress(spec, request);
        var phases = new List<PowerForgeReleaseProgressPhase>();

        var coordinated = runModule && runPackages && spec.Module?.SynchronizeVersionWithPackages == true;
        if (coordinated)
        {
            phases.Add(PowerForgeReleaseProgressPhase.Versioning);
            if (runModule) phases.Add(PowerForgeReleaseProgressPhase.Module);
            if (runTools) phases.Add(PowerForgeReleaseProgressPhase.Tools);
            if (!request.PlanOnly && !request.ValidateOnly)
                phases.Add(PowerForgeReleaseProgressPhase.Packages);
        }
        else
        {
            if (runModule) phases.Add(PowerForgeReleaseProgressPhase.Module);
            if (runPackages) phases.Add(PowerForgeReleaseProgressPhase.Packages);
            if (runTools) phases.Add(PowerForgeReleaseProgressPhase.Tools);
        }
        var runAppleApps = PowerForgeReleaseService.ShouldRunAppleAppsForProgress(spec, request);
        if (runAppleApps)
            phases.Add(PowerForgeReleaseProgressPhase.AppleApps);
        var explicitAppleAction = request.AppleAction != PowerForgeAppleReleaseAction.Configured;
        var publishUnifiedGitHub = !explicitAppleAction &&
                                   PowerForgeReleaseService.ShouldPublishUnifiedGitHub(spec, request, runModule);
        if (!request.PlanOnly && !request.ValidateOnly && publishUnifiedGitHub)
            phases.Add(PowerForgeReleaseProgressPhase.GitHub);
        if (PowerForgeReleaseService.ShouldPublishVirusTotalMonitor(
                spec,
                request,
                explicitAppleAction,
                runModule,
                runPackages,
                runTools,
                publishUnifiedGitHub))
        {
            phases.Add(PowerForgeReleaseProgressPhase.VirusTotal);
        }
        return phases.ToArray();
    }

    private static void WriteHeader(
        PowerForgeReleaseSpec spec,
        PowerForgeReleaseRequest request,
        IReadOnlyList<PowerForgeReleaseProgressPhase> phases,
        IReadOnlyDictionary<PowerForgeReleaseProgressPhase, string> phaseNames)
    {
        var toolOutputs = CountToolOutputs(spec.Tools);
        var versionPolicy = spec.Module?.SynchronizeVersionWithPackages == true
            ? $"highest of module floor {spec.Module.ModuleVersion ?? "(required)"} and package project {spec.Module.VersionPrimaryProject}"
            : "lane-specific versions";
        var info = new KeyValueList();
        info.Add("Mode", request.PlanOnly ? "[warn]Plan only[/]" : request.ValidateOnly ? "[warn]Validate only[/]" : "[pass]Release execution[/]");
        info.Add("Config", Markup.Escape(request.ConfigPath ?? string.Empty));
        info.Add("Order", Markup.Escape(string.Join(" → ", phases.Select(phase => phaseNames[phase]))));
        info.Add("Version", Markup.Escape(versionPolicy));
        if (toolOutputs > 0) info.Add("Tool matrix", Markup.Escape($"{spec.Tools!.Targets.Length} target(s), {toolOutputs} output(s)"));
        ConsoleOutput.Write(new Rows(new Rule("[bold accent]PowerForge - Unified release[/]"), info, new TextBlock(string.Empty)));
    }

    private static int CountToolOutputs(PowerForgeToolReleaseSpec? tools)
        => tools is null
            ? 0
            : (tools.Targets ?? Array.Empty<PowerForgeToolReleaseTarget>()).Sum(target =>
                Math.Max(1, target.Frameworks?.Length ?? 0) *
                Math.Max(1, target.Runtimes?.Length ?? 0) *
                Math.Max(1, target.Flavors?.Length ?? 0));

    private static string GetPhaseName(
        PowerForgeReleaseProgressPhase phase,
        PowerForgeReleaseSpec spec,
        PowerForgeReleaseRequest request)
        => phase switch
        {
            PowerForgeReleaseProgressPhase.Versioning => "Plan packages and resolve shared version",
            PowerForgeReleaseProgressPhase.Module => "Build PowerShell module",
            PowerForgeReleaseProgressPhase.Packages =>
                (request.PublishNuget ?? spec.Packages?.PublishNuget) == true
                    ? "Build and publish NuGet packages"
                    : "Build NuGet packages",
            PowerForgeReleaseProgressPhase.Tools => "Build executable matrix",
            PowerForgeReleaseProgressPhase.AppleApps => request.AppleAction == PowerForgeAppleReleaseAction.Configured
                ? "Run Apple release configuration"
                : $"Apple {request.AppleAction}",
            PowerForgeReleaseProgressPhase.GitHub => "Publish unified GitHub release",
            PowerForgeReleaseProgressPhase.VirusTotal => "VirusTotal Monitor registration",
            _ => phase.ToString()
        };

    // Release phases are the ledger rows; module steps, packages, tools and assets are listed under their phase.
    private sealed class Reporter(ProgressView view, IReadOnlyCollection<PowerForgeReleaseProgressPhase> phases)
        : IPowerForgeReleaseProgressReporterV2
    {
        public void PhaseStarted(PowerForgeReleaseProgressPhase phase, int totalItems, string? detail = null)
            => view.Start(phase.ToString(), detail);

        public void PhaseCompleted(PowerForgeReleaseProgressPhase phase, string? detail = null)
            => view.Finish(phase.ToString(), ProgressState.Completed, detail);

        public void PhaseFailed(PowerForgeReleaseProgressPhase phase, string? detail = null)
            => view.Finish(phase.ToString(), ProgressState.Failed, detail);

        public void ItemsPlanned(PowerForgeReleaseProgressPhase phase, IReadOnlyList<PowerForgeReleaseProgressItem> items)
        {
            if (items is null || items.Count == 0) return;
            foreach (var item in items.Where(static item => item is not null))
                Plan(item);
            if (view.StateOf(phase.ToString()) == ProgressState.Planned) view.Start(phase.ToString());
        }

        public void ItemUpdated(PowerForgeReleaseProgressItem item, PowerForgeReleaseProgressItemState state, string? detail = null)
        {
            if (item is null) return;
            ProgressLedger.Apply(view, Plan(item), ProgressLedger.From(state), detail, item.ProgressValue, item.ProgressMaximum, item.Duration);
        }

        // A phase whose detailed items all finished completes with the run; the rest follow the view's rules.
        public void FinishRemaining(bool success)
        {
            foreach (var phase in phases.Select(phase => phase.ToString()))
            {
                var state = view.StateOf(phase);
                if (state is ProgressState.Completed or ProgressState.Failed or ProgressState.Skipped) continue;
                var items = view.ItemStates(phase);
                if (success && items.Count > 0 && items.All(item => item is ProgressState.Completed or ProgressState.Skipped))
                    view.Finish(phase, ProgressState.Completed, $"{items.Count} detailed step(s) completed");
            }
            view.FinishRemaining(success);
        }

        private string Plan(PowerForgeReleaseProgressItem item)
        {
            var key = $"{item.Phase}:{item.Key}";
            var phase = item.Phase.ToString();
            if (!view.Contains(phase)) view.Step(phase, phase, phase);
            view.Item(phase, key, ProgressLedger.Label(item, item.CounterLabel ?? CounterLabel(item)), item.Kind, item.Target);
            return key;
        }

        private static string CounterLabel(PowerForgeReleaseProgressItem item)
        {
            if (item.Phase == PowerForgeReleaseProgressPhase.Packages &&
                (string.Equals(item.Kind, nameof(ProjectBuildProgressPhase.PackageSigning), StringComparison.Ordinal) ||
                 string.Equals(item.Kind, nameof(ProjectBuildProgressPhase.NuGetPublish), StringComparison.Ordinal)))
                return "Package";

            return item.Phase switch
            {
                PowerForgeReleaseProgressPhase.Versioning => "Version",
                PowerForgeReleaseProgressPhase.Module => "Module",
                PowerForgeReleaseProgressPhase.Packages => "Project",
                PowerForgeReleaseProgressPhase.Tools => "Tool",
                PowerForgeReleaseProgressPhase.GitHub => "Asset",
                _ => "Item"
            };
        }
    }
}
