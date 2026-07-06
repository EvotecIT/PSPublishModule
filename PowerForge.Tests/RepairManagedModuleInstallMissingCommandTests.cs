using System.Management.Automation;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed class RepairManagedModuleInstallMissingCommandTests
{
    [Fact]
    public void RepairManagedModule_NameInstallMissingPlansInstallForMissingLiteralName()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("Name", new[] { "Company.Tools", "Company.Missing" })
            .AddParameter("InstallMissing")
            .AddParameter("Latest")
            .AddParameter("Repository", feed.Path)
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.Contains(result.Plan.Actions, static action =>
            string.Equals(action.ModuleName, "Company.Tools", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(action.Kind, "Update", StringComparison.OrdinalIgnoreCase));
        var missingAction = Assert.Single(result.Plan.Actions, static action =>
            string.Equals(action.ModuleName, "Company.Missing", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Install", missingAction.Kind);
        Assert.Equal("*", missingAction.VersionPolicy);
        Assert.Equal("Local", missingAction.TargetRepository);
        var missingCommand = Assert.Single(result.Apply.Commands, static command =>
            string.Equals(command.ModuleName, "Company.Missing", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Install-ManagedModule", missingCommand.CommandName);
        Assert.Contains("-ModuleRoot", missingCommand.Arguments);
        Assert.Contains(moduleRoot.Path, missingCommand.Arguments);
        Assert.False(result.Apply.ExecutionRequested);
    }

    [Fact]
    public void RepairManagedModule_NameWithoutInstallMissingKeepsMissingNamesOutOfPlan()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Tools", "1.0.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("Name", new[] { "Company.Tools", "Company.Missing" })
            .AddParameter("Latest")
            .AddParameter("Repository", feed.Path)
            .AddParameter("Plan");

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.DoesNotContain(result.Plan.Actions, static action =>
            string.Equals(action.ModuleName, "Company.Missing", StringComparison.OrdinalIgnoreCase));
        Assert.Single(result.Plan.Actions);
    }

    [Fact]
    public void RepairManagedModule_NameInstallMissingAppliesMissingInstallToInventoriedRoot()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Missing.1.1.0.nupkg"),
            "Company.Missing",
            "1.1.0",
            files: CreateModuleFiles("Company.Missing", "1.1.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("Name", new[] { "Company.Missing" })
            .AddParameter("InstallMissing")
            .AddParameter("Latest")
            .AddParameter("Repository", feed.Path);

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.True(result.Apply.ExecutionRequested);
        var execution = Assert.Single(result.Apply.ExecutionResults);
        Assert.Equal("Install", execution.Operation);
        Assert.True(execution.OperationPerformed);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Missing", "1.1.0", "Company.Missing.psd1")));
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

    private static IReadOnlyDictionary<string, string> CreateModuleFiles(string name, string version)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [name + ".psd1"] = "@{ RootModule = '" + name + ".psm1'; ModuleVersion = '" + version + "' }",
            [name + ".psm1"] = string.Empty
        };

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
