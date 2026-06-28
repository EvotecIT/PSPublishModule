using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleBenchmarkTransitionGateEvaluatorTests
{
    [Fact]
    public void Evaluate_RecordsFailedCompatibilityEngineReason()
    {
        var gates = ManagedModuleBenchmarkTransitionGateEvaluator.Evaluate(new[]
        {
            CreateRun(ManagedModuleBenchmarkEngine.Managed, succeeded: true),
            CreateRun(ManagedModuleBenchmarkEngine.PSResourceGet, succeeded: true),
            CreateRun(
                ManagedModuleBenchmarkEngine.PowerShellGet,
                succeeded: false,
                error: "Install-Module failed (exit 1)." + Environment.NewLine + "Administrator rights are required.")
        });

        var gate = Assert.Single(gates);
        Assert.Equal(ManagedModuleBenchmarkTransitionGateStatus.Incomplete, gate.Status);
        Assert.True(gate.ManagedEvidenceReady);
        Assert.False(gate.NativeIsolationRequired);
        Assert.True(gate.LegacyCompatibilityProviderFailureObserved);
        Assert.Contains("PowerShellGet", gate.CompatibilityFallbackReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Administrator rights", gate.CompatibilityFallbackReason, StringComparison.OrdinalIgnoreCase);
        var limitation = Assert.Single(gate.CompatibilityProviderLimitations);
        Assert.Contains("PowerShellGet", limitation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Administrator rights", limitation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(gate.Reasons, reason => reason.Contains("PowerShellGet", StringComparison.OrdinalIgnoreCase) &&
                                                reason.Contains("Administrator rights", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_BlocksWhenManagedMedianExceedsPerformancePolicy()
    {
        var gates = ManagedModuleBenchmarkTransitionGateEvaluator.Evaluate(
            new[]
            {
                CreateRun(ManagedModuleBenchmarkEngine.Managed, succeeded: true, elapsed: TimeSpan.FromSeconds(12)),
                CreateRun(ManagedModuleBenchmarkEngine.PSResourceGet, succeeded: true, elapsed: TimeSpan.FromSeconds(1)),
                CreateRun(ManagedModuleBenchmarkEngine.PowerShellGet, succeeded: true, elapsed: TimeSpan.FromSeconds(2))
            },
            maximumManagedSlowdownRatio: 2,
            maximumManagedSlowdownMilliseconds: 500);

        var gate = Assert.Single(gates);
        Assert.Equal(ManagedModuleBenchmarkTransitionGateStatus.Incomplete, gate.Status);
        Assert.False(gate.ReadyForDefaultManagedTransport);
        Assert.False(gate.PerformanceWithinPolicy);
        Assert.Equal(12000, gate.ManagedMedianMilliseconds);
        Assert.Equal(1000, gate.CompatibilityMedianMilliseconds);
        Assert.Equal(2000, gate.AllowedManagedMilliseconds);
        Assert.Contains(gate.Reasons, reason => reason.Contains("Managed median elapsed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_AllowsManagedMedianWithinAbsoluteTolerance()
    {
        var gates = ManagedModuleBenchmarkTransitionGateEvaluator.Evaluate(
            new[]
            {
                CreateRun(ManagedModuleBenchmarkEngine.Managed, succeeded: true, elapsed: TimeSpan.FromMilliseconds(1400)),
                CreateRun(ManagedModuleBenchmarkEngine.PSResourceGet, succeeded: true, elapsed: TimeSpan.FromSeconds(1)),
                CreateRun(ManagedModuleBenchmarkEngine.PowerShellGet, succeeded: true, elapsed: TimeSpan.FromMilliseconds(1100))
            },
            maximumManagedSlowdownRatio: 1.1,
            maximumManagedSlowdownMilliseconds: 500);

        var gate = Assert.Single(gates);
        Assert.Equal(ManagedModuleBenchmarkTransitionGateStatus.Ready, gate.Status);
        Assert.True(gate.ReadyForDefaultManagedTransport);
        Assert.True(gate.PerformanceWithinPolicy);
        Assert.Contains(gate.Reasons, reason => reason.Contains("passed", StringComparison.OrdinalIgnoreCase));
    }

    private static ManagedModuleBenchmarkRunResult CreateRun(
        ManagedModuleBenchmarkEngine engine,
        bool succeeded,
        string? error = null,
        TimeSpan? elapsed = null)
        => new()
        {
            ScenarioId = "install-transition",
            Operation = ManagedModuleBenchmarkOperation.Install,
            Engine = engine.ToString(),
            Iteration = 1,
            Succeeded = succeeded,
            Status = succeeded ? "Installed" : "Failed",
            ModuleName = "Company.Tools",
            Version = succeeded ? "1.0.0" : null,
            ValidatedVersion = succeeded ? "1.0.0" : null,
            VersionValidationSucceeded = succeeded ? true : null,
            Elapsed = elapsed ?? TimeSpan.FromMilliseconds(100),
            ErrorMessage = error
        };
}
