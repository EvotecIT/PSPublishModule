using System;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModuleBuildOutcomeServiceTests
{
    [Fact]
    public void Evaluate_ReturnsSuccessCompletionDetails()
    {
        var result = new ModuleBuildOutcomeService().Evaluate(
            new ModuleBuildWorkflowResult
            {
                Succeeded = true
            },
            exitCodeMode: true,
            jsonOnly: false,
            useLegacy: false,
            elapsed: TimeSpan.FromSeconds(2.5));

        Assert.True(result.Succeeded);
        Assert.True(result.ShouldSetExitCode);
        Assert.Equal(0, result.ExitCode);
        Assert.False(result.ShouldEmitErrorRecord);
        Assert.False(result.ShouldReplayBufferedLogs);
        Assert.False(result.ShouldWriteInteractiveFailureSummary);
        Assert.Equal("Module build completed in 2s 500ms", result.CompletionMessage);
    }

    [Fact]
    public void Evaluate_ReturnsFailureDecisionsForNonInteractiveNonPolicyFailure()
    {
        var workflow = new ModuleBuildWorkflowResult
        {
            Succeeded = false,
            UsedInteractiveView = false,
            Error = new InvalidOperationException("boom")
        };

        var result = new ModuleBuildOutcomeService().Evaluate(
            workflow,
            exitCodeMode: false,
            jsonOnly: true,
            useLegacy: true,
            elapsed: TimeSpan.FromMinutes(1.0));

        Assert.False(result.Succeeded);
        Assert.False(result.ShouldSetExitCode);
        Assert.True(result.ShouldEmitErrorRecord);
        Assert.Equal("InvokeModuleBuildDslFailed", result.ErrorRecordId);
        Assert.True(result.ShouldReplayBufferedLogs);
        Assert.False(result.ShouldWriteInteractiveFailureSummary);
        Assert.Equal("Pipeline config generation failed in 1m 0s", result.CompletionMessage);
    }

    [Fact]
    public void Evaluate_SuppressesErrorRecordAndRequestsInteractiveSummary_WhenInteractiveFailureNeedsFollowup()
    {
        var workflow = new ModuleBuildWorkflowResult
        {
            Succeeded = false,
            UsedInteractiveView = true,
            WrotePolicySummary = false,
            Plan = CreateUninitializedPlan(),
            Error = new InvalidOperationException("boom")
        };

        var result = new ModuleBuildOutcomeService().Evaluate(
            workflow,
            exitCodeMode: false,
            jsonOnly: false,
            useLegacy: false,
            elapsed: TimeSpan.FromMilliseconds(250));

        Assert.False(result.ShouldEmitErrorRecord);
        Assert.True(result.ShouldReplayBufferedLogs);
        Assert.True(result.ShouldWriteInteractiveFailureSummary);
        Assert.Equal("Module build failed in 250ms", result.CompletionMessage);
    }

    private static ModulePipelinePlan CreateUninitializedPlan()
    {
#pragma warning disable SYSLIB0050
        return (ModulePipelinePlan)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(ModulePipelinePlan));
#pragma warning restore SYSLIB0050
    }
}
