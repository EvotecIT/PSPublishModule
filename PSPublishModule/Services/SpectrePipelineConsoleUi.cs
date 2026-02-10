using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using PowerForge;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace PSPublishModule;

internal static partial class SpectrePipelineConsoleUi
{
    public static bool ShouldUseInteractiveView(bool isVerbose)
    {
        if (isVerbose) return false;
        if (Console.IsOutputRedirected || Console.IsErrorRedirected) return false;
        return !ConsoleEnvironment.IsCI && AnsiConsole.Profile.Capabilities.Interactive;
    }

    public static ModulePipelineResult RunInteractive(
        ModulePipelineRunner runner,
        ModulePipelineSpec spec,
        ModulePipelinePlan plan,
        string? configLabel)
    {
        if (runner is null) throw new ArgumentNullException(nameof(runner));
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        if (plan is null) throw new ArgumentNullException(nameof(plan));

        var steps = ModulePipelineStep.Create(plan);
        WriteHeader(plan, configLabel, steps);

        int vw = 120;
        try { vw = Math.Max(60, Console.WindowWidth); } catch { }

        bool includeElapsed = vw >= 100;

        int barWidth = ComputeBarWidth(vw);
        bool includeBar = barWidth > 0;
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

        Exception? failure = null;
        ModulePipelineResult? result = null;
        AnsiConsole.Progress()
            .AutoRefresh(true)
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(BuildColumns(includeBar, includeElapsed, barWidth, iconLookup, startLookup, doneLookup))
            .Start(ctx =>
            {
                var tasksByKey = new Dictionary<string, ProgressTask>(StringComparer.OrdinalIgnoreCase);

                var total = Math.Max(1, steps.Length);
                var digits = Math.Max(2, total.ToString().Length);        

                for (int i = 0; i < steps.Length; i++)
                {
                    var step = steps[i];
                    int ord = i + 1;

                    var label = BuildLabel(step, ord, total, digits, descMax, targetWidth, plan);
                    var task = ctx.AddTask(label, maxValue: 1, autoStart: false);

                    tasksByKey[step.Key] = task;
                    iconLookup[task] = GetStepIcon(step);
                }

                var reporter = new SpectrePipelineProgressReporter(tasksByKey, iconLookup, startLookup, doneLookup);

                using var refreshCts = new CancellationTokenSource();
                var refresher = StartRefreshLoop(ctx, refreshCts.Token);
                try
                {
                    result = runner.Run(spec, plan, reporter);
                }
                catch (Exception ex)
                {
                    failure = ex;
                    AbortRemainingTasks(tasksByKey, iconLookup, startLookup, doneLookup);
                }
                finally
                {
                    refreshCts.Cancel();
                    try { refresher.GetAwaiter().GetResult(); } catch { /* best effort */ }
                }
            });

        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();

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

    private static Task StartRefreshLoop(ProgressContext ctx, CancellationToken token)
        => Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try { ctx.Refresh(); } catch { /* best effort */ }
                try { await Task.Delay(100, token).ConfigureAwait(false); } catch { }
            }
        }, token);

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

    private static void WriteHeader(ModulePipelinePlan plan, string? configLabel, ModulePipelineStep[] steps)
    {
        static string Esc(string? s) => Markup.Escape(s ?? string.Empty);
        static string Icon(string? s) => Esc(NormalizeIcon(s));

        var unicode = AnsiConsole.Profile.Capabilities.Unicode;

        var title = unicode
            ? $"🛠️ PowerForge • {plan.ModuleName} {plan.ResolvedVersion}"
            : $"PowerForge • {plan.ModuleName} {plan.ResolvedVersion}";
        AnsiConsole.Write(new Rule($"[yellow bold underline]{Esc(title)}[/]") { Justification = Justify.Left });

        var iconColWidth = unicode ? 2 : 3;
        var info = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(BuildHeaderIconColumn(iconColWidth))
            .AddColumn(BuildHeaderKeyColumn())
            .AddColumn(BuildHeaderValueColumn());

        void AddInfoRow(string icon, string label, string valueMarkup)
            => info.AddRow($"[grey]{Icon(icon)}[/]", $"[grey]{Esc(label)}[/]", valueMarkup);

        var cfgText = string.IsNullOrWhiteSpace(configLabel) ? "(dsl)" : configLabel;
        AddInfoRow(unicode ? "⚙️" : "CFG", "Config", Esc(cfgText));
        AddInfoRow(unicode ? "📁" : "DIR", "Project", Esc(plan.ProjectRoot));

        var stagingText = string.IsNullOrWhiteSpace(plan.BuildSpec.StagingPath) ? "(temp)" : plan.BuildSpec.StagingPath;
        AddInfoRow(unicode ? "🧪" : "TMP", "Staging", Esc(stagingText));

        var frameworks = plan.BuildSpec.Frameworks is { Length: > 0 }
            ? string.Join(", ", plan.BuildSpec.Frameworks)
            : "(auto)";
        AddInfoRow(unicode ? "🧩" : "TFM", "Frameworks", Esc(frameworks));

        var docsEnabled = plan.DocumentationBuild?.Enable == true;
        AddInfoRow(unicode ? "📚" : "DOC", "Docs", docsEnabled ? "[green]Enabled[/]" : "[grey]Disabled[/]");

        var validations = new List<string>();
        if (plan.FileConsistencySettings?.Enable == true) validations.Add("File consistency");
        if (plan.CompatibilitySettings?.Enable == true) validations.Add("Compatibility");
        if (plan.ValidationSettings?.Enable == true) validations.Add("Module validation");
        AddInfoRow(
            unicode ? "🔎" : "VAL",
            "Validation",
            validations.Count == 0 ? "[grey]Disabled[/]" : Esc(string.Join(", ", validations)));

        AddInfoRow(unicode ? "📦" : "PKG", "Artefacts", Esc((plan.Artefacts?.Length ?? 0).ToString()));
        AddInfoRow(unicode ? "🚀" : "PUB", "Publishes", Esc((plan.Publishes?.Length ?? 0).ToString()));
        AddInfoRow(unicode ? "📥" : "INS", "Install", plan.InstallEnabled ? Esc($"{plan.InstallStrategy}, keep {plan.InstallKeepVersions}") : "[grey]Disabled[/]");

        AddInfoRow(unicode ? "🧭" : "STP", "Steps", Esc(steps.Length.ToString()));
        AnsiConsole.Write(info);

        AnsiConsole.WriteLine();
    }

    private static string NormalizeIcon(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon)) return string.Empty;
        // Strip variation selectors so terminal glyph width matches Spectre's measurement more consistently.
        return icon!.Replace("\uFE0F", string.Empty).Replace("\uFE0E", string.Empty);
    }

    private static TableColumn BuildHeaderIconColumn(int width)
    {
        var col = new TableColumn("i").NoWrap().Width(width);
        // Table uses per-column padding; remove left padding so the header aligns with progress output.
        col.Padding = new Padding(0, 0, 1, 0);
        return col;
    }

    private static TableColumn BuildHeaderKeyColumn()
    {
        var col = new TableColumn("k").NoWrap();
        col.Padding = new Padding(0, 0, 1, 0);
        return col;
    }

    private static TableColumn BuildHeaderValueColumn()
    {
        var col = new TableColumn("v");
        col.Padding = new Padding(0, 0, 0, 0);
        return col;
    }

    private static string GetStepIcon(ModulePipelineStep step)
    {
        var unicode = AnsiConsole.Profile.Capabilities.Unicode;
        return step.Kind switch
        {
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
            _ => unicode ? "[grey]•[/]" : "[grey]PF[/]"
        };
    }

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
            ModulePipelineStepKind.Build => step.Title,
            ModulePipelineStepKind.Documentation => step.Title,
            ModulePipelineStepKind.Tests => step.Title,
            ModulePipelineStepKind.Artefact => "Pack",
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

    private sealed class SpectrePipelineProgressReporter : IModulePipelineProgressReporter
    {
        private readonly IReadOnlyDictionary<string, ProgressTask> _tasks;
        private readonly ConcurrentDictionary<ProgressTask, string> _icons;
        private readonly ConcurrentDictionary<ProgressTask, DateTimeOffset> _startLookup;
        private readonly ConcurrentDictionary<ProgressTask, TimeSpan> _doneLookup;
        private readonly bool _unicode;

        public SpectrePipelineProgressReporter(
            IReadOnlyDictionary<string, ProgressTask> tasks,
            ConcurrentDictionary<ProgressTask, string> icons,
            ConcurrentDictionary<ProgressTask, DateTimeOffset> startLookup,
            ConcurrentDictionary<ProgressTask, TimeSpan> doneLookup)      
        {
            _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
            _icons = icons ?? throw new ArgumentNullException(nameof(icons));
            _startLookup = startLookup ?? throw new ArgumentNullException(nameof(startLookup));
            _doneLookup = doneLookup ?? throw new ArgumentNullException(nameof(doneLookup));
            _unicode = AnsiConsole.Profile.Capabilities.Unicode;
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
            _icons[task] = StatusIcon(StepUiStatus.Completed, _unicode);

            if (_startLookup.TryGetValue(task, out var start))
                _doneLookup[task] = DateTimeOffset.Now - start;
        }

        public void StepFailed(ModulePipelineStep step, Exception error)  
        {
            if (step is null) return;
            if (!_tasks.TryGetValue(step.Key, out var task)) return;      

            task.IsIndeterminate = false;
            task.Value = task.MaxValue;
            _icons[task] = StatusIcon(StepUiStatus.Failed, _unicode);

            task.StopTask();

            if (_startLookup.TryGetValue(task, out var start))
                _doneLookup[task] = DateTimeOffset.Now - start;
        }
    }

    private static void AbortRemainingTasks(
        IReadOnlyDictionary<string, ProgressTask> tasksByKey,
        ConcurrentDictionary<ProgressTask, string> iconLookup,
        ConcurrentDictionary<ProgressTask, DateTimeOffset> startLookup,   
        ConcurrentDictionary<ProgressTask, TimeSpan> doneLookup)
    {
        if (tasksByKey is null) return;
        if (iconLookup is null) return;
        if (startLookup is null) return;
        if (doneLookup is null) return;

        // Stop any running step and mark the rest as skipped. This ensures the interactive UI closes cleanly
        // and does not leave indeterminate spinners running when an exception escapes the pipeline runner.
        var unicode = AnsiConsole.Profile.Capabilities.Unicode;
        foreach (var entry in tasksByKey)
        {
            var key = entry.Key;
            var task = entry.Value;

            if (task is null) continue;

            if (startLookup.ContainsKey(task))
            {
                if (!doneLookup.ContainsKey(task))
                {
                    task.IsIndeterminate = false;
                    task.Value = task.MaxValue;
                    iconLookup[task] = StatusIcon(StepUiStatus.Failed, unicode);
                    try { task.StopTask(); } catch { /* best effort */ }  

                    if (startLookup.TryGetValue(task, out var start))     
                        doneLookup[task] = DateTimeOffset.Now - start;    
                }

                continue;
            }

            iconLookup[task] = StatusIcon(StepUiStatus.Skipped, unicode);
        }
    }

    private enum StepUiStatus
    {
        Completed,
        Failed,
        Skipped
    }

    private static string StatusIcon(StepUiStatus status, bool unicode)
        => status switch
        {
            StepUiStatus.Completed => unicode ? "[green]✅[/]" : "[green]OK[/]",
            StepUiStatus.Failed => unicode ? "[red]❌[/]" : "[red]X[/]",
            StepUiStatus.Skipped => unicode ? "[grey]⏭[/]" : "[grey]SK[/]",
            _ => unicode ? "[grey]•[/]" : "[grey]?[/]"
        };

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

