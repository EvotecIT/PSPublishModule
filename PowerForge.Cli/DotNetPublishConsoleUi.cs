using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using PowerForge;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace PowerForge.Cli;

internal static class DotNetPublishConsoleUi
{
    public static bool ShouldUseInteractiveView(bool outputJson, CliOptions cli)
    {
        if (outputJson || cli.Quiet || cli.NoColor || cli.Verbose) return false;
        if (Console.IsOutputRedirected || Console.IsErrorRedirected) return false;

        var view = ResolveView(cli.View);
        if (view != ConsoleView.Standard) return false;

        return !ConsoleEnvironment.IsCI && AnsiConsole.Profile.Capabilities.Interactive;
    }

    public static DotNetPublishResult Run(
        DotNetPublishPipelineRunner runner,
        DotNetPublishPlan plan,
        string? configPath,
        bool outputJson,
        CliOptions cli)
    {
        if (!ShouldUseInteractiveView(outputJson, cli))
            return runner.Run(plan, progress: null);

        WriteHeader(plan, configPath);

        int vw = 120;
        try { vw = Math.Max(60, Console.WindowWidth); } catch { }

        bool includeElapsed = vw >= 100;

        int barWidth = ComputeBarWidth(vw);
        bool includeBar = barWidth > 0;

        var startLookup = new ConcurrentDictionary<ProgressTask, DateTimeOffset>();
        var doneLookup = new ConcurrentDictionary<ProgressTask, TimeSpan>();
        var iconLookup = new ConcurrentDictionary<ProgressTask, string>();

        DotNetPublishResult? result = null;
        var steps = plan.Steps ?? Array.Empty<DotNetPublishStep>();
        AnsiConsole.Progress()
            .AutoRefresh(true)
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(BuildColumns(includeBar, includeElapsed, barWidth, iconLookup, startLookup, doneLookup))
            .Start(ctx =>
            {
                var tasksByKey = new Dictionary<string, ProgressTask>(StringComparer.OrdinalIgnoreCase);
                var labelsByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                var total = Math.Max(1, steps.Length);
                var digits = Math.Max(2, total.ToString().Length);

                for (int i = 0; i < steps.Length; i++)
                {
                    var step = steps[i];
                    int ord = i + 1;

                    var label = BuildLabel(step, ord, total, digits, plan);
                    var task = ctx.AddTask(label, maxValue: 1, autoStart: false);

                    tasksByKey[step.Key] = task;
                    labelsByKey[step.Key] = label;
                    iconLookup[task] = GetStepIcon(step);
                }

                var reporter = new SpectreDotNetPublishProgressReporter(tasksByKey, labelsByKey, startLookup, doneLookup);
                result = runner.Run(plan, reporter);
            });

        WriteSummary(plan, result!);
        return result!;
    }

    private static int ComputeBarWidth(int viewportWidth)
    {
        if (viewportWidth >= 160) return 40;
        if (viewportWidth >= 140) return 30;
        if (viewportWidth >= 120) return 18;
        if (viewportWidth >= 100) return 14;
        if (viewportWidth >= 80) return 12;
        return 10;
    }

    private static void WriteHeader(DotNetPublishPlan plan, string? configPath)
    {
        static string Esc(string? s) => Markup.Escape(s ?? string.Empty);

        var unicode = AnsiConsole.Profile.Capabilities.Unicode;
        var titleTarget = !string.IsNullOrWhiteSpace(plan.SolutionPath)
            ? Path.GetFileName(plan.SolutionPath)
            : plan.ProjectRoot;

        var title = unicode
            ? $"📦 dotnet publish • {titleTarget}"
            : $"dotnet publish • {titleTarget}";

        AnsiConsole.Write(new Rule($"[yellow bold underline]{Esc(title)}[/]") { Justification = Justify.Left });

        var info = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(new TableColumn("k").NoWrap())
            .AddColumn(new TableColumn("v"));

        var cfgText = string.IsNullOrWhiteSpace(configPath) ? "(discovered)" : configPath;
        info.AddRow($"[grey]{(unicode ? "⚙️" : "CFG")}[/] [grey]Config[/]", Esc(cfgText));
        info.AddRow($"[grey]{(unicode ? "📁" : "DIR")}[/] [grey]Project[/]", Esc(plan.ProjectRoot));
        if (!string.IsNullOrWhiteSpace(plan.SolutionPath))
            info.AddRow($"[grey]{(unicode ? "🧩" : "SLN")}[/] [grey]Solution[/]", Esc(plan.SolutionPath));
        info.AddRow($"[grey]{(unicode ? "⚙️" : "CFG")}[/] [grey]Configuration[/]", Esc(plan.Configuration));

        var runtimes = plan.Targets
            .SelectMany(t => t.Publish.Runtimes ?? Array.Empty<string>())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        info.AddRow($"[grey]{(unicode ? "🎯" : "TGT")}[/] [grey]Targets[/]", plan.Targets.Length.ToString());
        info.AddRow($"[grey]{(unicode ? "🖥️" : "RID")}[/] [grey]Runtimes[/]", runtimes.Length == 0 ? "(none)" : string.Join(", ", runtimes));
        info.AddRow($"[grey]{(unicode ? "🧾" : "STP")}[/] [grey]Steps[/]", (plan.Steps?.Length ?? 0).ToString());

        AnsiConsole.Write(info);
        AnsiConsole.WriteLine();
    }

    private static void WriteSummary(DotNetPublishPlan plan, DotNetPublishResult result)
    {
        var unicode = AnsiConsole.Profile.Capabilities.Unicode;
        var title = unicode
            ? (result.Succeeded ? "✅ Summary" : "❌ Summary")
            : "Summary";

        AnsiConsole.Write(new Rule($"[grey]{Markup.Escape(title)}[/]") { Justification = Justify.Left });

        var totalBytes = result.Artefacts?.Sum(a => a.TotalBytes) ?? 0L;

        var summary = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn(new TableColumn("[grey]Item[/]").NoWrap())
            .AddColumn(new TableColumn("Value"));

        summary.AddRow("Succeeded", result.Succeeded ? "[green]true[/]" : "[red]false[/]");
        summary.AddRow("Artefacts", (result.Artefacts?.Length ?? 0).ToString());
        summary.AddRow("Bytes", totalBytes.ToString("N0"));
        if (!string.IsNullOrWhiteSpace(result.ManifestJsonPath))
            summary.AddRow("Manifest", Markup.Escape(result.ManifestJsonPath));

        if (!result.Succeeded && result.Failure is not null)
        {
            var step = $"{result.Failure.StepKind} ({result.Failure.StepKey})";
            summary.AddRow("Step", Markup.Escape(step));
            if (!string.IsNullOrWhiteSpace(result.Failure.LogPath))
                summary.AddRow("Log", Markup.Escape(result.Failure.LogPath));
        }

        if (!result.Succeeded && !string.IsNullOrWhiteSpace(result.ErrorMessage))
            summary.AddRow("Error", $"[red]{Markup.Escape(result.ErrorMessage)}[/]");

        AnsiConsole.Write(summary);

        if (!result.Succeeded && result.Failure is not null)
        {
            var tail = !string.IsNullOrWhiteSpace(result.Failure.StdErrTail)
                ? result.Failure.StdErrTail
                : result.Failure.StdOutTail;

            if (!string.IsNullOrWhiteSpace(tail))
            {
                var header = unicode ? "📄 Output tail" : "Output tail";
                AnsiConsole.WriteLine();
                AnsiConsole.Write(new Rule($"[grey]{Markup.Escape(header)}[/]") { Justification = Justify.Left });

                var panel = new Panel(new Text(tail.TrimEnd()))
                {
                    Border = BoxBorder.Rounded,
                    Padding = new Padding(1, 0, 1, 0)
                };
                panel.Header = new PanelHeader(string.IsNullOrWhiteSpace(result.Failure.StdErrTail) ? "stdout" : "stderr");

                AnsiConsole.Write(panel);
                AnsiConsole.WriteLine();
            }
        }

        if (result.Artefacts is null || result.Artefacts.Length == 0) return;

        var artefacts = new Table()
            .Border(TableBorder.Simple)
            .AddColumn(new TableColumn("Target").NoWrap())
            .AddColumn(new TableColumn("Framework").NoWrap())
            .AddColumn(new TableColumn("Runtime").NoWrap())
            .AddColumn(new TableColumn("Style").NoWrap())
            .AddColumn(new TableColumn("Output"))
            .AddColumn(new TableColumn("Zip"));

        foreach (var a in result.Artefacts)
        {
            artefacts.AddRow(
                Markup.Escape(a.Target),
                Markup.Escape(a.Framework),
                Markup.Escape(a.Runtime),
                Markup.Escape(a.Style.ToString()),
                Markup.Escape(a.OutputDir),
                string.IsNullOrWhiteSpace(a.ZipPath) ? string.Empty : Markup.Escape(a.ZipPath));
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(artefacts);
        AnsiConsole.WriteLine();
    }

    private static ProgressColumn[] BuildColumns(
        bool includeBar,
        bool includeElapsed,
        int barWidth,
        ConcurrentDictionary<ProgressTask, string> iconLookup,
        ConcurrentDictionary<ProgressTask, DateTimeOffset> startLookup,
        ConcurrentDictionary<ProgressTask, TimeSpan> doneLookup)
    {
        var columns = new List<ProgressColumn>
        {
            new StepIconColumn(iconLookup),
            new IconAndDescriptionColumn(string.Empty),
        };

        if (includeBar)
        {
            columns.Add(new ProgressBarColumn
            {
                Width = barWidth,
                CompletedStyle = new Style(Color.Green),
                FinishedStyle = new Style(Color.Green),
                IndeterminateStyle = new Style(Color.Grey)
            });
        }

        columns.Add(new PercentageColumn());

        if (includeElapsed)
            columns.Add(new FixedElapsedColumn(startLookup, doneLookup));

        columns.Add(new SpinnerColumn());
        return columns.ToArray();
    }

    private static string BuildLabel(DotNetPublishStep step, int ord, int total, int digits, DotNetPublishPlan plan)
    {
        var prefix = ord.ToString().PadLeft(digits, '0') + "/" + total.ToString().PadLeft(digits, '0');
        if (step.Kind == DotNetPublishStepKind.Publish)
        {
            var target = step.TargetName ?? string.Empty;
            var rid = step.Runtime ?? string.Empty;

            var tp = plan.Targets.FirstOrDefault(t => t.Name.Equals(target, StringComparison.OrdinalIgnoreCase));
            var framework = step.Framework ?? tp?.Publish.Framework;
            var style = tp?.Publish.Style.ToString();

            var details = new List<string>();
            if (!string.IsNullOrWhiteSpace(rid)) details.Add(rid);
            if (!string.IsNullOrWhiteSpace(framework)) details.Add(framework!);
            if (!string.IsNullOrWhiteSpace(style)) details.Add(style!);

            var suffix = details.Count > 0 ? $" ({string.Join(", ", details)})" : string.Empty;
            return $"{prefix} Publish {target}{suffix}".Trim();
        }

        var title = string.IsNullOrWhiteSpace(step.Title) ? step.Kind.ToString() : step.Title.Trim();
        if (step.Kind != DotNetPublishStepKind.Publish && !string.IsNullOrWhiteSpace(step.Runtime))
            title = $"{title} ({step.Runtime})";
        return $"{prefix} {title}".Trim();
    }

    private static string GetStepIcon(DotNetPublishStep step)
    {
        var unicode = AnsiConsole.Profile.Capabilities.Unicode;
        if (!unicode)
        {
            return step.Kind switch
            {
                DotNetPublishStepKind.Restore => "[grey]RS[/]",
                DotNetPublishStepKind.Clean => "[grey]CL[/]",
                DotNetPublishStepKind.Build => "[grey]BL[/]",
                DotNetPublishStepKind.Publish => "[grey]PB[/]",
                DotNetPublishStepKind.Manifest => "[grey]MF[/]",
                _ => "[grey]?[/]",
            };
        }

        return step.Kind switch
        {
            DotNetPublishStepKind.Restore => "[blue]📥[/]",
            DotNetPublishStepKind.Clean => "[yellow]🧹[/]",
            DotNetPublishStepKind.Build => "[yellow]🔨[/]",
            DotNetPublishStepKind.Publish => "[green]📦[/]",
            DotNetPublishStepKind.Manifest => "[blue]📝[/]",
            _ => "[grey]•[/]",
        };
    }

    private static ConsoleView ResolveView(ConsoleView requested)
    {
        if (requested != ConsoleView.Auto) return requested;
        var interactive = AnsiConsole.Profile.Capabilities.Interactive && !ConsoleEnvironment.IsCI;
        return interactive ? ConsoleView.Standard : ConsoleView.Ansi;
    }

    private sealed class SpectreDotNetPublishProgressReporter : IDotNetPublishProgressReporter
    {
        private readonly IReadOnlyDictionary<string, ProgressTask> _tasks;
        private readonly IReadOnlyDictionary<string, string> _labels;
        private readonly ConcurrentDictionary<ProgressTask, DateTimeOffset> _startLookup;
        private readonly ConcurrentDictionary<ProgressTask, TimeSpan> _doneLookup;

        public SpectreDotNetPublishProgressReporter(
            IReadOnlyDictionary<string, ProgressTask> tasks,
            IReadOnlyDictionary<string, string> labels,
            ConcurrentDictionary<ProgressTask, DateTimeOffset> startLookup,
            ConcurrentDictionary<ProgressTask, TimeSpan> doneLookup)
        {
            _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
            _labels = labels ?? throw new ArgumentNullException(nameof(labels));
            _startLookup = startLookup ?? throw new ArgumentNullException(nameof(startLookup));
            _doneLookup = doneLookup ?? throw new ArgumentNullException(nameof(doneLookup));
        }

        public void StepStarting(DotNetPublishStep step)
        {
            if (step is null) return;
            if (!_tasks.TryGetValue(step.Key, out var task)) return;

            task.IsIndeterminate = true;
            task.StartTask();
            _startLookup[task] = DateTimeOffset.Now;
        }

        public void StepCompleted(DotNetPublishStep step)
        {
            if (step is null) return;
            if (!_tasks.TryGetValue(step.Key, out var task)) return;

            task.IsIndeterminate = false;
            task.Value = task.MaxValue;
            task.StopTask();

            if (_startLookup.TryGetValue(task, out var start))
                _doneLookup[task] = DateTimeOffset.Now - start;
        }

        public void StepFailed(DotNetPublishStep step, Exception error)
        {
            if (step is null) return;
            if (!_tasks.TryGetValue(step.Key, out var task)) return;

            task.IsIndeterminate = false;
            task.Value = task.MaxValue;

            if (_labels.TryGetValue(step.Key, out var label))
            {
                // Keep it plain (no markup) to avoid rendering issues in live regions.
                task.Description = $"{label} FAILED";
            }

            task.StopTask();

            if (_startLookup.TryGetValue(task, out var start))
                _doneLookup[task] = DateTimeOffset.Now - start;
        }
    }

    private sealed class StepIconColumn : ProgressColumn
    {
        private readonly ConcurrentDictionary<ProgressTask, string> _icons;

        public StepIconColumn(ConcurrentDictionary<ProgressTask, string> icons) => _icons = icons;

        public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
        {
            var icon = _icons.TryGetValue(task, out var s) && !string.IsNullOrWhiteSpace(s) ? s : string.Empty;
            var content = new Markup(icon);
            return new Panel(content)
            {
                Border = BoxBorder.None,
                Padding = new Padding(0, 0, 0, 0),
                Width = 2
            };
        }
    }

    private sealed class IconAndDescriptionColumn : ProgressColumn
    {
        private readonly string _icon;

        public IconAndDescriptionColumn(string icon = "•") => _icon = icon;

        public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
        {
            var desc = task.Description ?? string.Empty;
            var prefix = string.IsNullOrWhiteSpace(_icon) ? string.Empty : _icon + " ";
            var t = new Text(prefix + desc);
            try { t.Overflow = Overflow.Ellipsis; } catch { }
            return t;
        }
    }

    private sealed class FixedElapsedColumn : ProgressColumn
    {
        private readonly ConcurrentDictionary<ProgressTask, DateTimeOffset> _start;
        private readonly ConcurrentDictionary<ProgressTask, TimeSpan> _done;

        public FixedElapsedColumn(ConcurrentDictionary<ProgressTask, DateTimeOffset> start, ConcurrentDictionary<ProgressTask, TimeSpan> done)
        {
            _start = start;
            _done = done;
        }

        public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
        {
            if (_done.TryGetValue(task, out var e)) return new Markup($"[blue]{e:mm\\:ss}[/]");
            if (_start.TryGetValue(task, out var s))
            {
                var e2 = DateTimeOffset.Now - s;
                return new Markup($"[blue]{e2:mm\\:ss}[/]");
            }
            return new Markup("[blue]00:00[/]");
        }
    }
}
