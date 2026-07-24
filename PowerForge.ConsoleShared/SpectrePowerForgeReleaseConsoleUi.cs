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
        WriteHeader(spec, request, phases);
        PowerForgeReleaseResult? result = null;
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
                    phase => context.AddTask($"[grey]{Markup.Escape(GetPhaseName(phase))} — pending[/]", maxValue: 100, autoStart: false));
                var reporter = new Reporter(tasks);
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
        if (!string.IsNullOrWhiteSpace(packageVersion)) table.AddRow("Package version", Esc(packageVersion));
        if (!string.IsNullOrWhiteSpace(moduleVersion)) table.AddRow("Module version", Esc(moduleVersion));
        if (toolSteps > 0) table.AddRow("Tool output steps", toolSteps.ToString());
        table.AddRow("Release assets", result.ReleaseAssets.Length.ToString());
        var gitHubReleaseUrl = result.UnifiedGitHubRelease?.ReleaseUrl;
        if (!string.IsNullOrWhiteSpace(gitHubReleaseUrl))
            table.AddRow("GitHub release", Esc(gitHubReleaseUrl));
        table.AddRow("Duration", Esc(new BufferedLogSupportService().FormatDuration(duration)));
        if (!result.Success && !string.IsNullOrWhiteSpace(result.ErrorMessage))
            table.AddRow("Error", $"[red]{Esc(result.ErrorMessage)}[/]");
        AnsiConsole.Write(table);
    }

    private static PowerForgeReleaseProgressPhase[] ResolvePhases(PowerForgeReleaseSpec spec, PowerForgeReleaseRequest request)
    {
        var runModule = spec.Module is not null && (!request.PackagesOnly && !request.ToolsOnly || request.ModuleOnly);
        var runPackages = spec.Packages is not null && !request.ModuleOnly && !request.ToolsOnly;
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
        if (!request.PlanOnly && !request.ValidateOnly && spec.GitHub?.Publish == true)
            phases.Add(PowerForgeReleaseProgressPhase.GitHub);
        return phases.ToArray();
    }

    private static void WriteHeader(
        PowerForgeReleaseSpec spec,
        PowerForgeReleaseRequest request,
        IReadOnlyList<PowerForgeReleaseProgressPhase> phases)
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
        table.AddRow("[grey]Order[/]", Esc(string.Join(" → ", phases.Select(GetPhaseName))));
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

    private static string GetPhaseName(PowerForgeReleaseProgressPhase phase)
        => phase switch
        {
            PowerForgeReleaseProgressPhase.Versioning => "Plan packages and resolve shared version",
            PowerForgeReleaseProgressPhase.Module => "Build PowerShell module",
            PowerForgeReleaseProgressPhase.Packages => "Build and publish NuGet packages",
            PowerForgeReleaseProgressPhase.Tools => "Build executable matrix",
            PowerForgeReleaseProgressPhase.GitHub => "Publish unified GitHub release",
            _ => phase.ToString()
        };

    private sealed class Reporter : IPowerForgeReleaseProgressReporter
    {
        private readonly IReadOnlyDictionary<PowerForgeReleaseProgressPhase, ProgressTask> _tasks;
        private readonly HashSet<PowerForgeReleaseProgressPhase> _failed = new();

        public Reporter(IReadOnlyDictionary<PowerForgeReleaseProgressPhase, ProgressTask> tasks) => _tasks = tasks;

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
        }

        private static string Label(PowerForgeReleaseProgressPhase phase, string? detail, string color, string? status = null)
        {
            var prefix = string.IsNullOrWhiteSpace(status) ? string.Empty : status + " ";
            var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : " — " + detail;
            return $"[{color}]{Markup.Escape(prefix + GetPhaseName(phase) + suffix)}[/]";
        }
    }
}
