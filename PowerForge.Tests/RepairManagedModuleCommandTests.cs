using System.Management.Automation;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed class RepairManagedModuleCommandTests
{
    [Fact]
    public void RepairManagedModule_ExposesManagedMaintenanceParameters()
    {
        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Get-Command")
            .AddArgument("Repair-ManagedModule");

        var command = Assert.IsType<CmdletInfo>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.True(command.Parameters.ContainsKey("Plan"));
        Assert.True(command.Parameters.ContainsKey("Latest"));
        Assert.True(command.Parameters.ContainsKey("Family"));
        Assert.True(command.Parameters.ContainsKey("Cleanup"));
        Assert.True(command.Parameters.ContainsKey("MaintenanceReceiptPath"));
        Assert.True(command.Parameters.ContainsKey("Transport"));
        Assert.True(command.Parameters.ContainsKey("AcceptLicense"));
        Assert.True(command.Parameters.ContainsKey("AllowClobber"));
        Assert.True(command.Parameters.TryGetValue("Version", out var versionParameter));
        Assert.Contains("RequiredVersion", versionParameter.Aliases);
        Assert.True(command.Parameters.TryGetValue("Repository", out var repositoryParameter));
        Assert.Contains("Source", repositoryParameter.Aliases);
        Assert.Contains("RepositoryUri", repositoryParameter.Aliases);
        Assert.True(command.Parameters.TryGetValue("ModuleRoot", out var moduleRootParameter));
        Assert.Contains("Path", moduleRootParameter.Aliases);
    }

    [Fact]
    public void RepairManagedModule_PlanDetectsGraphFamilyVersionMismatch()
    {
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Microsoft.Graph.Authentication", "2.36.0");
        CreateInstalledModule(moduleRoot.Path, "Microsoft.Graph.Users", "2.38.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("Family", new[] { "Graph" })
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var action = Assert.Single(result.Plan.Actions, action =>
            string.Equals(action.ModuleName, "Microsoft.Graph.Authentication", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(action.Kind, "Update", StringComparison.OrdinalIgnoreCase));
        Assert.True(action.IsRepair);
        Assert.Equal("=2.38.0", action.VersionPolicy);
        Assert.False(result.Apply.ExecutionRequested);
        Assert.Empty(result.Apply.ExecutionResults);
    }

    private static void CreateInstalledModule(string moduleRoot, string name, string version)
    {
        var modulePath = Path.Combine(moduleRoot, name, version);
        Directory.CreateDirectory(modulePath);
        File.WriteAllText(
            Path.Combine(modulePath, name + ".psd1"),
            "@{ RootModule = '" + name + ".psm1'; ModuleVersion = '" + version + "' }");
        File.WriteAllText(Path.Combine(modulePath, name + ".psm1"), string.Empty);
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
            throw new InvalidOperationException(string.Join(Environment.NewLine, ps.Streams.Error.Select(error => error.ToString())));
    }
}
