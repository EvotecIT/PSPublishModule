using System.Management.Automation;
using PowerForge;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed class PrivateManagedModuleCommandTests
{
    [Fact]
    public void InstallPrivateModule_can_use_managed_transport_against_local_feed()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Install-PrivateModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Transport", ModuleStateDeliveryTransport.ManagedModule)
            .AddParameter("Path", moduleRoot.Path)
            .AddParameter("RequiredVersion", "1.0.0");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ModuleDependencyInstallResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ModuleDependencyInstallStatus.Installed, result.Status);
        Assert.Equal("ManagedModule", result.Installer);
        Assert.Equal("1.0.0", result.ResolvedVersion);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
    }

    [Fact]
    public void UpdatePrivateModule_can_use_managed_transport_against_local_feed()
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
        Directory.CreateDirectory(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Update-PrivateModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Transport", ModuleStateDeliveryTransport.ManagedModule)
            .AddParameter("Path", moduleRoot.Path);
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ModuleDependencyInstallResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ModuleDependencyInstallStatus.Updated, result.Status);
        Assert.Equal("ManagedModule", result.Installer);
        Assert.Equal("1.0.0", result.InstalledVersion);
        Assert.Equal("1.1.0", result.ResolvedVersion);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.1.0", "Company.Tools.psd1")));
    }

    [Fact]
    public void Managed_transport_rejects_registered_repository_name_without_source()
    {
        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Install-PrivateModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", "Company")
            .AddParameter("Transport", ModuleStateDeliveryTransport.ManagedModule);

        var exception = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());

        Assert.Contains(
            "registered PowerShell repository name",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, string> CreateModuleFiles(string version)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Tools.psd1"] = "@{ ModuleVersion = '" + version + "' }"
        };

    private static PowerShell CreatePowerShellWithModuleImported()
    {
        var ps = PowerShell.Create();
        ps.AddCommand("Import-Module")
            .AddParameter("Name", typeof(InstallPrivateModuleCommand).Assembly.Location)
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
