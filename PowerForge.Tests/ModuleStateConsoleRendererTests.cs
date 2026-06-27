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
}
