using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using PowerForge;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace PowerForge.Cli;

internal static class PipelineConsoleUi
{
    public static bool ShouldUseInteractiveView(bool outputJson, CliOptions cli)
    {
        if (outputJson || cli.Quiet || cli.NoColor) return false;

        var view = ResolveView(cli.View);
        if (view != ConsoleView.Standard) return false;

        return !ConsoleEnvironment.IsCI && AnsiConsole.Profile.Capabilities.Interactive;
    }

    public static ModulePipelineResult Run(
        ModulePipelineRunner runner,
        ModulePipelineSpec spec,
        ModulePipelinePlan plan,
        string? configPath,
        bool outputJson,
        CliOptions cli)
    {
        if (!ShouldUseInteractiveView(outputJson, cli))
        {
            // Non-interactive: keep streaming logs and avoid live regions.
            return runner.Run(spec, plan, progress: null);
        }

        var steps = ModulePipelineStep.Create(plan);
        WriteHeader(plan, configPath, steps);

        int vw = 120;
        try { vw = Math.Max(60, Console.WindowWidth); } catch { }

        bool includeBar = vw >= 120;
        bool includeElapsed = vw >= 100;

        int barWidth = includeBar ? (vw >= 160 ? 40 : vw >= 140 ? 30 : 18) : 0;
        int percentWidth = 5;
        int elapsedWidth = includeElapsed ? 5 : 0;
        int spinnerWidth = 2;
        int iconWidth = 2;
        int gaps = 10;
        int descMax = Math.Max(24, vw - (iconWidth + barWidth + percentWidth + elapsedWidth + spinnerWidth + gaps));
        int targetWidth = vw <= 100 ? 0 : 26;

        var startLookup = new ConcurrentDictionary<ProgressTask, DateTimeOffset>();
        var doneLookup = new ConcurrentDictionary<ProgressTask, TimeSpan>();
        var iconLookup = new ConcurrentDictionary<ProgressTask, string>();

        ModulePipelineResult? result = null;
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

                    var label = BuildLabel(step, ord, total, digits, descMax, targetWidth, plan);
                    var task = ctx.AddTask(label, maxValue: 1, autoStart: false);

                    tasksByKey[step.Key] = task;
                    labelsByKey[step.Key] = label;
                    iconLookup[task] = GetStepIcon(step);
                }

                var reporter = new SpectrePipelineProgressReporter(tasksByKey, labelsByKey, startLookup, doneLookup);
                result = runner.Run(spec, plan, reporter);
            });

        return result!;
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

    private static void WriteHeader(ModulePipelinePlan plan, string? configPath, ModulePipelineStep[] steps)
    {
        static string Esc(string? s) => Markup.Escape(s ?? string.Empty);

        var title = $"PowerForge • {plan.ModuleName} {plan.ResolvedVersion}";
        AnsiConsole.Write(new Rule($"[yellow bold underline]{Esc(title)}[/]") { Justification = Justify.Left });

        if (!string.IsNullOrWhiteSpace(configPath))
            AnsiConsole.MarkupLine($"[grey][[i]][/] [grey]Config:[/] {Esc(configPath)}");
        AnsiConsole.MarkupLine($"[grey][[i]][/] [grey]Project:[/] {Esc(plan.ProjectRoot)}");
        AnsiConsole.MarkupLine($"[grey][[i]][/] [grey]Planned steps:[/] {steps.Length}");

        try
        {
            int vw = Math.Max(60, Console.WindowWidth);
            if (vw >= 120)
            {
                var preview = string.Join(", ", steps.Select(s => s.Kind.ToString()));
                AnsiConsole.MarkupLine($"[grey][[i]][/] [grey]Steps:[/] {Esc(preview)}");
            }
        }
        catch { }

        AnsiConsole.WriteLine();
    }

    private static string GetStepIcon(ModulePipelineStep step)
        => step.Kind switch
        {
            ModulePipelineStepKind.Build => "[grey]BL[/]",
            ModulePipelineStepKind.Documentation => "[grey]DC[/]",
            ModulePipelineStepKind.Artefact => "[grey]PK[/]",
            ModulePipelineStepKind.Publish => "[grey]PB[/]",
            ModulePipelineStepKind.Install => "[grey]IN[/]",
            ModulePipelineStepKind.Cleanup => "[grey]CL[/]",
            _ => "[grey]PF[/]"
        };

    private static string BuildLabel(
        ModulePipelineStep step,
        int ord,
        int total,
        int digits,
        int descMax,
        int targetWidth,
        ModulePipelinePlan plan)
    {
        static string FormatId(int n, int total, int digits)
        {
            var left = Math.Max(0, n).ToString(new string('0', Math.Max(1, digits)));
            var right = Math.Max(0, total).ToString(new string('0', Math.Max(1, digits)));
            return $"{left}/{right}";
        }

        static string PadOrEllipsis(string input, int width)
        {
            if (width <= 0) return string.Empty;
            input ??= string.Empty;
            if (input.Length == width) return input;
            if (input.Length < width) return input.PadRight(width);
            if (width <= 1) return "…";
            return input.Substring(0, Math.Max(0, width - 1)) + "…";
        }

        string name = step.Kind switch
        {
            ModulePipelineStepKind.Build => "Build to staging",
            ModulePipelineStepKind.Documentation => "Generate docs",
            ModulePipelineStepKind.Artefact => "Pack artefact",
            ModulePipelineStepKind.Publish => "Publish",
            ModulePipelineStepKind.Install => "Install",
            ModulePipelineStepKind.Cleanup => "Cleanup staging",
            _ => step.Title
        };

        string target = step.Kind switch
        {
            ModulePipelineStepKind.Artefact => FormatArtefactTarget(step.ArtefactSegment),
            ModulePipelineStepKind.Publish => FormatPublishTarget(step.PublishSegment),
            ModulePipelineStepKind.Install => $"{plan.InstallStrategy}, keep {plan.InstallKeepVersions}",
            _ => string.Empty
        };

        int safeTotal = Math.Max(1, total);
        int idFieldWidth = (digits * 2) + 1;
        string idField = PadOrEllipsis(FormatId(ord, safeTotal, digits), idFieldWidth);

        if (targetWidth <= 0)
        {
            int nameWidthOnly = Math.Max(0, descMax - idFieldWidth - 1);
            string nameFieldOnly = PadOrEllipsis(name, nameWidthOnly);
            return $"{idField} {nameFieldOnly}".TrimEnd();
        }

        int nameWidth = Math.Max(0, descMax - idFieldWidth - 1 - targetWidth);
        string nameField = PadOrEllipsis(name, nameWidth);
        string targetField = PadOrEllipsis(target, targetWidth);
        return $"{idField} {nameField} {targetField}".TrimEnd();

        static string FormatArtefactTarget(ConfigurationArtefactSegment? seg)
        {
            if (seg is null) return string.Empty;
            var id = seg.Configuration?.ID;
            var label = seg.ArtefactType.ToString();
            return string.IsNullOrWhiteSpace(id) ? label : $"{label} ({id})";
        }

        static string FormatPublishTarget(ConfigurationPublishSegment? seg)
        {
            if (seg is null) return string.Empty;
            var cfg = seg.Configuration ?? new PublishConfiguration();
            var repoName = cfg.Repository?.Name ?? cfg.RepositoryName;
            if (!string.IsNullOrWhiteSpace(repoName)) return $"{cfg.Destination} ({repoName})";
            return cfg.Destination.ToString();
        }
    }

    private static ConsoleView ResolveView(ConsoleView requested)
    {
        if (requested != ConsoleView.Auto) return requested;
        var interactive = AnsiConsole.Profile.Capabilities.Interactive && !ConsoleEnvironment.IsCI;
        return interactive ? ConsoleView.Standard : ConsoleView.Ansi;
    }

    private sealed class SpectrePipelineProgressReporter : IModulePipelineProgressReporter
    {
        private readonly IReadOnlyDictionary<string, ProgressTask> _tasks;
        private readonly IReadOnlyDictionary<string, string> _labels;
        private readonly ConcurrentDictionary<ProgressTask, DateTimeOffset> _startLookup;
        private readonly ConcurrentDictionary<ProgressTask, TimeSpan> _doneLookup;

        public SpectrePipelineProgressReporter(
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

        public void StepStarting(ModulePipelineStep step)
        {
            if (step is null) return;
            if (!_tasks.TryGetValue(step.Key, out var task)) return;

            task.IsIndeterminate = true;
            task.StartTask();
            _startLookup[task] = DateTimeOffset.Now;
        }

        public void StepCompleted(ModulePipelineStep step)
        {
            if (step is null) return;
            if (!_tasks.TryGetValue(step.Key, out var task)) return;

            task.IsIndeterminate = false;
            task.Value = task.MaxValue;
            task.StopTask();

            if (_startLookup.TryGetValue(task, out var start))
                _doneLookup[task] = DateTimeOffset.Now - start;
        }

        public void StepFailed(ModulePipelineStep step, Exception error)
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

