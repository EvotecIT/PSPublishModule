using System;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using ConsoleMonster.Layout;
using ConsoleMonster.Text;
using ConsoleMonster.Widgets;
using PowerForge;

namespace PowerForge.ConsoleUi;

/// <summary>Renders executable publishing (dotnet publish, MSI, store and benchmark steps) with the shared build view.</summary>
internal static partial class ExecutablePublishConsoleUi
{
    internal static DotNetPublishResult RunInteractive(
        DotNetPublishPlan plan,
        string? configPath,
        Func<IDotNetPublishProgressReporter, DotNetPublishResult> run)
        => RunInteractive(new ProgressView(), plan, configPath, run);

    internal static DotNetPublishResult RunInteractive(
        ProgressView view,
        DotNetPublishPlan plan,
        string? configPath,
        Func<IDotNetPublishProgressReporter, DotNetPublishResult> run)
    {
        if (view is null) throw new ArgumentNullException(nameof(view));
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (run is null) throw new ArgumentNullException(nameof(run));

        WriteHeader(plan, configPath);
        var steps = plan.Steps ?? Array.Empty<DotNetPublishStep>();
        foreach (var step in steps)
            view.Step(step.Key, GetStepTitle(step, plan), step.Kind == DotNetPublishStepKind.Publish ? "Packages" : step.Kind.ToString());

        DotNetPublishResult? result = null;
        Exception? failure = null;
        view.Run(() =>
        {
            try
            {
                result = run(new Reporter(view));
                view.FinishRemaining(result.Succeeded);
            }
            catch (Exception ex)
            {
                failure = ex;
                view.FinishRemaining(success: false);
            }
        });

        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        WriteSummary(plan, result!);
        return result!;
    }

    private static void WriteHeader(DotNetPublishPlan plan, string? configPath)
    {
        var titleTarget = !string.IsNullOrWhiteSpace(plan.SolutionPath)
            ? Path.GetFileName(plan.SolutionPath)
            : plan.ProjectRoot;
        var runtimes = plan.Targets
            .SelectMany(t => t.Publish.Runtimes ?? Array.Empty<string>())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var info = new KeyValueList();
        info.Add("Config", Markup.Escape(string.IsNullOrWhiteSpace(configPath) ? "(discovered)" : configPath!));
        info.Add("Project", Markup.Escape(plan.ProjectRoot));
        if (!string.IsNullOrWhiteSpace(plan.SolutionPath)) info.Add("Solution", Markup.Escape(plan.SolutionPath!));
        info.Add("Configuration", Markup.Escape(plan.Configuration));
        info.Add("Targets", plan.Targets.Length.ToString());
        info.Add("Runtimes", runtimes.Length == 0 ? "(none)" : Markup.Escape(string.Join(", ", runtimes)));
        info.Add("Steps", (plan.Steps?.Length ?? 0).ToString());
        ConsoleOutput.Write(new Rows(new Rule($"[bold accent]dotnet publish - {Markup.Escape(titleTarget ?? string.Empty)}[/]"), info, new TextBlock(string.Empty)));
    }

    private sealed class Reporter(ProgressView view) : IDotNetPublishProgressReporterV2
    {
        public void StepStarting(DotNetPublishStep step) => view.Start(step.Key);

        // Signing reports its own progress; the row shows the outputs being signed and the provider guidance.
        public void StepProgress(DotNetPublishStep step, string kind, string detail)
            => view.Detail(step.Key, kind == "Signing" ? $"Sign outputs ({step.TargetName ?? "release evidence"}): {detail}" : detail, kind == "Signing" ? kind : null);

        public void StepCompleted(DotNetPublishStep step) => view.Finish(step.Key, ProgressState.Completed);

        public void StepFailed(DotNetPublishStep step, Exception error)
            => view.Finish(step.Key, ProgressState.Failed, error.GetBaseException().Message);
    }
}
