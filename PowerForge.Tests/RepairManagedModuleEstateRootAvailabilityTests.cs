using System.Management.Automation;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed partial class RepairManagedModuleEstateTests
{
    [Fact]
    public void Plan_ReportsExistingNonDirectoryExplicitDestinationAsError()
    {
        using var workspace = new TemporaryDirectory();
        var artifactRoot = Path.Combine(workspace.Path, "artifact", "Modules");
        var destinationRoot = Path.Combine(workspace.Path, "destination-file");
        Directory.CreateDirectory(artifactRoot);
        File.WriteAllText(destinationRoot, "not a module directory");
        var inventory = new ModuleStateInventoryResult
        {
            Source = "Artifact",
            ModulePaths = new[] { artifactRoot },
            ScannedPaths = new[] { new ModuleStateInventoryPathResult { Path = artifactRoot, IsRequired = true } }
        };

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("Inventory", inventory)
            .AddParameter("ModuleRoot", destinationRoot)
            .AddParameter("Name", new[] { "Company.Missing" })
            .AddParameter("InstallMissing")
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var diagnostic = Assert.Single(result.Inventory.Diagnostics, static item =>
            item.Code == "ModuleState.InventoryPathInaccessible");
        Assert.Equal("Error", diagnostic.Severity);
        Assert.Contains(result.Plan.Findings, static finding =>
            finding.Code == "ModuleState.InventoryPathInaccessible" && finding.Severity == "Error");
        Assert.False(result.Test.IsCompliant);
        Assert.False(result.Apply.CanApply);
    }
}
