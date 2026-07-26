using System.Management.Automation;
using System.Text.Json;
using PSPublishModule;

namespace PowerForge.Tests;

[Collection("ModuleRepositoryProfileEnvironment")]
public sealed class RepairManagedModuleInventoryPathTests
{
    [Fact]
    public void Plan_PreservesLegacyInventoryPathAvailability()
    {
        using var workspace = new TemporaryDirectory();
        var moduleRoot = Path.Combine(workspace.Path, "Modules");
        Directory.CreateDirectory(moduleRoot);
        var inventoryPath = Path.Combine(workspace.Path, "inventory.json");
        File.WriteAllText(inventoryPath, JsonSerializer.Serialize(new
        {
            Source = "LegacyArtifact",
            ModulePaths = new[] { moduleRoot },
            InstalledModules = Array.Empty<object>()
        }));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("InventoryPath", inventoryPath)
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var scannedPath = Assert.Single(result.Inventory.ScannedPaths);
        Assert.Equal(moduleRoot, scannedPath.Path);
        Assert.True(scannedPath.WasAvailable);
    }

    private static PowerShell CreatePowerShellWithModuleImported()
    {
        var ps = PowerShell.Create();
        ps.AddCommand("Import-Module")
            .AddParameter("Name", typeof(RepairManagedModuleCommand).Assembly.Location)
            .AddParameter("Force");
        _ = ps.Invoke();
        AssertNoPowerShellErrors(ps);
        ps.Commands.Clear();
        return ps;
    }

    private static void AssertNoPowerShellErrors(PowerShell ps)
    {
        if (ps.HadErrors)
            throw new InvalidOperationException(string.Join(Environment.NewLine, ps.Streams.Error.Select(static error => error.ToString())));
    }
}
