using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleCompatibilityRetirementEvaluatorTests
{
    [Fact]
    public void Evaluate_AllRequiredGatesReadyAndSupportedProviders_ReturnsReady()
    {
        var result = ManagedModuleCompatibilityRetirementEvaluator.Evaluate(
            new[]
            {
                ReadyGate(ManagedModuleBenchmarkOperation.Install),
                ReadyGate(ManagedModuleBenchmarkOperation.Save),
                ReadyGate(ManagedModuleBenchmarkOperation.Update),
                ReadyGate(ManagedModuleBenchmarkOperation.Publish)
            },
            new[]
            {
                new ManagedModuleProviderSupport
                {
                    Provider = "Local folder feed",
                    Level = ManagedModuleProviderSupportLevel.Supported,
                    ManagedLifecycleSupported = true
                }
            });

        Assert.Equal(ManagedModuleCompatibilityRetirementStatus.Ready, result.Status);
        Assert.True(result.ReadyToMarkCompatibilityLegacy);
        Assert.Empty(result.ProviderFallbacks);
        Assert.Contains("All required transition gates", Assert.Single(result.Reasons), StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_MissingOperation_ReturnsIncomplete()
    {
        var result = ManagedModuleCompatibilityRetirementEvaluator.Evaluate(
            new[]
            {
                ReadyGate(ManagedModuleBenchmarkOperation.Install)
            },
            Array.Empty<ManagedModuleProviderSupport>());

        Assert.Equal(ManagedModuleCompatibilityRetirementStatus.Incomplete, result.Status);
        Assert.False(result.ReadyToMarkCompatibilityLegacy);
        Assert.Contains(ManagedModuleBenchmarkOperation.Save, result.MissingOperations);
        Assert.Contains(result.Reasons, reason => reason.Contains("Missing transition gate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_ProviderFallback_ReturnsIncomplete()
    {
        var result = ManagedModuleCompatibilityRetirementEvaluator.Evaluate(
            new[]
            {
                ReadyGate(ManagedModuleBenchmarkOperation.Install),
                ReadyGate(ManagedModuleBenchmarkOperation.Save),
                ReadyGate(ManagedModuleBenchmarkOperation.Update),
                ReadyGate(ManagedModuleBenchmarkOperation.Publish)
            },
            new[]
            {
                new ManagedModuleProviderSupport
                {
                    Provider = "ProviderX",
                    Level = ManagedModuleProviderSupportLevel.Partial,
                    ManagedLifecycleSupported = true,
                    CompatibilityFallbackRecommended = true,
                    Limitations = new[] { "live authentication is not proven" }
                }
            });

        Assert.Equal(ManagedModuleCompatibilityRetirementStatus.Incomplete, result.Status);
        Assert.False(result.ReadyToMarkCompatibilityLegacy);
        Assert.Contains("ProviderX", Assert.Single(result.Reasons), StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_BlockedGate_ReturnsBlocked()
    {
        var result = ManagedModuleCompatibilityRetirementEvaluator.Evaluate(
            new[]
            {
                new ManagedModuleBenchmarkTransitionGateResult
                {
                    Operation = ManagedModuleBenchmarkOperation.Install,
                    Status = ManagedModuleBenchmarkTransitionGateStatus.Blocked,
                    Reasons = new[] { "Native isolation required." }
                }
            },
            Array.Empty<ManagedModuleProviderSupport>(),
            new[] { ManagedModuleBenchmarkOperation.Install });

        Assert.Equal(ManagedModuleCompatibilityRetirementStatus.Blocked, result.Status);
        Assert.Contains(ManagedModuleBenchmarkOperation.Install, result.BlockedOperations);
    }

    private static ManagedModuleBenchmarkTransitionGateResult ReadyGate(ManagedModuleBenchmarkOperation operation)
        => new()
        {
            Operation = operation,
            Status = ManagedModuleBenchmarkTransitionGateStatus.Ready,
            ReadyForDefaultManagedTransport = true
        };
}
