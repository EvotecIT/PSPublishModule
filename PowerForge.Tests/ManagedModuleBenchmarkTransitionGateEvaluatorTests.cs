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
        Assert.False(gate.NativeIsolationRequired);
        Assert.Equal("Missing successful compatibility baseline for PowerShellGet.", gate.CompatibilityFallbackReason);
        Assert.Contains(gate.Reasons, reason => reason.Contains("PowerShellGet", StringComparison.OrdinalIgnoreCase) &&
                                                reason.Contains("Administrator rights", StringComparison.OrdinalIgnoreCase));
    }

    private static ManagedModuleBenchmarkRunResult CreateRun(
        ManagedModuleBenchmarkEngine engine,
        bool succeeded,
        string? error = null)
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
            ErrorMessage = error
        };
}
