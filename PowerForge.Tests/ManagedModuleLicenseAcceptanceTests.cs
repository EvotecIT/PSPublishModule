using System.Management.Automation;
using PowerForge;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed class ManagedModuleLicenseAcceptanceTests
{
    [Fact]
    public void ReadMetadata_reads_license_acceptance_requirement()
    {
        using var feed = new TemporaryDirectory();
        var packagePath = Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg");
        TestPackageFactory.Create(
            packagePath,
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"),
            requireLicenseAcceptance: true);

        var metadata = new ManagedModulePackageReader().ReadMetadata(packagePath);

        Assert.True(metadata.RequireLicenseAcceptance);
    }

    [Fact]
    public async Task InstallAsync_rejects_license_required_package_without_accept_license()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"),
            requireLicenseAcceptance: true);
        var service = new ManagedModuleInstallService(new NullLogger());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = "Company.Tools",
            Version = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path
        }));

        Assert.Contains("license acceptance", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0")));
    }

    [Fact]
    public async Task InstallAsync_installs_license_required_package_with_accept_license()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"),
            requireLicenseAcceptance: true);
        var service = new ManagedModuleInstallService(new NullLogger());

        var result = await service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = "Company.Tools",
            Version = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path,
            AcceptLicense = true
        });

        Assert.Equal(ManagedModuleInstallStatus.Installed, result.Status);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
    }

    [Fact]
    public async Task InstallAsync_rejects_license_required_dependency_without_accept_license()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Core.1.0.0.nupkg"),
            "Company.Core",
            "1.0.0",
            files: CreateDependencyModuleFiles("1.0.0"),
            requireLicenseAcceptance: true);
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Core", "[1.0.0]", null) },
            files: CreateModuleFiles("1.0.0"));
        var service = new ManagedModuleInstallService(new NullLogger());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = "Company.Tools",
            Version = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path
        }));

        Assert.Contains("Company.Core", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("license acceptance", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(moduleRoot.Path, "Company.Core", "1.0.0", "Company.Core.psd1")));
        Assert.False(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
    }

    [Fact]
    public async Task InstallAsync_installs_license_required_dependencies_with_accept_license()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Core.1.0.0.nupkg"),
            "Company.Core",
            "1.0.0",
            files: CreateDependencyModuleFiles("1.0.0"),
            requireLicenseAcceptance: true);
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Core", "[1.0.0]", null) },
            files: CreateModuleFiles("1.0.0"));
        var service = new ManagedModuleInstallService(new NullLogger());

        var result = await service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = "Company.Tools",
            Version = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path,
            AcceptLicense = true
        });

        Assert.Equal(ManagedModuleInstallStatus.Installed, result.Status);
        var dependency = Assert.Single(result.DependencyResults);
        Assert.Equal("Company.Core", dependency.Name);
        Assert.Equal(ManagedModuleInstallStatus.Installed, dependency.Status);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Core", "1.0.0", "Company.Core.psd1")));
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
    }

    [Theory]
    [InlineData("Install-ManagedModule")]
    [InlineData("Save-ManagedModule")]
    [InlineData("Update-ManagedModule")]
    public void Managed_mutating_cmdlets_expose_accept_license(string commandName)
    {
        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Get-Command")
            .AddArgument(commandName);

        var command = Assert.IsAssignableFrom<CommandInfo>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.True(command.Parameters.ContainsKey("AcceptLicense"));
    }

    private static IReadOnlyDictionary<string, string> CreateModuleFiles(string version)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Tools.psd1"] = "@{ ModuleVersion = '" + version + "' }"
        };

    private static IReadOnlyDictionary<string, string> CreateDependencyModuleFiles(string version)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Core.psd1"] = "@{ ModuleVersion = '" + version + "' }"
        };

    private static PowerShell CreatePowerShellWithModuleImported()
    {
        var ps = PowerShell.Create();
        ps.AddCommand("Import-Module")
            .AddParameter("Name", typeof(InstallManagedModuleCommand).Assembly.Location)
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
