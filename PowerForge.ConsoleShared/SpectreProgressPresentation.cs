using System;
using System.Collections.Concurrent;
using PowerForge;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge.ConsoleShared;

/// <summary>
/// Owns the shared Spectre layout, labels, icons, and fixed timing state used by
/// direct module builds and unified release progress.
/// </summary>
internal sealed class SpectreProgressPresentation
{
    private readonly ConcurrentDictionary<ProgressTask, string> _icons = new();
    private readonly ConcurrentDictionary<ProgressTask, DateTimeOffset> _started = new();
    private readonly ConcurrentDictionary<ProgressTask, TimeSpan> _completed = new();
    private readonly ConcurrentDictionary<ProgressTask, SpectreProgressLedgerState> _states = new();
    private readonly ConcurrentDictionary<ProgressTask, string> _details = new();
    private readonly ConcurrentDictionary<ProgressTask, bool> _signing = new();
    private readonly bool _unicode;
    private readonly bool _includeBar;
    private readonly bool _includeElapsed;
    private readonly int _barWidth;
    private readonly int _descriptionWidth;
    private readonly int _targetWidth;

    internal SpectreProgressPresentation(int viewportWidth, bool unicode)
    {
        var width = Math.Max(40, viewportWidth);
        _unicode = unicode;
        _includeElapsed = true;
        _barWidth = ComputeBarWidth(width);
        _includeBar = _barWidth > 0;

        const int percentWidth = 7;
        var elapsedWidth = _includeElapsed ? 8 : 0;
        const int spinnerWidth = 2;
        const int iconWidth = 2;
        const int gaps = 10;
        _descriptionWidth = Math.Max(
            24,
            width - (iconWidth + _barWidth + percentWidth + elapsedWidth + spinnerWidth + gaps));
        _targetWidth = width <= 100 ? 0 : 26;
    }

    internal static SpectreProgressPresentation Create(IAnsiConsole console)
    {
        if (console is null) throw new ArgumentNullException(nameof(console));
        var unicode = ConsoleEncoding.ShouldRenderUnicode(console.Profile.Capabilities.Unicode);
        return new SpectreProgressPresentation(console.Profile.Width, unicode);
    }

    internal ProgressColumn[] CreateColumns()
        => SpectreBuildProgressColumns.CreateDetailed(
            _includeBar,
            _includeElapsed,
            _barWidth,
            _icons,
            _started,
            _completed,
            new StepStateColumn(_states));

    internal void SetDetail(ProgressTask task, SpectreProgressLedgerItem item, SpectreProgressLedgerState state, string? detail)
    {
        _states[task] = state;
        _details[task] = string.IsNullOrWhiteSpace(detail) ? item.Title : $"{item.Title}: {detail}";
        _signing[task] = item.Kind?.ToLowerInvariant() is "signing" or "packagesigning" or "msisign";
    }

    internal IRenderable? RenderActiveStatus(IReadOnlyList<ProgressTask> tasks)
    {
        var task = tasks.LastOrDefault(task => task.IsStarted && !task.IsFinished);
        if (task is null || !_details.TryGetValue(task, out var detail)) return null;
        var status = new Text("Current: " + detail, new Style(Color.Cyan1));
        if (!_signing.TryGetValue(task, out var signing) || !signing) return status;
        return new Rows(status, new Text(ModuleSigningProgressProtocol.UserGuidance, new Style(Color.Yellow)));
    }

    internal string BuildLabel(SpectreProgressLedgerItem item, string? detail)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));

        var ordinal = item.Position > 0 && item.Total > 0
            ? ProgressCounterFormatter.Format(item.CounterLabel, item.Position, item.Total)
            : string.Empty;
        var idWidth = ordinal.Length;
        var detailSuffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" — {detail}";
        var title = item.Title + detailSuffix;

        if (idWidth == 0)
            return PadOrEllipsis(title, _descriptionWidth);

        if (_targetWidth <= 0 || string.IsNullOrWhiteSpace(item.Target))
        {
            var titleWidth = Math.Max(0, _descriptionWidth - idWidth - 1);
            return $"{ordinal} {PadOrEllipsis(title, titleWidth)}".TrimEnd();
        }

        var titleWidthWithTarget = Math.Max(0, _descriptionWidth - idWidth - _targetWidth - 2);
        var target = PadOrEllipsis(item.Target ?? string.Empty, _targetWidth);
        return $"{ordinal} {PadOrEllipsis(title, titleWidthWithTarget)} {target}".TrimEnd();
    }

    internal void Register(ProgressTask task, string? kind)
    {
        if (task is null) throw new ArgumentNullException(nameof(task));
        _icons[task] = GetKindIcon(kind, _unicode);
    }

    internal void MarkStarted(ProgressTask task, string? kind)
    {
        Register(task, kind);
        _states[task] = SpectreProgressLedgerState.Started;
        _started.TryAdd(task, DateTimeOffset.Now);
    }

    internal void MarkTerminal(
        ProgressTask task,
        SpectreProgressLedgerState state,
        TimeSpan? duration = null)
    {
        if (task is null) throw new ArgumentNullException(nameof(task));

        _icons[task] = GetStateIcon(state, _unicode);
        _states[task] = state;
        if (duration.HasValue)
        {
            _completed[task] = duration.Value;
            return;
        }

        if (_started.TryGetValue(task, out var startedAt))
            _completed[task] = DateTimeOffset.Now - startedAt;
    }

    internal void Remove(ProgressTask task)
    {
        if (task is null)
            return;

        _icons.TryRemove(task, out _);
        _started.TryRemove(task, out _);
        _completed.TryRemove(task, out _);
        _states.TryRemove(task, out _);
        _details.TryRemove(task, out _);
        _signing.TryRemove(task, out _);
    }

    internal static string GetKindIcon(string? kind, bool unicode)
    {
        if (!Enum.TryParse(kind, ignoreCase: true, out ModulePipelineStepKind parsed))
        {
            return kind?.Trim().ToLowerInvariant() switch
            {
                "module" => unicode ? "[cyan]🔨[/]" : "[cyan]BL[/]",
                "plan" => unicode ? "[steelblue1]🧭[/]" : "[steelblue1]PL[/]",
                "packages" => unicode ? "[magenta]📦[/]" : "[magenta]PK[/]",
                "packagesigning" or "msisign" => unicode ? "[gold3]🔏[/]" : "[gold3]SG[/]",
                "nugetpublish" => unicode ? "[yellow]🚀[/]" : "[yellow]PB[/]",
                "githubpublish" or "githubasset" => unicode ? "[yellow]🚀[/]" : "[yellow]GH[/]",
                "tools" or "toolpublish" => unicode ? "[deepskyblue1]🧰[/]" : "[deepskyblue1]TL[/]",
                "github" => unicode ? "[yellow]🚀[/]" : "[yellow]GH[/]",
                "versioning" => unicode ? "[lightskyblue1]🏷[/]" : "[lightskyblue1]VR[/]",
                "clean" => unicode ? "[grey]🧹[/]" : "[grey]CL[/]",
                "restore" => unicode ? "[green]📥[/]" : "[green]RS[/]",
                "servicelifecycle" or "commandhook" => unicode ? "[steelblue1]⚙[/]" : "[steelblue1]AC[/]",
                "benchmarkextract" or "benchmarkgate" => unicode ? "[orange3]🧪[/]" : "[orange3]TS[/]",
                "manifest" => unicode ? "[deepskyblue1]📝[/]" : "[deepskyblue1]DC[/]",
                "msiprepare" or "msibuild" or "bundle" or "storepackage" or "debianpackage" or "macapppackage" => unicode ? "[magenta]📦[/]" : "[magenta]PK[/]",
                _ => unicode ? "[grey]•[/]" : "[grey]PF[/]"
            };
        }

        return parsed switch
        {
            ModulePipelineStepKind.Versioning => unicode ? "[lightskyblue1]🏷[/]" : "[lightskyblue1]VR[/]",
            ModulePipelineStepKind.Build => unicode ? "[cyan]🔨[/]" : "[cyan]BL[/]",
            ModulePipelineStepKind.Compilation => unicode ? "[cyan]🔨[/]" : "[cyan]BL[/]",
            ModulePipelineStepKind.Documentation => unicode ? "[deepskyblue1]📝[/]" : "[deepskyblue1]DC[/]",
            ModulePipelineStepKind.Formatting => unicode ? "[mediumpurple3]🎨[/]" : "[mediumpurple3]FM[/]",
            ModulePipelineStepKind.Signing => unicode ? "[gold3]🔏[/]" : "[gold3]SG[/]",
            ModulePipelineStepKind.Validation => unicode ? "[lightskyblue1]🔎[/]" : "[lightskyblue1]VA[/]",
            ModulePipelineStepKind.Tests => unicode ? "[orange3]🧪[/]" : "[orange3]TS[/]",
            ModulePipelineStepKind.Artefact => unicode ? "[magenta]📦[/]" : "[magenta]PK[/]",
            ModulePipelineStepKind.Publish => unicode ? "[yellow]🚀[/]" : "[yellow]PB[/]",
            ModulePipelineStepKind.Install => unicode ? "[green]📥[/]" : "[green]IN[/]",
            ModulePipelineStepKind.Cleanup => unicode ? "[grey]🧹[/]" : "[grey]CL[/]",
            ModulePipelineStepKind.Action => unicode ? "[steelblue1]⚙[/]" : "[steelblue1]AC[/]",
            ModulePipelineStepKind.PackageBuild => unicode ? "[magenta]📦[/]" : "[magenta]PK[/]",
            ModulePipelineStepKind.ExternalAsset => unicode ? "[deepskyblue1]📥[/]" : "[deepskyblue1]AS[/]",
            _ => unicode ? "[grey]•[/]" : "[grey]PF[/]"
        };
    }

    private static string GetStateIcon(SpectreProgressLedgerState state, bool unicode)
        => state switch
        {
            SpectreProgressLedgerState.Completed => unicode ? "[green]✅[/]" : "[green]OK[/]",
            SpectreProgressLedgerState.Failed => unicode ? "[red]❌[/]" : "[red]X[/]",
            SpectreProgressLedgerState.Skipped => unicode ? "[grey]⏭[/]" : "[grey]SK[/]",
            _ => unicode ? "[grey]•[/]" : "[grey]PF[/]"
        };

    private static int ComputeBarWidth(int viewportWidth)
    {
        if (viewportWidth >= 160) return 40;
        if (viewportWidth >= 140) return 30;
        if (viewportWidth >= 120) return 18;
        if (viewportWidth >= 100) return 14;
        if (viewportWidth >= 80) return 12;
        return 0;
    }

    private static string PadOrEllipsis(string input, int width)
    {
        if (width <= 0)
            return string.Empty;

        input ??= string.Empty;
        if (input.Length == width)
            return input;
        if (input.Length < width)
            return input.PadRight(width);
        if (width == 1)
            return "…";
        return input.Substring(0, width - 1) + "…";
    }

    private sealed class StepStateColumn(ConcurrentDictionary<ProgressTask, SpectreProgressLedgerState> states) : ProgressColumn
    {
        public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
        {
            states.TryGetValue(task, out var state);
            var text = state switch
            {
                SpectreProgressLedgerState.Completed => "done",
                SpectreProgressLedgerState.Failed => "failed",
                SpectreProgressLedgerState.Skipped => "skipped",
                SpectreProgressLedgerState.Started => task.IsIndeterminate ? "running" : $"{task.Percentage:0}%",
                _ => "queued"
            };
            return new Text(text.PadLeft(7));
        }
    }
}
