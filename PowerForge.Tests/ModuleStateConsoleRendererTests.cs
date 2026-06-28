using PSPublishModule;

namespace PowerForge.Tests;

public sealed class ModuleStateConsoleRendererTests
{
    [Fact]
    public void FormatExecutionStatuses_UsesDependencyStatuses()
    {
        var execution = new ModuleStateDeliveryExecutionResult
        {
            OperationPerformed = true,
            DependencyResults = new[]
            {
                new ModuleStateDependencyResult { Status = "Installed" },
                new ModuleStateDependencyResult { Status = "SourceRepaired" }
            }
        };

        Assert.Equal("Installed, SourceRepaired", ModuleStateConsoleRenderer.FormatExecutionStatuses(execution));
    }

    [Fact]
    public void FormatExecutionDetails_ExplainsSkippedDelivery()
    {
        var execution = new ModuleStateDeliveryExecutionResult
        {
            OperationPerformed = false,
            DependencyResults = new[]
            {
                new ModuleStateDependencyResult
                {
                    Status = "Skipped",
                    Message = "ShouldProcess declined the operation."
                }
            }
        };

        Assert.Equal("ShouldProcess declined the operation.", ModuleStateConsoleRenderer.FormatExecutionDetails(execution));
    }

    [Fact]
    public void FormatExecutionDetails_UsesTransportReasonWhenNoDependencyMessageExists()
    {
        var execution = new ModuleStateDeliveryExecutionResult
        {
            OperationPerformed = true,
            DeliveryTransportReason = "Auto selected managed transport because a repository source URI or local feed path was resolved."
        };

        Assert.Contains("Auto selected managed transport", ModuleStateConsoleRenderer.FormatExecutionDetails(execution));
    }

    [Fact]
    public void FormatExecutionTransport_ShowsAutoResolution()
    {
        var execution = new ModuleStateDeliveryExecutionResult
        {
            RequestedTransport = ModuleStateDeliveryTransport.Auto,
            EffectiveTransport = ModuleStateDeliveryTransport.ManagedModule
        };

        Assert.Equal("Auto -> ManagedModule", ModuleStateConsoleRenderer.FormatExecutionTransport(execution));
    }
}
