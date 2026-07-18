using System.Management.Automation;
using PSPublishModule;

namespace PowerForge.Tests;

[Collection("ModuleRepositoryProfileEnvironment")]
public sealed class RepairManagedModuleForceCleanupTests
{
    [Fact]
    public void RepairManagedModule_ForceReinstallsSatisfiedVersionBeforeCleanup()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        var oldPath = CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        var currentPath = CreateInstalledModule(moduleRoot.Path, "Company.Tools", "2.0.0");
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.2.0.0.nupkg"),
            "Company.Tools",
            "2.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Company.Tools.psd1"] = "@{ RootModule = 'Company.Tools.psm1'; ModuleVersion = '2.0.0' }",
                ["Company.Tools.psm1"] = "# repaired payload"
            });

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("ModuleRoot", moduleRoot.Path)
            .AddParameter("Name", new[] { "Company.Tools" })
            .AddParameter("Version", "2.0.0")
            .AddParameter("Cleanup", "OldVersions")
            .AddParameter("Repository", feed.Path)
            .AddParameter("Force")
            .AddParameter("Confirm", false);

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        var repairAction = Assert.Single(result.Plan.Actions, static action => action.Kind == "Update");
        Assert.True(repairAction.IsRepair);
        Assert.True(repairAction.Force);
        Assert.Single(result.Plan.Actions, static action => action.Kind == "Remove");
        Assert.Contains(result.Apply.ExecutionResults, static execution => execution.Operation == "Update" && execution.Succeeded);
        Assert.Contains(result.Apply.ExecutionResults, static execution => execution.Operation == "Remove" && execution.Succeeded);
        Assert.False(Directory.Exists(oldPath));
        Assert.True(Directory.Exists(currentPath));
        Assert.Contains("repaired payload", File.ReadAllText(Path.Combine(currentPath, "Company.Tools.psm1")), StringComparison.Ordinal);
        Assert.True(result.Apply.ExecutionSucceeded);
        Assert.True(result.Apply.Converged);
    }

    private static string CreateInstalledModule(string moduleRoot, string name, string version)
    {
        var modulePath = Path.Combine(moduleRoot, name, version);
        Directory.CreateDirectory(modulePath);
        File.WriteAllText(
            Path.Combine(modulePath, name + ".psd1"),
            "@{ RootModule = '" + name + ".psm1'; ModuleVersion = '" + version + "' }");
        File.WriteAllText(Path.Combine(modulePath, name + ".psm1"), string.Empty);
        return modulePath;
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
