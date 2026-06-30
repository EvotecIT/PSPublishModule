using System.Management.Automation;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed class GetManagedModuleCommandTests
{
    [Fact]
    public void GetManagedModule_ReturnsInstalledModuleRowsFromExplicitModuleRoot()
    {
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.2.0");
        CreateInstalledModule(moduleRoot.Path, "Other.Tools", "2.0.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Get-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("Name", "Company.*");

        var result = Assert.IsType<ModuleStateInstalledModuleResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.Equal("Company.Tools", result.Name);
        Assert.Equal("1.2.0", result.Version);
        Assert.Equal(Path.Combine(moduleRoot.Path, "Company.Tools", "1.2.0"), result.Path);
    }

    [Fact]
    public void GetManagedModule_AsInventory_ReturnsFilteredInventoryObject()
    {
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.2.0");
        CreateInstalledModule(moduleRoot.Path, "Other.Tools", "2.0.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Get-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("Name", "Company.*")
            .AddParameter("AsInventory");

        var inventory = Assert.IsType<ModuleStateInventoryResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var result = Assert.Single(inventory.InstalledModules);
        Assert.Equal("Company.Tools", result.Name);
        Assert.Equal(moduleRoot.Path, Assert.Single(inventory.ModulePaths));
    }

    private static void CreateInstalledModule(string moduleRoot, string name, string version)
    {
        var modulePath = Path.Combine(moduleRoot, name, version);
        Directory.CreateDirectory(modulePath);
        File.WriteAllText(Path.Combine(modulePath, name + ".psd1"), "@{ ModuleVersion = '" + version + "' }");
    }

    private static PowerShell CreatePowerShellWithModuleImported()
    {
        var ps = PowerShell.Create();
        ps.AddCommand("Import-Module")
            .AddParameter("Name", typeof(GetManagedModuleCommand).Assembly.Location)
            .AddParameter("Force");
        _ = ps.Invoke();
        AssertNoPowerShellErrors(ps);
        ps.Commands.Clear();
        return ps;
    }

    private static void AssertNoPowerShellErrors(PowerShell ps)
    {
        if (ps.HadErrors)
            throw new InvalidOperationException(string.Join(Environment.NewLine, ps.Streams.Error.Select(error => error.ToString())));
    }
}
