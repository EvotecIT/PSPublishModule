using System;

namespace PowerForge;

internal sealed class ModuleBuildOutcomeService
{
    private readonly BufferedLogSupportService _logSupport;

    public ModuleBuildOutcomeService(BufferedLogSupportService? logSupport = null)
    {
        _logSupport = logSupport ?? new BufferedLogSupportService();
    }

    public ModuleBuildCompletionOutcome Evaluate(
        ModuleBuildWorkflowResult? workflow,
        bool exitCodeMode,
        bool jsonOnly,
        bool useLegacy,
        TimeSpan elapsed)
    {
        var succeeded = workflow?.Succeeded ?? false;
        var duration = _logSupport.FormatDuration(elapsed);

        return new ModuleBuildCompletionOutcome
        {
            Succeeded = succeeded,
            ShouldSetExitCode = exitCodeMode,
            ExitCode = succeeded ? 0 : 1,
            ShouldEmitErrorRecord = !succeeded &&
                                    !exitCodeMode &&
                                    workflow?.UsedInteractiveView != true &&
                                    workflow?.PolicyFailure is null,
            ErrorRecordId = useLegacy ? "InvokeModuleBuildDslFailed" : "InvokeModuleBuildPowerForgeFailed",
            ShouldReplayBufferedLogs = !succeeded,
            ShouldWriteInteractiveFailureSummary = !succeeded &&
                                                workflow?.UsedInteractiveView == true &&
                                                workflow?.Plan is not null &&
                                                !workflow.WrotePolicySummary,
            CompletionMessage = succeeded
                ? (jsonOnly
                    ? $"Pipeline config generated in {duration}"
                    : $"Module build completed in {duration}")
                : (jsonOnly
                    ? $"Pipeline config generation failed in {duration}"
                    : $"Module build failed in {duration}")
        };
    }
}
