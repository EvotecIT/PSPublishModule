using System.Management.Automation;
using PSPublishModule;

namespace PowerForge.Tests;

[Collection("ModuleRepositoryProfileEnvironment")]
public sealed class RepairManagedModuleVisibilityTests
{
    [Fact]
    public void Cleanup_UsesAllAnonymousRootsVisibleThroughCurrentPSModulePath()
    {
        using var workspace = new TemporaryDirectory();
        var globalRoot = Path.Combine(workspace.Path, "PowerShell", "Modules");
        var dependentRoot = Path.Combine(workspace.Path, "WindowsPowerShell", "Modules");
        var alternativeRoot = Path.Combine(workspace.Path, "PowerShell", "AlternativeModules");
        var oldPath = CreateInstalledModule(globalRoot, "Company.Core", "1.0.0");
        var currentPath = CreateInstalledModule(globalRoot, "Company.Core", "2.0.0");
        var dependentPath = CreateInstalledModule(
            dependentRoot,
            "Company.Tools",
            "1.0.0",
            requiredModuleName: "Company.Core",
            requiredModuleVersion: "1.0.0");
        var visibleAlternative = CreateInstalledModule(alternativeRoot, "Company.Core", "1.0.0");

        using var ps = CreatePowerShellWithModuleImported();
        using var modulePathScope = new TestEnvironmentVariable(
            "PSModulePath",
            string.Join(Path.PathSeparator.ToString(), globalRoot, dependentRoot, alternativeRoot));
        ps.AddCommand("Repair-ManagedModule")
            .AddParameter("ModuleRoot", globalRoot)
            .AddParameter("Name", new[] { "Company.Core" })
            .AddParameter("Cleanup", "OldVersions")
            .AddParameter("Confirm", false);

        var result = Assert.IsType<ModuleStateWorkflowResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.All(result.Inventory.ScannedPaths, static path => Assert.Equal(
            ModuleStateInventoryCommandSupport.CurrentProcessModulePathVisibilityGroup,
            path.DependencyVisibilityGroup));
        Assert.Contains(result.Inventory.ScannedPaths, static path => path.PowerShellEdition == "Core");
        Assert.Contains(result.Inventory.ScannedPaths, static path => path.PowerShellEdition == "Desktop");
        var execution = Assert.Single(result.Apply.ExecutionResults, static execution => execution.Operation == "Remove");
        Assert.True(execution.Succeeded);
        Assert.False(Directory.Exists(oldPath));
        Assert.True(Directory.Exists(currentPath));
        Assert.True(Directory.Exists(dependentPath));
        Assert.True(Directory.Exists(visibleAlternative));
        Assert.True(result.Apply.Converged);
    }

    private static string CreateInstalledModule(
        string moduleRoot,
        string name,
        string version,
        string? requiredModuleName = null,
        string? requiredModuleVersion = null)
    {
        var modulePath = Path.Combine(moduleRoot, name, version);
        Directory.CreateDirectory(modulePath);
        var requiredModules = string.IsNullOrWhiteSpace(requiredModuleName)
            ? string.Empty
            : "; RequiredModules = @(@{ ModuleName = '" + requiredModuleName + "'; RequiredVersion = '" + requiredModuleVersion + "' })";
        File.WriteAllText(
            Path.Combine(modulePath, name + ".psd1"),
            "@{ RootModule = '" + name + ".psm1'; ModuleVersion = '" + version + "'" + requiredModules + " }");
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

    private sealed class TestEnvironmentVariable : IDisposable
    {
        private readonly string _name;
        private readonly string? _previousValue;

        internal TestEnvironmentVariable(string name, string value)
        {
            _name = name;
            _previousValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
            => Environment.SetEnvironmentVariable(_name, _previousValue);
    }
}
