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
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (run is null) throw new ArgumentNullException(nameof(run));

        var phases = ResolvePhases(spec, request);
        var phaseNames = phases.ToDictionary(
            phase => phase,
            phase => GetPhaseName(phase, spec, request));
        WriteHeader(spec, request, phases, phaseNames);
        PowerForgeReleaseResult? result = null;
        Exception? failure = null;

        SpectreProgressDisplay.Run(
            SpectreBuildProgressColumns.CreateStandard(),
            context =>
            {
                var tasks = phases.ToDictionary(
                    phase => phase,
                    phase => context.AddTask($"[grey]{Markup.Escape(phaseNames[phase])} — pending[/]", maxValue: 100, autoStart: false));
                var reporter = new Reporter(context, tasks, phaseNames);
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

    public static void WriteSummary(PowerForgeReleaseResult result, TimeSpan duration)
    {
        if (result is null) return;
        static string Esc(string? value) => Markup.Escape(value ?? string.Empty);

        var unicode = ConsoleEncoding.ShouldRenderUnicode(AnsiConsole.Profile.Capabilities.Unicode);
        var icon = result.Success ? (unicode ? "✅" : "+") : (unicode ? "❌" : "x");
        var color = result.Success ? "green" : "red";
        AnsiConsole.Write(new Rule($"[{color}]{icon} Unified release summary[/]").LeftJustified());

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
        table.AddRow("Duration", Esc(new BufferedLogSupportService().FormatDuration(duration)));
        if (!result.Success && !string.IsNullOrWhiteSpace(result.ErrorMessage))
            table.AddRow("Error", $"[red]{Esc(result.ErrorMessage)}[/]");
        AnsiConsole.Write(table);
    }

    private static PowerForgeReleaseProgressPhase[] ResolvePhases(PowerForgeReleaseSpec spec, PowerForgeReleaseRequest request)
    {
        var hasTargetAwareSelection =
            request.Targets.Any(static target => !string.IsNullOrWhiteSpace(target)) &&
            (spec.Tools is not null || spec.AppleApps is not null);
        var runModule = !hasTargetAwareSelection &&
                        spec.Module is not null &&
                        (!request.PackagesOnly && !request.ToolsOnly || request.ModuleOnly);
        var runPackages = spec.Packages is not null && !request.ModuleOnly && !request.ToolsOnly;
        if (hasTargetAwareSelection)
            runPackages = false;
        var runTools = spec.Tools is not null && !request.ModuleOnly && !request.PackagesOnly;
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
        if (!request.PlanOnly &&
            !request.ValidateOnly &&
            PowerForgeReleaseService.ShouldPublishUnifiedGitHub(spec, request, runModule))
            phases.Add(PowerForgeReleaseProgressPhase.GitHub);
        return phases.ToArray();
    }

    private static void WriteHeader(
        PowerForgeReleaseSpec spec,
        PowerForgeReleaseRequest request,
        IReadOnlyList<PowerForgeReleaseProgressPhase> phases,
        IReadOnlyDictionary<PowerForgeReleaseProgressPhase, string> phaseNames)
    {
        static string Esc(string? value) => Markup.Escape(value ?? string.Empty);
        var unicode = ConsoleEncoding.ShouldRenderUnicode(AnsiConsole.Profile.Capabilities.Unicode);
        var title = unicode ? "🚀 PowerForge • Unified release" : "PowerForge • Unified release";
        AnsiConsole.Write(new Rule($"[yellow bold underline]{Esc(title)}[/]") { Justification = Justify.Left });

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
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
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
            _ => phase.ToString()
        };

    private sealed class Reporter : IPowerForgeReleaseProgressReporterV2
    {
        private readonly ProgressContext _context;
        private readonly IReadOnlyDictionary<PowerForgeReleaseProgressPhase, ProgressTask> _tasks;
        private readonly IReadOnlyDictionary<PowerForgeReleaseProgressPhase, string> _phaseNames;
        private readonly HashSet<PowerForgeReleaseProgressPhase> _failed = new();
        private readonly Dictionary<string, ProgressTask> _itemTasks = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PowerForgeReleaseProgressItem> _items = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _sync = new();

        public Reporter(
            ProgressContext context,
            IReadOnlyDictionary<PowerForgeReleaseProgressPhase, ProgressTask> tasks,
            IReadOnlyDictionary<PowerForgeReleaseProgressPhase, string> phaseNames)
        {
            _context = context;
            _tasks = tasks;
            _phaseNames = phaseNames;
        }

        public void PhaseStarted(PowerForgeReleaseProgressPhase phase, int totalItems, string? detail = null)
        {
            if (!_tasks.TryGetValue(phase, out var task)) return;
            if (!task.IsStarted) task.StartTask();
            task.Value = 5;
            task.Description = Label(phase, detail, "cyan");
        }

        public void PhaseCompleted(PowerForgeReleaseProgressPhase phase, string? detail = null)
        {
            if (!_tasks.TryGetValue(phase, out var task)) return;
            if (!task.IsStarted) task.StartTask();
            task.Description = Label(phase, detail, "green", "✓");
            task.Value = 100;
            task.StopTask();
        }

        public void PhaseFailed(PowerForgeReleaseProgressPhase phase, string? detail = null)
        {
            if (!_tasks.TryGetValue(phase, out var task)) return;
            _failed.Add(phase);
            if (!task.IsStarted) task.StartTask();
            task.Description = Label(phase, detail, "red", "x");
            task.Value = 100;
            task.StopTask();
        }

        public void ItemsPlanned(
            PowerForgeReleaseProgressPhase phase,
            IReadOnlyList<PowerForgeReleaseProgressItem> items)
        {
            if (items is null || items.Count == 0)
                return;

            lock (_sync)
            {
                foreach (var item in items)
                {
                    if (item is null)
                        continue;

                    var key = ItemKey(item);
                    if (_itemTasks.ContainsKey(key))
                        continue;

                    _items[key] = item;
                    _itemTasks[key] = _context.AddTask(
                        BuildItemLabel(item, PowerForgeReleaseProgressItemState.Planned, null),
                        maxValue: 1,
                        autoStart: false);
                }
            }

            if (_tasks.TryGetValue(phase, out var phaseTask))
                phaseTask.Description = Label(phase, $"{CountItems(phase)} detailed step(s)", "cyan");
        }

        public void ItemUpdated(
            PowerForgeReleaseProgressItem item,
            PowerForgeReleaseProgressItemState state,
            string? detail = null)
        {
            if (item is null)
                return;

            ProgressTask task;
            lock (_sync)
            {
                var key = ItemKey(item);
                if (!_itemTasks.TryGetValue(key, out task!))
                {
                    _items[key] = item;
                    task = _context.AddTask(
                        BuildItemLabel(item, PowerForgeReleaseProgressItemState.Planned, null),
                        maxValue: 1,
                        autoStart: false);
                    _itemTasks[key] = task;
                }
            }

            task.Description = BuildItemLabel(item, state, detail);
            switch (state)
            {
                case PowerForgeReleaseProgressItemState.Started:
                    if (!task.IsStarted) task.StartTask();
                    if (item.ProgressMaximum > 0)
                    {
                        task.IsIndeterminate = false;
                        task.Value = task.MaxValue *
                                     Math.Min(1d, Math.Max(0d, item.ProgressValue / item.ProgressMaximum));
                    }
                    else
                    {
                        task.IsIndeterminate = true;
                    }
                    break;
                case PowerForgeReleaseProgressItemState.Completed:
                case PowerForgeReleaseProgressItemState.Failed:
                case PowerForgeReleaseProgressItemState.Skipped:
                    if (!task.IsStarted) task.StartTask();
                    task.IsIndeterminate = false;
                    task.Value = task.MaxValue;
                    task.StopTask();
                    break;
            }

            UpdatePhaseProgress(item.Phase);
        }

        public void FinishRemaining(bool success)
        {
            foreach (var entry in _tasks)
            {
                if (entry.Value.IsFinished || _failed.Contains(entry.Key)) continue;
                if (entry.Value.IsStarted && !success)
                {
                    PhaseFailed(entry.Key, "workflow stopped");
                    continue;
                }

                entry.Value.StartTask();
                entry.Value.Description = Label(entry.Key, success ? "not required" : "skipped after failure", "grey", "–");
                entry.Value.Value = 100;
                entry.Value.StopTask();
            }

            lock (_sync)
            {
                foreach (var entry in _itemTasks)
                {
                    var task = entry.Value;
                    if (task.IsFinished) continue;
                    var item = _items[entry.Key];
                    var state = task.IsStarted && !success
                        ? PowerForgeReleaseProgressItemState.Failed
                        : PowerForgeReleaseProgressItemState.Skipped;
                    ItemUpdated(
                        item,
                        state,
                        success ? "not required" : "skipped after failure");
                }
            }
        }

        private string Label(PowerForgeReleaseProgressPhase phase, string? detail, string color, string? status = null)
        {
            var prefix = string.IsNullOrWhiteSpace(status) ? string.Empty : status + " ";
            var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : " — " + detail;
            return $"[{color}]{Markup.Escape(prefix + _phaseNames[phase] + suffix)}[/]";
        }

        private void UpdatePhaseProgress(PowerForgeReleaseProgressPhase phase)
        {
            if (!_tasks.TryGetValue(phase, out var phaseTask) ||
                !phaseTask.IsStarted ||
                phaseTask.IsFinished)
            {
                return;
            }

            lock (_sync)
            {
                var tasks = _items
                    .Where(entry => entry.Value.Phase == phase)
                    .Select(entry => _itemTasks[entry.Key])
                    .ToArray();
                if (tasks.Length == 0)
                    return;

                var completed = tasks.Sum(task =>
                    task.MaxValue <= 0 ? 0 : Math.Min(1d, Math.Max(0d, task.Value / task.MaxValue)));
                phaseTask.Value = Math.Max(5d, Math.Min(99d, completed / tasks.Length * 100d));
            }
        }

        private int CountItems(PowerForgeReleaseProgressPhase phase)
        {
            lock (_sync)
                return _items.Values.Count(item => item.Phase == phase);
        }

        private static string ItemKey(PowerForgeReleaseProgressItem item)
            => $"{item.Phase}:{item.Key}";

        private static string BuildItemLabel(
            PowerForgeReleaseProgressItem item,
            PowerForgeReleaseProgressItemState state,
            string? detail)
        {
            var ordinal = item.Position > 0 && item.Total > 0
                ? $"{item.Position:00}/{item.Total:00} "
                : string.Empty;
            var status = GetItemIcon(item, state) + " ";
            var color = state switch
            {
                PowerForgeReleaseProgressItemState.Started => "cyan",
                PowerForgeReleaseProgressItemState.Completed => "green",
                PowerForgeReleaseProgressItemState.Failed => "red",
                _ => "grey"
            };
            var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" — {detail}";
            return $"[{color}]{Markup.Escape($"  {status}{ordinal}{item.Title}{suffix}")}[/]";
        }

        private static string GetItemIcon(
            PowerForgeReleaseProgressItem item,
            PowerForgeReleaseProgressItemState state)
        {
            var unicode = ConsoleEncoding.ShouldRenderUnicode(AnsiConsole.Profile.Capabilities.Unicode);
            if (state == PowerForgeReleaseProgressItemState.Completed) return unicode ? "✓" : "+";
            if (state == PowerForgeReleaseProgressItemState.Failed) return "x";
            if (state == PowerForgeReleaseProgressItemState.Skipped) return "–";
            if (!unicode) return "·";

            return item.Kind switch
            {
                nameof(ModulePipelineStepKind.Build) => "🔨",
                nameof(ModulePipelineStepKind.Documentation) => "📝",
                nameof(ModulePipelineStepKind.Formatting) => "🎨",
                nameof(ModulePipelineStepKind.Signing) => "🔏",
                nameof(ModulePipelineStepKind.Validation) => "🔎",
                nameof(ModulePipelineStepKind.Tests) => "🧪",
                nameof(ModulePipelineStepKind.Artefact) => "📦",
                nameof(ModulePipelineStepKind.Install) => "📥",
                nameof(ModulePipelineStepKind.Cleanup) => "🧹",
                nameof(ProjectBuildProgressPhase.NuGetPublish) => "🚀",
                nameof(ProjectBuildProgressPhase.PackageSigning) => "🔏",
                "ToolPublish" => "🚀",
                _ => "•"
            };
        }
    }
}
