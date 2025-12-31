using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using PowerForge;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace PSPublishModule;

internal static class SpectrePipelineConsoleUi
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

    private static int ComputeBarWidth(int viewportWidth)
    {
        if (viewportWidth >= 160) return 40;
        if (viewportWidth >= 140) return 30;
        if (viewportWidth >= 120) return 18;
        if (viewportWidth >= 100) return 14;
        if (viewportWidth >= 80) return 12;
        return 10;
    }

    public static void WriteSummary(ModulePipelineResult res)
    {
        if (res is null) return;

        static string Esc(string? s) => Markup.Escape(s ?? string.Empty);       
        static string StatusMarkup(CheckStatus status)
            => status switch
            {
                CheckStatus.Pass => "[green]Pass[/]",
                CheckStatus.Warning => "[yellow]Warning[/]",
                _ => "[red]Fail[/]"
            };
        static int CountIssues(ProjectConsistencyReport report, FileConsistencySettings? settings)
        {
            if (report is null) return 0;
            if (settings is null) return report.ProblematicFiles.Length;

            int count = 0;
            foreach (var f in report.ProblematicFiles)
            {
                if (f.NeedsEncodingConversion || f.NeedsLineEndingConversion)
                {
                    count++;
                    continue;
                }

                if (settings.CheckMissingFinalNewline && f.MissingFinalNewline)
                {
                    count++;
                    continue;
                }

                if (settings.CheckMixedLineEndings && f.HasMixedLineEndings)
                {
                    count++;
                }
            }

            return count;
        }

        var unicode = AnsiConsole.Profile.Capabilities.Unicode;
        var border = unicode ? TableBorder.Rounded : TableBorder.Simple;        

        AnsiConsole.Write(new Rule($"[green]{(unicode ? "✅" : "OK")} Summary[/]").LeftJustified());

        var table = new Table()
            .Border(border)
            .AddColumn(new TableColumn("Item").NoWrap())
            .AddColumn(new TableColumn("Value"));

        table.AddRow($"{(unicode ? "📦" : "*")} Module", $"{Esc(res.Plan.ModuleName)} [grey]{Esc(res.Plan.ResolvedVersion)}[/]");
        table.AddRow($"{(unicode ? "🧪" : "*")} Staging", Esc(res.BuildResult.StagingPath));

        if (res.FileConsistencyReport is not null)
        {
            var status = res.FileConsistencyStatus ?? CheckStatus.Warning;
            var total = res.FileConsistencyReport.Summary.TotalFiles;
            var issues = CountIssues(res.FileConsistencyReport, res.Plan.FileConsistencySettings);
            var compliance = total <= 0 ? 100.0 : Math.Round(((total - issues) / (double)total) * 100.0, 1);
            table.AddRow(
                $"{(unicode ? "🔎" : "*")} File consistency",
                $"{StatusMarkup(status)} [grey]{compliance:0.0}% compliant[/]");
        }
        else
        {
            table.AddRow($"{(unicode ? "🔎" : "*")} File consistency", "[grey]Disabled[/]");
        }

        if (res.ProjectRootFileConsistencyReport is not null)
        {
            var status = res.ProjectRootFileConsistencyStatus ?? CheckStatus.Warning;
            var total = res.ProjectRootFileConsistencyReport.Summary.TotalFiles;
            var issues = CountIssues(res.ProjectRootFileConsistencyReport, res.Plan.FileConsistencySettings);
            var compliance = total <= 0 ? 100.0 : Math.Round(((total - issues) / (double)total) * 100.0, 1);
            table.AddRow(
                $"{(unicode ? "🔎" : "*")} File consistency (project)",
                $"{StatusMarkup(status)} [grey]{compliance:0.0}% compliant[/]");
        }
        else if (res.Plan.FileConsistencySettings?.Enable == true && res.Plan.FileConsistencySettings.UpdateProjectRoot)
        {
            table.AddRow($"{(unicode ? "🔎" : "*")} File consistency (project)", "[grey]Disabled[/]");
        }

        if (res.CompatibilityReport is not null)
        {
            var s = res.CompatibilityReport.Summary;
            table.AddRow(
                $"{(unicode ? "🔎" : "*")} Compatibility",
                $"{StatusMarkup(s.Status)} [grey]{s.CrossCompatibilityPercentage:0.0}% cross-compatible[/]");
        }
        else
        {
            table.AddRow($"{(unicode ? "🔎" : "*")} Compatibility", "[grey]Disabled[/]");
        }

        if (res.FormattingStagingResults is { Length: > 0 } || res.FormattingProjectResults is { Length: > 0 })
        {
            static string FormatCount(int changed, int total, string label)
            {
                var c = changed > 0 ? $"[green]{changed}[/]" : "[grey]0[/]";
                return $"{label} {c}[grey]/{total}[/]";
            }

            var parts = new List<string>(2);
            if (res.FormattingStagingResults is { Length: > 0 })
            {
                var total = res.FormattingStagingResults.Length;
                var changed = res.FormattingStagingResults.Count(r => r.Changed);
                parts.Add(FormatCount(changed, total, "staging"));
            }
            if (res.FormattingProjectResults is { Length: > 0 })
            {
                var total = res.FormattingProjectResults.Length;
                var changed = res.FormattingProjectResults.Count(r => r.Changed);
                parts.Add(FormatCount(changed, total, "project"));
            }

            table.AddRow($"{(unicode ? "🎨" : "*")} Formatting", string.Join(", ", parts));
        }
        else
        {
            table.AddRow($"{(unicode ? "🎨" : "*")} Formatting", "[grey]Disabled[/]");
        }

        if (res.ArtefactResults is { Length: > 0 })
            table.AddRow($"{(unicode ? "📦" : "*")} Artefacts", $"[green]{res.ArtefactResults.Length}[/]");
        else
            table.AddRow($"{(unicode ? "📦" : "*")} Artefacts", "[grey]None[/]");

        if (res.InstallResult is not null)
            table.AddRow($"{(unicode ? "📥" : "*")} Install", $"[green]{Esc(res.InstallResult.Version)}[/]");
        else
            table.AddRow($"{(unicode ? "📥" : "*")} Install", "[grey]Disabled[/]");

        AnsiConsole.Write(table);

        if (res.ArtefactResults is { Length: > 0 })
        {
            var artefacts = new Table()
                .Border(border)
                .AddColumn(new TableColumn("Type").NoWrap())
                .AddColumn(new TableColumn("Id").NoWrap())
                .AddColumn(new TableColumn("Path"));

            foreach (var a in res.ArtefactResults)
                artefacts.AddRow(Esc(a.Type.ToString()), Esc(a.Id ?? string.Empty), Esc(a.OutputPath));

            AnsiConsole.Write(artefacts);
        }

        if (res.InstallResult is not null && res.InstallResult.InstalledPaths is { Count: > 0 })
        {
            AnsiConsole.MarkupLine($"[grey]{(unicode ? "📍" : "*")} Installed paths:[/]");
            foreach (var path in res.InstallResult.InstalledPaths)
                AnsiConsole.MarkupLine($"  [grey]{(unicode ? "→" : "->")}[/] {Esc(path)}");
        }
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

    private static void WriteHeader(ModulePipelinePlan plan, string? configLabel, ModulePipelineStep[] steps)
    {
        static string Esc(string? s) => Markup.Escape(s ?? string.Empty);

        var unicode = AnsiConsole.Profile.Capabilities.Unicode;

        var title = unicode
            ? $"🛠️ PowerForge • {plan.ModuleName} {plan.ResolvedVersion}"
            : $"PowerForge • {plan.ModuleName} {plan.ResolvedVersion}";
        AnsiConsole.Write(new Rule($"[yellow bold underline]{Esc(title)}[/]") { Justification = Justify.Left });

        var info = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(new TableColumn("k").NoWrap())
            .AddColumn(new TableColumn("v"));

        var cfgText = string.IsNullOrWhiteSpace(configLabel) ? "(dsl)" : configLabel;
        info.AddRow($"[grey]{(unicode ? "⚙️" : "CFG")}[/] [grey]Config[/]", Esc(cfgText));
        info.AddRow($"[grey]{(unicode ? "📁" : "DIR")}[/] [grey]Project[/]", Esc(plan.ProjectRoot));

        var stagingText = string.IsNullOrWhiteSpace(plan.BuildSpec.StagingPath) ? "(temp)" : plan.BuildSpec.StagingPath;
        info.AddRow($"[grey]{(unicode ? "🧪" : "TMP")}[/] [grey]Staging[/]", Esc(stagingText));

        var frameworks = plan.BuildSpec.Frameworks is { Length: > 0 }
            ? string.Join(", ", plan.BuildSpec.Frameworks)
            : "(auto)";
        info.AddRow($"[grey]{(unicode ? "🧩" : "TFM")}[/] [grey]Frameworks[/]", Esc(frameworks));

        var docsEnabled = plan.DocumentationBuild?.Enable == true;
        info.AddRow($"[grey]{(unicode ? "📚" : "DOC")}[/] [grey]Docs[/]", docsEnabled ? "[green]Enabled[/]" : "[grey]Disabled[/]");

        var validations = new List<string>();
        if (plan.FileConsistencySettings?.Enable == true) validations.Add("File consistency");
        if (plan.CompatibilitySettings?.Enable == true) validations.Add("Compatibility");
        info.AddRow(
            $"[grey]{(unicode ? "🔎" : "VAL")}[/] [grey]Validation[/]",
            validations.Count == 0 ? "[grey]Disabled[/]" : Esc(string.Join(", ", validations)));

        info.AddRow($"[grey]{(unicode ? "📦" : "PKG")}[/] [grey]Artefacts[/]", Esc((plan.Artefacts?.Length ?? 0).ToString()));
        info.AddRow($"[grey]{(unicode ? "🚀" : "PUB")}[/] [grey]Publishes[/]", Esc((plan.Publishes?.Length ?? 0).ToString()));
        info.AddRow($"[grey]{(unicode ? "📥" : "INS")}[/] [grey]Install[/]", plan.InstallEnabled ? Esc($"{plan.InstallStrategy}, keep {plan.InstallKeepVersions}") : "[grey]Disabled[/]");

        info.AddRow($"[grey]{(unicode ? "🧭" : "STP")}[/] [grey]Steps[/]", Esc(steps.Length.ToString()));
        AnsiConsole.Write(info);

        AnsiConsole.WriteLine();
    }

    private static string GetStepIcon(ModulePipelineStep step)
    {
        var unicode = AnsiConsole.Profile.Capabilities.Unicode;
        return step.Kind switch
        {
            ModulePipelineStepKind.Build => unicode ? "[cyan]🔨[/]" : "[cyan]BL[/]",
            ModulePipelineStepKind.Documentation => unicode ? "[deepskyblue1]📝[/]" : "[deepskyblue1]DC[/]",
            ModulePipelineStepKind.Formatting => unicode ? "[mediumpurple3]🎨[/]" : "[mediumpurple3]FM[/]",
            ModulePipelineStepKind.Validation => unicode ? "[lightskyblue1]🔎[/]" : "[lightskyblue1]VA[/]",
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
