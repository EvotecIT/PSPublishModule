using System;
using System.Collections.Concurrent;
using PowerForge;
using Spectre.Console;

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
    private readonly bool _unicode;
    private readonly bool _includeBar;
    private readonly bool _includeElapsed;
    private readonly int _barWidth;
    private readonly int _descriptionWidth;
    private readonly int _targetWidth;

    internal SpectreProgressPresentation(int viewportWidth, bool unicode)
    {
        var width = Math.Max(60, viewportWidth);
        _unicode = unicode;
        _includeElapsed = width >= 100;
        _barWidth = ComputeBarWidth(width);
        _includeBar = _barWidth > 0;

        const int percentWidth = 5;
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
            _completed);

    internal string BuildLabel(SpectreProgressLedgerItem item, string? detail)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));

        var ordinal = item.Position > 0 && item.Total > 0
            ? FormatOrdinal(item.Position, item.Total)
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
        _started.TryAdd(task, DateTimeOffset.Now);
    }

    internal void MarkTerminal(
        ProgressTask task,
        SpectreProgressLedgerState state,
        TimeSpan? duration = null)
    {
        if (task is null) throw new ArgumentNullException(nameof(task));

        _icons[task] = GetStateIcon(state, _unicode);
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
    }

    internal static string GetKindIcon(string? kind, bool unicode)
    {
        if (!Enum.TryParse(kind, ignoreCase: true, out ModulePipelineStepKind parsed))
        {
            return kind?.Trim().ToLowerInvariant() switch
            {
                "module" => unicode ? "[cyan]🔨[/]" : "[cyan]BL[/]",
                "packages" => unicode ? "[magenta]📦[/]" : "[magenta]PK[/]",
                "tools" => unicode ? "[deepskyblue1]🧰[/]" : "[deepskyblue1]TL[/]",
                "github" => unicode ? "[yellow]🚀[/]" : "[yellow]GH[/]",
                "versioning" => unicode ? "[lightskyblue1]🏷[/]" : "[lightskyblue1]VR[/]",
                _ => unicode ? "[grey]•[/]" : "[grey]PF[/]"
            };
        }

        return parsed switch
        {
            ModulePipelineStepKind.Versioning => unicode ? "[lightskyblue1]🏷[/]" : "[lightskyblue1]VR[/]",
            ModulePipelineStepKind.Build => unicode ? "[cyan]🔨[/]" : "[cyan]BL[/]",
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
        return 10;
    }

    private static string FormatOrdinal(int position, int total)
    {
        var safeTotal = Math.Max(1, total);
        var digits = Math.Max(2, safeTotal.ToString().Length);
        var format = new string('0', digits);
        return $"{Math.Max(0, position).ToString(format)}/{safeTotal.ToString(format)}";
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
}
