using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using PowerForge;
using Spectre.Console;

namespace PowerForge.ConsoleShared;

/// <summary>Renders executable publishing with the shared build ledger in every host.</summary>
internal static partial class SpectreDotNetPublishConsoleUi
{
    internal static DotNetPublishResult RunInteractive(
        DotNetPublishPlan plan,
        string? configPath,
        Func<IDotNetPublishProgressReporter, DotNetPublishResult> run)
        => RunInteractive(AnsiConsole.Console, plan, configPath, run);

    internal static DotNetPublishResult RunInteractive(
        IAnsiConsole console,
        DotNetPublishPlan plan,
        string? configPath,
        Func<IDotNetPublishProgressReporter, DotNetPublishResult> run)
    {
        if (console is null) throw new ArgumentNullException(nameof(console));
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (run is null) throw new ArgumentNullException(nameof(run));

        WriteHeader(console, plan, configPath);
        var presentation = SpectreProgressPresentation.Create(console);
        SpectreProgressLedger? ledger = null;
        DotNetPublishResult? result = null;
        Exception? failure = null;
        SpectreProgressDisplay.Run(console, presentation.CreateColumns(), context =>
        {
            ledger = new SpectreProgressLedger(context, presentation);
            var steps = plan.Steps ?? Array.Empty<DotNetPublishStep>();
            var items = steps.Select((step, index) => new SpectreProgressLedgerItem
            {
                Key = step.Key,
                GroupKey = "dotnetpublish",
                GroupTitle = "Executable publishing",
                Title = GetStepTitle(step, plan),
                Kind = step.Kind == DotNetPublishStepKind.Publish ? "Packages" : step.Kind.ToString(),
                CounterLabel = "Step",
                Position = index + 1,
                Total = steps.Length
            }).ToArray();
            ledger.Plan(items);
            var reporter = new Reporter(ledger, items.ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase));
            try
            {
                result = run(reporter);
                ledger.FinishRemaining(result.Succeeded);
            }
            catch (Exception ex)
            {
                failure = ex;
                ledger.FinishRemaining(success: false);
            }
        });

        SpectreProgressLedger.WriteLedger(console, ledger!.GetSnapshots(), "Executable publish details");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        WriteSummary(console, plan, result!);
        return result!;
    }

    private sealed class Reporter : IDotNetPublishProgressReporter
    {
        private readonly SpectreProgressLedger _ledger;
        private readonly IReadOnlyDictionary<string, SpectreProgressLedgerItem> _items;

        internal Reporter(SpectreProgressLedger ledger, IReadOnlyDictionary<string, SpectreProgressLedgerItem> items)
        {
            _ledger = ledger;
            _items = items;
        }

        public void StepStarting(DotNetPublishStep step)
            => Update(step, SpectreProgressLedgerState.Started);

        public void StepCompleted(DotNetPublishStep step)
            => Update(step, SpectreProgressLedgerState.Completed);

        public void StepFailed(DotNetPublishStep step, Exception error)
            => Update(step, SpectreProgressLedgerState.Failed,
                SpectrePipelineSummaryWriter.GetFailureHeadline(error.GetBaseException().Message));

        private void Update(DotNetPublishStep step, SpectreProgressLedgerState state, string? detail = null)
        {
            if (_items.TryGetValue(step.Key, out var item))
                _ledger.Update(item, state, detail);
        }
    }
}
