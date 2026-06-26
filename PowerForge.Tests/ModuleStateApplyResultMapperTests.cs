using PSPublishModule;

namespace PowerForge.Tests;

public sealed class ModuleStateApplyResultMapperTests
{
    [Fact]
    public void ToCmdletResult_IncludesExecutionAndPostApplyInventoryEvidence()
    {
        var plan = new ModuleStatePlan(
            new[] { new ModuleStatePlanAction(ModuleStatePlanActionKind.Install, "Company.Tools", null, ">=1.0.0", "missing") },
            Array.Empty<ModuleStateConflictFinding>());
        var applyResult = new ModuleStateApplyService().Prepare(plan, new ModuleStateDeliveryOptions(repository: "Company"));
        var execution = new[]
        {
            new ModuleStateDeliveryExecutionResult
            {
                Operation = "Install",
                OperationPerformed = true,
                RepositoryName = "Company",
                DependencyResults = new[]
                {
                    new ModuleStateDependencyResult
                    {
                        Name = "Company.Tools",
                        Status = "Installed",
                        ResolvedVersion = "1.0.0"
                    }
                }
            }
        };
        var postInventory = new ModuleStateInventoryResult
        {
            Source = "ModulePath",
            InstalledModules = new[]
            {
                new ModuleStateInstalledModuleResult { Name = "Company.Tools", Version = "1.0.0" }
            }
        };

        var result = ModuleStateApplyResultMapper.ToCmdletResult(
            applyResult,
            "receipt.json",
            "maintenance.json",
            executionRequested: true,
            executionResults: execution,
            postApplyInventory: postInventory);

        Assert.True(result.ExecutionRequested);
        Assert.Equal("receipt.json", result.ReceiptPath);
        Assert.Equal("maintenance.json", result.MaintenanceReceiptOutputPath);
        Assert.Same(execution, result.ExecutionResults);
        Assert.Same(postInventory, result.PostApplyInventory);
        Assert.Equal("Install-PrivateModule", Assert.Single(result.Commands).CommandName);
    }

    [Fact]
    public void MaintenanceEvidenceMapper_CombinesExecutionAndPostApplyEvidence()
    {
        var execution = new[]
        {
            new ModuleStateDeliveryExecutionResult
            {
                RepositoryName = "ExecutionRepo",
                DependencyResults = new[]
                {
                    new ModuleStateDependencyResult
                    {
                        Name = "Company.Tools",
                        ResolvedVersion = "1.4.0"
                    }
                }
            }
        };
        var postInventory = new ModuleStateInventoryResult
        {
            InstalledModules = new[]
            {
                new ModuleStateInstalledModuleResult
                {
                    Name = "Company.Other",
                    Version = "2.0.0",
                    Scope = "CurrentUser"
                }
            }
        };

        var modules = ModuleStateMaintenanceEvidenceMapper.ToObservedModules(execution, postInventory, "Company");

        Assert.Contains(modules, static module => module.Name == "Company.Tools" && module.Version == "1.4.0" && module.SourceRepository == "ExecutionRepo");
        Assert.Contains(modules, static module => module.Name == "Company.Other" && module.Version == "2.0.0" && module.Scope == "CurrentUser");
    }
}
