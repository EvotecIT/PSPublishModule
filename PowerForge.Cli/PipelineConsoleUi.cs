using PowerForge;
using Spectre.Console;

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

        WriteHeader(plan, configPath, steps.Length);

        ModulePipelineResult? result = null;
        AnsiConsole.Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new ElapsedTimeColumn(),
                new SpinnerColumn(),
            })
            .Start(ctx =>
            {
                var tasks = new Dictionary<string, ProgressTask>(StringComparer.OrdinalIgnoreCase);
                var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                var total = Math.Max(1, steps.Length);
                var digits = Math.Max(2, total.ToString().Length);
                for (int i = 0; i < steps.Length; i++)
                {
                    var step = steps[i];
                    var id = $"{(i + 1).ToString(new string('0', digits))}/{total.ToString(new string('0', digits))}";
                    var title = $"PF {id} {step.Title}";

                    var task = ctx.AddTask(title, autoStart: false);
                    task.MaxValue = 1;
                    tasks[step.Key] = task;
                    labels[step.Key] = title;
                }

                var reporter = new SpectrePipelineProgressReporter(tasks, labels);
                result = runner.Run(spec, plan, reporter);
            });

        return result!;
    }

    private static void WriteHeader(ModulePipelinePlan plan, string? configPath, int stepCount)
    {
        string Esc(string? s) => Markup.Escape(s ?? string.Empty);

        var header = $"[bold yellow]PowerForge[/] • {Esc(plan.ModuleName)} {Esc(plan.ResolvedVersion)}";
        AnsiConsole.Write(new Rule(header) { Justification = Justify.Left });

        if (!string.IsNullOrWhiteSpace(configPath))
            AnsiConsole.MarkupLine($"[grey]Config:[/] {Esc(configPath)}");
        AnsiConsole.MarkupLine($"[grey]Project:[/] {Esc(plan.ProjectRoot)}");
        AnsiConsole.MarkupLine($"[grey]Steps:[/] {stepCount}");
        AnsiConsole.WriteLine();
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

        public SpectrePipelineProgressReporter(
            IReadOnlyDictionary<string, ProgressTask> tasks,
            IReadOnlyDictionary<string, string> labels)
        {
            _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
            _labels = labels ?? throw new ArgumentNullException(nameof(labels));
        }

        public void StepStarting(ModulePipelineStep step)
        {
            if (step is null) return;
            if (!_tasks.TryGetValue(step.Key, out var task)) return;

            task.IsIndeterminate = true;
            task.StartTask();
        }

        public void StepCompleted(ModulePipelineStep step)
        {
            if (step is null) return;
            if (!_tasks.TryGetValue(step.Key, out var task)) return;

            task.IsIndeterminate = false;
            task.Value = task.MaxValue;
            task.StopTask();
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
                task.Description = $"{label} (FAILED)";
            }

            task.StopTask();
        }
    }
}
