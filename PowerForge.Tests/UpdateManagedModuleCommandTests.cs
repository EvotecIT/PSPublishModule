using System.Management.Automation;
using PowerForge;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed class UpdateManagedModuleCommandTests
{
    [Fact]
    public void UpdateManagedModule_WithoutName_UpdatesInstalledModulesInSelectedRoot()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        foreach (var name in new[] { "Company.Tools", "Company.Other" })
        {
            TestPackageFactory.Create(
                Path.Combine(feed.Path, name + ".1.1.0.nupkg"),
                name,
                "1.1.0",
                files: CreateModuleFiles(name, "1.1.0"));
            var installedPath = Path.Combine(moduleRoot.Path, name, "1.0.0");
            Directory.CreateDirectory(installedPath);
            File.WriteAllText(Path.Combine(installedPath, name + ".psd1"), "@{ ModuleVersion = '1.0.0' }");
        }

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Update-ManagedModule")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", moduleRoot.Path)
            .AddParameter("AllowClobber");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var updateResults = results.Select(result => Assert.IsType<ManagedModuleUpdateResult>(result.BaseObject)).ToArray();
        Assert.Equal(new[] { "Company.Other", "Company.Tools" }, updateResults.Select(result => result.Name).OrderBy(static name => name, StringComparer.Ordinal).ToArray());
        Assert.All(updateResults, result =>
        {
            Assert.Equal("1.0.0", result.PreviousVersion);
            Assert.Equal("1.1.0", result.TargetVersion);
            Assert.Equal(ManagedModuleUpdateStatus.Updated, result.Status);
        });
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.1.0", "Company.Tools.psd1")));
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Other", "1.1.0", "Company.Other.psd1")));
    }

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

    [Fact]
    public void UpdateManagedModule_repairs_installed_family_member_version_mismatch()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Microsoft.Graph.Users.2.38.0.nupkg"),
            "Microsoft.Graph.Users",
            "2.38.0",
            files: CreateModuleFiles("Microsoft.Graph.Users", "2.38.0"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Microsoft.Graph.Authentication.2.38.0.nupkg"),
            "Microsoft.Graph.Authentication",
            "2.38.0",
            files: CreateModuleFiles("Microsoft.Graph.Authentication", "2.38.0"));
        var authPath = Path.Combine(moduleRoot.Path, "Microsoft.Graph.Authentication", "2.36.0");
        Directory.CreateDirectory(authPath);
        File.WriteAllText(
            Path.Combine(authPath, "Microsoft.Graph.Authentication.psd1"),
            "@{ ModuleVersion = '2.36.0' }");
        var usersPath = Path.Combine(moduleRoot.Path, "Microsoft.Graph.Users", "2.38.0");
        Directory.CreateDirectory(usersPath);
        File.WriteAllText(
            Path.Combine(usersPath, "Microsoft.Graph.Users.psd1"),
            "@{ ModuleVersion = '2.38.0' }");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Update-ManagedModule")
            .AddParameter("Name", "Microsoft.Graph.Users")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", moduleRoot.Path)
            .AddParameter("Version", "2.38.0")
            .AddParameter("FamilyName", "MicrosoftGraph")
            .AddParameter("FamilyModuleNamePrefix", "Microsoft.Graph.")
            .AddParameter("AllowClobber");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleUpdateResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedModuleUpdateStatus.UpToDate, result.Status);
        var family = Assert.Single(result.FamilyResults);
        Assert.Equal("Microsoft.Graph.Authentication", family.Name);
        Assert.Equal("MicrosoftGraph", family.FamilyName);
        Assert.Equal("2.36.0", family.PreviousVersion);
        Assert.Equal("2.38.0", family.TargetVersion);
        Assert.Equal(ManagedModuleFamilyUpdatePlanAction.Update, family.Action);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Microsoft.Graph.Authentication", "2.38.0", "Microsoft.Graph.Authentication.psd1")));
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

    private static IReadOnlyDictionary<string, string> CreateModuleFiles(string name, string version)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [name + ".psd1"] = "@{ ModuleVersion = '" + version + "' }"
        };

    private static void AssertNoPowerShellErrors(PowerShell ps)
    {
        if (ps.HadErrors)
            throw new InvalidOperationException(string.Join(Environment.NewLine, ps.Streams.Error.Select(error => error.ToString())));
    }
}
