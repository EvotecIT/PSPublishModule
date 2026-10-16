using System;
using System.Collections.Concurrent;
using PowerForge;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text;

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
    private readonly bool _emoji;
    private readonly bool _includeBar;
    private readonly bool _includeElapsed;
    private readonly int _barWidth;
    private readonly int _descriptionWidth;
    private readonly int _targetWidth;

    internal SpectreProgressPresentation(int viewportWidth, bool unicode, bool? emoji = null)
    {
        var width = Math.Max(40, viewportWidth);
        _unicode = unicode;
        _emoji = unicode && (emoji ?? ConsoleEncoding.ShouldRenderEmoji());
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
        return new SpectreProgressPresentation(console.Profile.Width, unicode,
            ConsoleEncoding.ShouldRenderEmoji(console.Profile.Capabilities.Unicode));
    }

    internal ProgressColumn[] CreateColumns()
        => SpectreBuildProgressColumns.CreateDetailed(
            _includeBar,
            _includeElapsed,
            _barWidth,
            _icons,
            _started,
            _completed,
            new StepStateColumn(_states),
            _emoji);

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
        var detailSuffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : (_unicode ? $" — {detail}" : $" - {detail}");
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
        _icons[task] = GetKindIcon(kind, _emoji);
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

        _icons[task] = GetStateIcon(state, _emoji);
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

    internal static string GetKindIcon(string? kind, bool emoji)
    {
        if (!Enum.TryParse(kind, ignoreCase: true, out ModulePipelineStepKind parsed))
        {
            return kind?.Trim().ToLowerInvariant() switch
            {
                "module" => emoji ? "[cyan]🔨[/]" : "[cyan]BL[/]",
                "plan" => emoji ? "[steelblue1]🧭[/]" : "[steelblue1]PL[/]",
                "packages" => emoji ? "[magenta]📦[/]" : "[magenta]PK[/]",
                "packagesigning" or "msisign" => emoji ? "[gold3]🔏[/]" : "[gold3]SG[/]",
                "nugetpublish" => emoji ? "[yellow]🚀[/]" : "[yellow]PB[/]",
                "githubpublish" or "githubasset" => emoji ? "[yellow]🚀[/]" : "[yellow]GH[/]",
                "tools" or "toolpublish" => emoji ? "[deepskyblue1]🧰[/]" : "[deepskyblue1]TL[/]",
                "github" => emoji ? "[yellow]🚀[/]" : "[yellow]GH[/]",
                "versioning" => emoji ? "[lightskyblue1]🏷[/]" : "[lightskyblue1]VR[/]",
                "clean" => emoji ? "[grey]🧹[/]" : "[grey]CL[/]",
                "restore" => emoji ? "[green]📥[/]" : "[green]RS[/]",
                "servicelifecycle" or "commandhook" => emoji ? "[steelblue1]⚙[/]" : "[steelblue1]AC[/]",
                "benchmarkextract" or "benchmarkgate" => emoji ? "[orange3]🧪[/]" : "[orange3]TS[/]",
                "manifest" => emoji ? "[deepskyblue1]📝[/]" : "[deepskyblue1]DC[/]",
                "msiprepare" or "msibuild" or "bundle" or "storepackage" or "debianpackage" or "macapppackage" => emoji ? "[magenta]📦[/]" : "[magenta]PK[/]",
                _ => emoji ? "[grey]•[/]" : "[grey]PF[/]"
            };
        }

        return parsed switch
        {
            ModulePipelineStepKind.Versioning => emoji ? "[lightskyblue1]🏷[/]" : "[lightskyblue1]VR[/]",
            ModulePipelineStepKind.Build => emoji ? "[cyan]🔨[/]" : "[cyan]BL[/]",
            ModulePipelineStepKind.Compilation => emoji ? "[cyan]🔨[/]" : "[cyan]BL[/]",
            ModulePipelineStepKind.Documentation => emoji ? "[deepskyblue1]📝[/]" : "[deepskyblue1]DC[/]",
            ModulePipelineStepKind.Formatting => emoji ? "[mediumpurple3]🎨[/]" : "[mediumpurple3]FM[/]",
            ModulePipelineStepKind.Signing => emoji ? "[gold3]🔏[/]" : "[gold3]SG[/]",
            ModulePipelineStepKind.Validation => emoji ? "[lightskyblue1]🔎[/]" : "[lightskyblue1]VA[/]",
            ModulePipelineStepKind.Tests => emoji ? "[orange3]🧪[/]" : "[orange3]TS[/]",
            ModulePipelineStepKind.Artefact => emoji ? "[magenta]📦[/]" : "[magenta]PK[/]",
            ModulePipelineStepKind.Publish => emoji ? "[yellow]🚀[/]" : "[yellow]PB[/]",
            ModulePipelineStepKind.Install => emoji ? "[green]📥[/]" : "[green]IN[/]",
            ModulePipelineStepKind.Cleanup => emoji ? "[grey]🧹[/]" : "[grey]CL[/]",
            ModulePipelineStepKind.Action => emoji ? "[steelblue1]⚙[/]" : "[steelblue1]AC[/]",
            ModulePipelineStepKind.PackageBuild => emoji ? "[magenta]📦[/]" : "[magenta]PK[/]",
            ModulePipelineStepKind.ExternalAsset => emoji ? "[deepskyblue1]📥[/]" : "[deepskyblue1]AS[/]",
            _ => emoji ? "[grey]•[/]" : "[grey]PF[/]"
        };
    }

    private static string GetStateIcon(SpectreProgressLedgerState state, bool emoji)
        => state switch
        {
            SpectreProgressLedgerState.Completed => emoji ? "[green]✅[/]" : "[green]OK[/]",
            SpectreProgressLedgerState.Failed => emoji ? "[red]❌[/]" : "[red]X[/]",
            SpectreProgressLedgerState.Skipped => emoji ? "[grey]⏭[/]" : "[grey]SK[/]",
            _ => emoji ? "[grey]•[/]" : "[grey]PF[/]"
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

    private string PadOrEllipsis(string input, int width)
    {
        if (width <= 0)
            return string.Empty;

        input ??= string.Empty;
        var cells = input.GetCellWidth();
        if (cells <= width)
            return input + new string(' ', width - cells);

        var marker = _unicode ? "…" : ".";
        var builder = new StringBuilder();
        var used = 0;
        var elements = StringInfo.GetTextElementEnumerator(input);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            var elementWidth = element.GetCellWidth();
            if (used + elementWidth > width - 1) break;
            builder.Append(element);
            used += elementWidth;
        }
        return builder.Append(marker).Append(' ', width - used - 1).ToString();
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
