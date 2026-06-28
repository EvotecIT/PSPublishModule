using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleManagedEvidenceEvaluatorTests
{
    [Fact]
    public void Evaluate_IsReadyForMeasuredOperationsWithManagedEvidence()
    {
        var result = ManagedModuleManagedEvidenceEvaluator.Evaluate(new[]
        {
            CreateGate(ManagedModuleBenchmarkOperation.Install, managedReady: true)
        });

        Assert.Equal(ManagedModuleManagedEvidenceStatus.Ready, result.Status);
        Assert.True(result.Ready);
        Assert.Equal(new[] { ManagedModuleBenchmarkOperation.Install }, result.RequiredOperations);
        Assert.Equal(new[] { ManagedModuleBenchmarkOperation.Install }, result.ReadyOperations);
        Assert.Empty(result.MissingOperations);
        Assert.Empty(result.IncompleteOperations);
        Assert.Contains(result.Reasons, reason => reason.Contains("ready", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_ReportsMissingRequiredOperations()
    {
        var result = ManagedModuleManagedEvidenceEvaluator.Evaluate(
            new[] { CreateGate(ManagedModuleBenchmarkOperation.Install, managedReady: true) },
            new[] { ManagedModuleBenchmarkOperation.Install, ManagedModuleBenchmarkOperation.Update });

        Assert.Equal(ManagedModuleManagedEvidenceStatus.Incomplete, result.Status);
        Assert.False(result.Ready);
        Assert.Equal(new[] { ManagedModuleBenchmarkOperation.Update }, result.MissingOperations);
        Assert.Contains(result.Reasons, reason => reason.Contains("Missing managed evidence gate for Update", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_ReportsIncompleteManagedEvidence()
    {
        var result = ManagedModuleManagedEvidenceEvaluator.Evaluate(new[]
        {
            CreateGate(
                ManagedModuleBenchmarkOperation.Save,
                managedReady: false,
                "One or more managed benchmark runs failed.")
        });

        Assert.Equal(ManagedModuleManagedEvidenceStatus.Incomplete, result.Status);
        Assert.False(result.Ready);
        Assert.Equal(new[] { ManagedModuleBenchmarkOperation.Save }, result.IncompleteOperations);
        Assert.Contains(result.Reasons, reason => reason.Contains("One or more managed benchmark runs failed", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_RequiresAtLeastOneGateWhenOperationsAreNotExplicit()
    {
        var result = ManagedModuleManagedEvidenceEvaluator.Evaluate(Array.Empty<ManagedModuleBenchmarkTransitionGateResult>());

        Assert.Equal(ManagedModuleManagedEvidenceStatus.Incomplete, result.Status);
        Assert.False(result.Ready);
        Assert.Empty(result.RequiredOperations);
        Assert.Contains(result.Reasons, reason => reason.Contains("No managed evidence gates", StringComparison.Ordinal));
    }

    private static ManagedModuleBenchmarkTransitionGateResult CreateGate(
        ManagedModuleBenchmarkOperation operation,
        bool managedReady,
        params string[] reasons)
        => new()
        {
            Operation = operation,
            Status = managedReady
                ? ManagedModuleBenchmarkTransitionGateStatus.Ready
                : ManagedModuleBenchmarkTransitionGateStatus.Incomplete,
            ManagedEvidenceReady = managedReady,
            ReadyForDefaultManagedTransport = managedReady,
            Reasons = reasons
        };
}
