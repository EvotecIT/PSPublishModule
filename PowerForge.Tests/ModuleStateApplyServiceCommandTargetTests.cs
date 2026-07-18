namespace PowerForge.Tests;

public sealed class ModuleStateApplyServiceCommandTargetTests
{
    [Fact]
    public void Prepare_UsesActionTargetPathAsManagedModuleRoot()
    {
        var plan = CreateUpdatePlan(targetPath: @"C:\SelectedRoot");

        var command = PrepareCommand(plan);

        Assert.Equal("Update-ManagedModule", command.CommandName);
        Assert.Equal(new[] { "-Name", "Company.Tools", "-RequiredVersion", "1.2.0", "-ModuleRoot", @"C:\SelectedRoot", "-Repository", "CompanyModules" }, command.Arguments);
    }

    [Fact]
    public void Prepare_UsesActionTargetModuleRootAsManagedModuleRoot()
    {
        var plan = CreateUpdatePlan(targetModuleRoot: @"C:\SelectedRoot");

        var command = PrepareCommand(plan);

        Assert.Equal("Update-ManagedModule", command.CommandName);
        Assert.Equal(new[] { "-Name", "Company.Tools", "-RequiredVersion", "1.2.0", "-ModuleRoot", @"C:\SelectedRoot", "-Repository", "CompanyModules" }, command.Arguments);
    }

    [Fact]
    public void CreateMaintenanceReceipt_UsesTargetPathToSelectObservedPlacement()
    {
        const string selectedRoot = @"C:\SelectedRoot";
        var plan = CreateUpdatePlan(targetPath: selectedRoot);
        var service = new ModuleStateApplyService();
        var result = service.Prepare(plan, new ModuleStateDeliveryOptions(repository: "CompanyModules"));
        var observedModules = new[]
        {
            new ModuleStateInstalledModule("Company.Tools", "1.2.0", sourceRepository: "CompanyModules", moduleRoot: @"C:\OtherRoot", profileName: "Bob"),
            new ModuleStateInstalledModule("Company.Tools", "1.2.0", sourceRepository: "CompanyModules", moduleRoot: selectedRoot, profileName: "Alice")
        };

        var receipt = service.CreateMaintenanceReceipt(result, observedModules: observedModules);

        var module = Assert.Single(receipt.Modules);
        Assert.Equal(selectedRoot, module.ModuleRoot);
        Assert.Equal("Alice", module.ProfileName);
    }

    [Fact]
    public void CreateMaintenanceReceipt_PreservesDeliveryFallbackRoot()
    {
        const string selectedRoot = @"C:\SelectedRoot";
        var service = new ModuleStateApplyService();
        var result = service.Prepare(
            CreateUpdatePlan(),
            new ModuleStateDeliveryOptions(repository: "CompanyModules", moduleRoot: selectedRoot));

        var receipt = service.CreateMaintenanceReceipt(result);

        Assert.Equal(selectedRoot, Assert.Single(receipt.Modules).ModuleRoot);
    }

    private static ModuleStatePlan CreateUpdatePlan(string? targetPath = null, string? targetModuleRoot = null)
        => new(
            new[]
            {
                new ModuleStatePlanAction(
                    ModuleStatePlanActionKind.Update,
                    "Company.Tools",
                    "1.0.0",
                    "=1.2.0",
                    "repair",
                    targetPath: targetPath,
                    targetRepository: "CompanyModules",
                    targetModuleRoot: targetModuleRoot)
            },
            Array.Empty<ModuleStateConflictFinding>());

    private static ModuleStateDeliveryCommand PrepareCommand(ModuleStatePlan plan)
    {
        var result = new ModuleStateApplyService().Prepare(
            plan,
            new ModuleStateDeliveryOptions(
                profileName: "FallbackProfile",
                moduleRoot: @"C:\FallbackRoot",
                transport: ModuleStateDeliveryTransport.ManagedModule));

        return Assert.Single(result.Receipt.Commands);
    }
}
