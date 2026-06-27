using System.Management.Automation;
using PowerForge;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed class UpdateManagedModuleCommandTests
{
    [Fact]
    public void UpdateManagedModule_blocks_loaded_module_update_by_default()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.1.0.nupkg"),
            "Company.Tools",
            "1.1.0",
            files: CreateModuleFiles("1.1.0"));
        var loadedPath = Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0");
        Directory.CreateDirectory(loadedPath);
        File.WriteAllText(Path.Combine(loadedPath, "Company.Tools.psd1"), "@{ ModuleVersion = '1.0.0' }");
        var loadedModule = new ManagedModuleLoadedModule
        {
            Name = "Company.Tools",
            Version = "1.0.0",
            ModuleBase = loadedPath
        };

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Update-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", moduleRoot.Path)
            .AddParameter("LoadedModule", new[] { loadedModule });
        var exception = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());

        Assert.Contains("already loaded", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AllowLoadedModuleUpdate", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.1.0")));
    }

    private static PowerShell CreatePowerShellWithModuleImported()
    {
        var ps = PowerShell.Create();
        ps.AddCommand("Import-Module")
            .AddParameter("Name", typeof(UpdateManagedModuleCommand).Assembly.Location)
            .AddParameter("Force");
        _ = ps.Invoke();
        AssertNoPowerShellErrors(ps);
        ps.Commands.Clear();
        return ps;
    }

    private static IReadOnlyDictionary<string, string> CreateModuleFiles(string version)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Tools.psd1"] = "@{ ModuleVersion = '" + version + "' }"
        };

    private static void AssertNoPowerShellErrors(PowerShell ps)
    {
        if (ps.HadErrors)
            throw new InvalidOperationException(string.Join(Environment.NewLine, ps.Streams.Error.Select(error => error.ToString())));
    }
}
