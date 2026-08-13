using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using PowerForge;
using Spectre.Console;

namespace PowerForge.ConsoleShared;

internal static class SpectrePowerForgeReleaseConsoleUi
{
    public static PowerForgeReleaseResult RunInteractive(
        PowerForgeReleaseSpec spec,
        PowerForgeReleaseRequest request,
        Func<IPowerForgeReleaseProgressReporter, PowerForgeReleaseResult> run)
        => RunInteractive(AnsiConsole.Console, spec, request, run);

    internal static PowerForgeReleaseResult RunInteractive(
        IAnsiConsole console,
        PowerForgeReleaseSpec spec,
        PowerForgeReleaseRequest request,
        Func<IPowerForgeReleaseProgressReporter, PowerForgeReleaseResult> run)
    {
        if (console is null) throw new ArgumentNullException(nameof(console));
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (run is null) throw new ArgumentNullException(nameof(run));

        var phases = ResolvePhases(spec, request);
        var phaseNames = phases.ToDictionary(
            phase => phase,
            phase => GetPhaseName(phase, spec, request));
        var phaseCounters = phases
            .Select((phase, index) => new
            {
                Phase = phase,
                Counter = ProgressCounterFormatter.Format("Phase", index + 1, phases.Length)
            })
            .ToDictionary(entry => entry.Phase, entry => entry.Counter);
        WriteHeader(console, spec, request, phases, phaseNames);
        PowerForgeReleaseResult? result = null;
        Exception? failure = null;
        Reporter? reporter = null;
        var presentation = SpectreProgressPresentation.Create(console);

        SpectreProgressDisplay.Run(
            console,
            presentation.CreateColumns(),
            context =>
            {
                var tasks = phases.ToDictionary(
                    phase => phase,
                    phase => context.AddTask(
                        $"{phaseCounters[phase]} — {phaseNames[phase]} — pending",
                        maxValue: 100,
                        autoStart: false));
                foreach (var entry in tasks)
                    presentation.Register(entry.Value, entry.Key.ToString());

                reporter = new Reporter(console, context, tasks, phaseNames, phaseCounters, presentation);
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

        reporter?.WriteLedger();
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
        return result!;
    }

    public static void WriteSummary(PowerForgeReleaseResult result, TimeSpan duration)
        => WriteSummary(AnsiConsole.Console, result, duration);

    internal static void WriteSummary(IAnsiConsole console, PowerForgeReleaseResult result, TimeSpan duration)
    {
        if (console is null) throw new ArgumentNullException(nameof(console));
        if (result is null) return;
        static string Esc(string? value) => Markup.Escape(value ?? string.Empty);

        var unicode = ConsoleEncoding.ShouldRenderUnicode(console.Profile.Capabilities.Unicode);
        var icon = result.Success ? (unicode ? "✅" : "+") : (unicode ? "❌" : "x");
        var color = result.Success ? "green" : "red";
        console.Write(new Rule($"[{color}]{icon} Unified release summary[/]").LeftJustified());

        var packageVersion = result.Packages?.Result.Release?.ResolvedVersion;
        var moduleVersion = result.ModulePlan?.ModuleVersion;
        var toolSteps = result.ToolPlan?.Targets.Sum(target => target.Combinations.Length)
            ?? result.DotNetToolPlan?.Steps.Length
            ?? 0;
        var table = new Table()
            .Border(unicode ? TableBorder.Rounded : TableBorder.Simple)
            .AddColumn(new TableColumn("Item").NoWrap())
            .AddColumn(new TableColumn("Value"));
        table.AddRow("Status", result.Success ? "[green]Succeeded[/]" : "[red]Failed[/]");
        if (!string.IsNullOrWhiteSpace(packageVersion)) table.AddRow("Package build version", Esc(packageVersion));
        if (!string.IsNullOrWhiteSpace(moduleVersion)) table.AddRow("Module version", Esc(moduleVersion));
        if (toolSteps > 0) table.AddRow("Tool output steps", toolSteps.ToString());
        table.AddRow("Release assets", result.ReleaseAssets.Length.ToString());
        var gitHubReleaseUrl = result.UnifiedGitHubRelease?.ReleaseUrl;
        if (!string.IsNullOrWhiteSpace(gitHubReleaseUrl))
        {
            table.AddRow("GitHub release", Esc(gitHubReleaseUrl));
            table.AddRow(
                "GitHub action",
                result.UnifiedGitHubRelease!.ReusedExistingRelease
                    ? "[yellow]Reused existing release[/]"
                    : "[green]Created new release[/]");
            table.AddRow(
                "GitHub assets",
                Esc(
                    $"{result.UnifiedGitHubRelease.UploadedAssets.Length} uploaded, " +
                    $"{result.UnifiedGitHubRelease.SkippedExistingAssets.Length} skipped, " +
                    $"{result.UnifiedGitHubRelease.ReplacedExistingAssets.Length} replaced"));
        }
        if (result.VirusTotalMonitor is { } virusTotal)
        {
            var monitorSummary = !virusTotal.Success
                ? $"[red]Failed: {Esc(virusTotal.ErrorMessage ?? "Monitor registration failed")}[/]"
                : virusTotal.Artifacts.Length == 0
                    ? "[yellow]Skipped: no configured final release artifacts matched[/]"
                    : $"[green]Registered {virusTotal.Artifacts.Length} artifact(s); analysis remains asynchronous[/]";
            table.AddRow("VirusTotal Monitor", monitorSummary);
            if (!string.IsNullOrWhiteSpace(result.VirusTotalMonitorReceiptPath))
                table.AddRow("VirusTotal receipt", Esc(result.VirusTotalMonitorReceiptPath));
        }
        table.AddRow("Duration", Esc(new BufferedLogSupportService().FormatDuration(duration)));
        if (!result.Success && !string.IsNullOrWhiteSpace(result.ErrorMessage))
            table.AddRow("Error", $"[red]{Esc(result.ErrorMessage)}[/]");
        console.Write(table);
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
        IAnsiConsole console,
        PowerForgeReleaseSpec spec,
        PowerForgeReleaseRequest request,
        IReadOnlyList<PowerForgeReleaseProgressPhase> phases,
        IReadOnlyDictionary<PowerForgeReleaseProgressPhase, string> phaseNames)
    {
        static string Esc(string? value) => Markup.Escape(value ?? string.Empty);
        var unicode = ConsoleEncoding.ShouldRenderUnicode(console.Profile.Capabilities.Unicode);
        var title = unicode ? "🚀 PowerForge • Unified release" : "PowerForge • Unified release";
        console.Write(new Rule($"[yellow bold underline]{Esc(title)}[/]") { Justification = Justify.Left });

        var toolOutputs = CountToolOutputs(spec.Tools);
        var versionPolicy = spec.Module?.SynchronizeVersionWithPackages == true
            ? $"highest of module floor {spec.Module.ModuleVersion ?? "(required)"} and package project {spec.Module.VersionPrimaryProject}"
            : "lane-specific versions";
        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(new TableColumn("Item").NoWrap())
            .AddColumn(new TableColumn("Value"));
        table.AddRow("[grey]Mode[/]", request.PlanOnly ? "[yellow]Plan only[/]" : request.ValidateOnly ? "[yellow]Validate only[/]" : "[green]Release execution[/]");
        table.AddRow("[grey]Config[/]", Esc(request.ConfigPath));
        table.AddRow("[grey]Order[/]", Esc(string.Join(" → ", phases.Select(phase => phaseNames[phase]))));
        table.AddRow("[grey]Version[/]", Esc(versionPolicy));
        if (toolOutputs > 0) table.AddRow("[grey]Tool matrix[/]", Esc($"{spec.Tools!.Targets.Length} target(s), {toolOutputs} output(s)"));
        console.Write(table);
        console.WriteLine();
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
            PowerForgeReleaseProgressPhase.GitHub => "Publish unified GitHub release",
            PowerForgeReleaseProgressPhase.VirusTotal => "VirusTotal Monitor registration",
            _ => phase.ToString()
        };

    private sealed class Reporter : IPowerForgeReleaseProgressReporterV2
    {
        private readonly ProgressContext _context;
        private readonly IAnsiConsole _console;
        private readonly IReadOnlyDictionary<PowerForgeReleaseProgressPhase, ProgressTask> _tasks;
        private readonly IReadOnlyDictionary<PowerForgeReleaseProgressPhase, string> _phaseNames;
        private readonly IReadOnlyDictionary<PowerForgeReleaseProgressPhase, string> _phaseCounters;
        private readonly HashSet<PowerForgeReleaseProgressPhase> _failed = new();
        private readonly SpectreProgressLedger _ledger;
        private readonly SpectreProgressPresentation _presentation;

        public Reporter(
            IAnsiConsole console,
            ProgressContext context,
            IReadOnlyDictionary<PowerForgeReleaseProgressPhase, ProgressTask> tasks,
            IReadOnlyDictionary<PowerForgeReleaseProgressPhase, string> phaseNames,
            IReadOnlyDictionary<PowerForgeReleaseProgressPhase, string> phaseCounters,
            SpectreProgressPresentation presentation)
        {
            _console = console;
            _context = context;
            _tasks = tasks;
            _phaseNames = phaseNames;
            _phaseCounters = phaseCounters;
            _presentation = presentation;
            _ledger = new SpectreProgressLedger(context, presentation);
        }

        public void PhaseStarted(PowerForgeReleaseProgressPhase phase, int totalItems, string? detail = null)
        {
            if (!_tasks.TryGetValue(phase, out var task)) return;
            if (!task.IsStarted) task.StartTask();
            _presentation.MarkStarted(task, phase.ToString());
            task.Value = 5;
            task.Description = Label(phase, detail);
        }

        public void PhaseCompleted(PowerForgeReleaseProgressPhase phase, string? detail = null)
        {
            if (!_tasks.TryGetValue(phase, out var task)) return;
            if (!task.IsStarted) task.StartTask();
            task.Description = Label(phase, detail);
            task.Value = 100;
            task.StopTask();
            _presentation.MarkTerminal(task, SpectreProgressLedgerState.Completed);
        }

        public void PhaseFailed(PowerForgeReleaseProgressPhase phase, string? detail = null)
        {
            if (!_tasks.TryGetValue(phase, out var task)) return;
            _failed.Add(phase);
            if (!task.IsStarted) task.StartTask();
            task.Description = Label(phase, detail);
            task.Value = 100;
            task.StopTask();
            _presentation.MarkTerminal(task, SpectreProgressLedgerState.Failed);
        }

        public void ItemsPlanned(
            PowerForgeReleaseProgressPhase phase,
            IReadOnlyList<PowerForgeReleaseProgressItem> items)
        {
            if (items is null || items.Count == 0)
                return;

            _ledger.Plan(items
                .Where(item => item is not null)
                .Select(ToLedgerItem));

            if (_tasks.TryGetValue(phase, out var phaseTask))
            {
                if (!phaseTask.IsStarted)
                {
                    phaseTask.StartTask();
                    _presentation.MarkStarted(phaseTask, phase.ToString());
                    phaseTask.Value = 5;
                }

                phaseTask.Description = Label(phase, $"{CountItems(phase)} detailed step(s)");
            }
        }

        public void ItemUpdated(
            PowerForgeReleaseProgressItem item,
            PowerForgeReleaseProgressItemState state,
            string? detail = null)
        {
            if (item is null)
                return;

            _ledger.Update(
                ToLedgerItem(item),
                state switch
                {
                    PowerForgeReleaseProgressItemState.Started => SpectreProgressLedgerState.Started,
                    PowerForgeReleaseProgressItemState.Completed => SpectreProgressLedgerState.Completed,
                    PowerForgeReleaseProgressItemState.Failed => SpectreProgressLedgerState.Failed,
                    PowerForgeReleaseProgressItemState.Skipped => SpectreProgressLedgerState.Skipped,
                    _ => SpectreProgressLedgerState.Planned
                },
                detail);

            UpdatePhaseProgress(item.Phase);
        }

        public void FinishRemaining(bool success)
        {
            foreach (var entry in _tasks)
            {
                if (entry.Value.IsFinished || _failed.Contains(entry.Key)) continue;
                if (success &&
                    _ledger.GetItemCount(entry.Key.ToString()) > 0 &&
                    _ledger.GetCompletionRatio(entry.Key.ToString()) >= 1d)
                {
                    PhaseCompleted(entry.Key, $"{CountItems(entry.Key)} detailed step(s) completed");
                    continue;
                }

                if (entry.Value.IsStarted && !success)
                {
                    PhaseFailed(entry.Key, "workflow stopped");
                    continue;
                }

                entry.Value.StartTask();
                entry.Value.Description = Label(entry.Key, success ? "not required" : "skipped after failure");
                entry.Value.Value = 100;
                entry.Value.StopTask();
                _presentation.MarkTerminal(entry.Value, SpectreProgressLedgerState.Skipped);
            }

            _ledger.FinishRemaining(success);
            _ledger.ClearLiveTasks();
            _context.Refresh();
        }

        public void WriteLedger()
            => SpectreProgressLedger.WriteLedger(
                _console,
                _ledger.GetSnapshots(),
                "Unified release details");

        private string Label(PowerForgeReleaseProgressPhase phase, string? detail)
        {
            var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : " — " + detail;
            return _phaseCounters[phase] + " — " + _phaseNames[phase] + suffix;
        }

        private void UpdatePhaseProgress(PowerForgeReleaseProgressPhase phase)
        {
            if (!_tasks.TryGetValue(phase, out var phaseTask) ||
                !phaseTask.IsStarted ||
                phaseTask.IsFinished)
            {
                return;
            }

            if (_ledger.GetItemCount(phase.ToString()) == 0)
                return;

            var completed = _ledger.GetCompletionRatio(phase.ToString());
            phaseTask.Value = Math.Max(5d, Math.Min(99d, completed * 100d));
        }

        private int CountItems(PowerForgeReleaseProgressPhase phase)
            => _ledger.GetItemCount(phase.ToString());

        private SpectreProgressLedgerItem ToLedgerItem(PowerForgeReleaseProgressItem item)
            => new()
            {
                Key = $"{item.Phase}:{item.Key}",
                GroupKey = item.GroupKey ?? item.Phase.ToString(),
                GroupTitle = item.GroupTitle ?? _phaseNames[item.Phase],
                GroupOrder = ((int)item.Phase * 100) + (item.GroupOrder ?? 0),
                Title = item.Title,
                Kind = item.Kind,
                Target = item.Target,
                CounterLabel = item.CounterLabel ?? GetCounterLabel(item),
                Position = item.Position,
                Total = item.Total,
                ProgressValue = item.ProgressValue,
                ProgressMaximum = item.ProgressMaximum,
                Duration = item.Duration
            };

        private static string GetCounterLabel(PowerForgeReleaseProgressItem item)
        {
            if (item.Phase == PowerForgeReleaseProgressPhase.Packages &&
                (string.Equals(item.Kind, ProjectBuildProgressPhase.PackageSigning.ToString(), StringComparison.Ordinal) ||
                 string.Equals(item.Kind, ProjectBuildProgressPhase.NuGetPublish.ToString(), StringComparison.Ordinal)))
            {
                return "Package";
            }

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
