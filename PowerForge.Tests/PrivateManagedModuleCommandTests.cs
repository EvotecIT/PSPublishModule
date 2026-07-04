using System.Management.Automation;
using PowerForge;
using PSPublishModule;

namespace PowerForge.Tests;

[Collection("ModuleRepositoryProfileEnvironment")]
public sealed class PrivateManagedModuleCommandTests
{
    [Fact]
    public void InstallManagedModule_defaults_to_managed_transport_for_local_feed()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Install-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", moduleRoot.Path)
            .AddParameter("RequiredVersion", "1.0.0");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleInstallResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedModuleInstallStatus.Installed, result.Status);
        Assert.Equal("1.0.0", result.Version);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
    }

    [Fact]
    public void InstallManagedModule_can_use_managed_transport_against_local_feed()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Install-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", moduleRoot.Path)
            .AddParameter("RequiredVersion", "1.0.0");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleInstallResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedModuleInstallStatus.Installed, result.Status);
        Assert.Equal("1.0.0", result.Version);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
    }

    [Fact]
    public void InstallManagedModule_resolves_provider_cache_path_before_multiple_async_installs()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        using var cacheRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.One.1.0.0.nupkg"),
            "Company.One",
            "1.0.0",
            files: CreateModuleFiles("1.0.0", "Company.One"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Two.1.0.0.nupkg"),
            "Company.Two",
            "1.0.0",
            files: CreateModuleFiles("1.0.0", "Company.Two"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Install-ManagedModule")
            .AddParameter("Name", new[] { "Company.One", "Company.Two" })
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", moduleRoot.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("PackageCacheDirectory", "FileSystem::" + cacheRoot.Path);
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        Assert.Equal(2, results.Count);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.One", "1.0.0", "Company.One.psd1")));
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Two", "1.0.0", "Company.Two.psd1")));
    }

    [Fact]
    public void InstallManagedModule_defaults_to_managed_transport_from_registered_repository_source()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        RegisterPSResourceRepositoryFunction(ps, "Company", feed.Path);
        ps.AddCommand("Install-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", "Company")
            .AddParameter("Path", moduleRoot.Path)
            .AddParameter("RequiredVersion", "1.0.0");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleInstallResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedModuleInstallStatus.Installed, result.Status);
        Assert.Equal("1.0.0", result.Version);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
    }

    [Fact]
    public void UpdateManagedModule_can_use_managed_transport_against_local_feed()
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
        ps.AddCommand("Update-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", moduleRoot.Path);
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleUpdateResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedModuleUpdateStatus.Updated, result.Status);
        Assert.Equal("1.0.0", result.PreviousVersion);
        Assert.Equal("1.1.0", result.TargetVersion);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.1.0", "Company.Tools.psd1")));
    }

    [Fact]
    public void UpdateManagedModule_defaults_to_managed_transport_from_repository_profile()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        using var profileRoot = new TemporaryDirectory();
        using var profileScope = UseProfileStore(profileRoot.Path);
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
        SaveProfile(feed.Path);

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Update-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("ProfileName", "Company")
            .AddParameter("Path", moduleRoot.Path);
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleUpdateResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedModuleUpdateStatus.Updated, result.Status);
        Assert.Equal("1.0.0", result.PreviousVersion);
        Assert.Equal("1.1.0", result.TargetVersion);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.1.0", "Company.Tools.psd1")));
    }

    [Fact]
    public void InstallManagedModule_can_use_managed_transport_from_repository_profile()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        using var profileRoot = new TemporaryDirectory();
        using var profileScope = UseProfileStore(profileRoot.Path);
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        SaveProfile(feed.Path);

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Install-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("ProfileName", "Company")
            .AddParameter("Path", moduleRoot.Path)
            .AddParameter("RequiredVersion", "1.0.0");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleInstallResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedModuleInstallStatus.Installed, result.Status);
        Assert.Equal("1.0.0", result.Version);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
    }

    [Fact]
    public void InstallManagedModule_accepts_required_resource_hashtable()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.One.1.0.0.nupkg"),
            "Company.One",
            "1.0.0",
            files: CreateModuleFiles("1.0.0", "Company.One"));
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Two.2.0.0-beta1.nupkg"),
            "Company.Two",
            "2.0.0-beta1",
            files: CreateModuleFiles("2.0.0-beta1", "Company.Two"));
        var requiredResource = new System.Collections.Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.One"] = new System.Collections.Hashtable(StringComparer.OrdinalIgnoreCase)
            {
                ["Version"] = "1.0.0",
                ["Repository"] = feed.Path,
                ["Reinstall"] = true
            },
            ["Company.Two"] = new System.Collections.Hashtable(StringComparer.OrdinalIgnoreCase)
            {
                ["Version"] = "2.0.0-beta1",
                ["Repository"] = feed.Path,
                ["Prerelease"] = true,
                ["SkipDependencyCheck"] = true
            }
        };

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Install-ManagedModule")
            .AddParameter("RequiredResource", requiredResource)
            .AddParameter("Path", moduleRoot.Path);
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        Assert.Equal(2, results.Count);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.One", "1.0.0", "Company.One.psd1")));
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Two", "2.0.0-beta1", "Company.Two.psd1")));
    }

    [Fact]
    public void InstallManagedModule_accepts_required_resource_file()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        using var files = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        var requiredResourceFile = Path.Combine(files.Path, "required.psd1");
        File.WriteAllText(
            requiredResourceFile,
            "@{ 'Company.Tools' = @{ Version = '1.0.0'; Repository = '" + EscapePowerShellSingleQuoted(feed.Path) + "' } }");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Install-ManagedModule")
            .AddParameter("RequiredResourceFile", requiredResourceFile)
            .AddParameter("Path", moduleRoot.Path);
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleInstallResult>(Assert.Single(results).BaseObject);
        Assert.Equal("Company.Tools", result.Name);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
    }

    [Fact]
    public void InstallManagedModule_rejects_required_resource_string_shorthand()
    {
        var requiredResource = new System.Collections.Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Tools"] = "1.0.0"
        };

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Install-ManagedModule")
            .AddParameter("RequiredResource", requiredResource)
            .AddParameter("Path", Path.GetTempPath());

        var exception = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());

        Assert.Contains("value must be a hashtable", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdateManagedModule_can_use_managed_transport_from_repository_profile()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        using var profileRoot = new TemporaryDirectory();
        using var profileScope = UseProfileStore(profileRoot.Path);
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
        SaveProfile(feed.Path);

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Update-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("ProfileName", "Company")
            .AddParameter("Path", moduleRoot.Path);
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedModuleUpdateResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedModuleUpdateStatus.Updated, result.Status);
        Assert.Equal("1.0.0", result.PreviousVersion);
        Assert.Equal("1.1.0", result.TargetVersion);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.1.0", "Company.Tools.psd1")));
    }

    [Fact]
    public void InstallManagedModule_rejects_unresolved_registered_repository_name()
    {
        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Install-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", "Company");

        var exception = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());

        Assert.Contains("registered PowerShell repository name", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, string> CreateModuleFiles(string version, string moduleName = "Company.Tools")
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [moduleName + ".psd1"] = "@{ ModuleVersion = '" + version + "' }"
        };

    private static void SaveProfile(string feedPath)
        => new ModuleRepositoryProfileStore().SaveProfile(new ModuleRepositoryProfile
        {
            Name = "Company",
            Provider = PrivateGalleryProvider.NuGet,
            RepositoryName = "CompanyModules",
            RepositoryUri = feedPath,
            RepositorySourceUri = feedPath,
            RepositoryPublishUri = feedPath
        });

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

    private static void RegisterPSResourceRepositoryFunction(PowerShell ps, string repositoryName, string source)
    {
        ps.AddScript($@"
$script:PFTestRepositoryName = '{EscapePowerShellSingleQuoted(repositoryName)}'
$script:PFTestRepositorySource = '{EscapePowerShellSingleQuoted(source)}'
function global:Get-PSResourceRepository {{
    param([string] $Name)
    if ($Name -eq $script:PFTestRepositoryName) {{
        [pscustomobject]@{{
            Name = $script:PFTestRepositoryName
            Uri = $script:PFTestRepositorySource
        }}
    }}
}}
");
        _ = ps.Invoke();
        AssertNoPowerShellErrors(ps);
        ps.Commands.Clear();
    }

    private static string EscapePowerShellSingleQuoted(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

    private static void AssertNoPowerShellErrors(PowerShell ps)
    {
        if (ps.HadErrors)
            throw new InvalidOperationException(string.Join(Environment.NewLine, ps.Streams.Error.Select(error => error.ToString())));
    }

    private static IDisposable UseProfileStore(string root)
    {
        Directory.CreateDirectory(root);
        return new TestEnvironmentVariables(
            ("POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH", Path.Combine(root, "profiles.json")),
            ("POWERFORGE_MODULE_REPOSITORY_MACHINE_PROFILE_PATH", Path.Combine(root, "machine-profiles.json")));
    }

    private sealed class TestEnvironmentVariables : IDisposable
    {
        private readonly (string Name, string? PreviousValue)[] _previousValues;

        internal TestEnvironmentVariables(params (string Name, string Value)[] values)
        {
            _previousValues = values
                .Select(value => (value.Name, Environment.GetEnvironmentVariable(value.Name)))
                .ToArray();

            foreach (var value in values)
            {
                Environment.SetEnvironmentVariable(value.Name, value.Value);
            }
        }

        public void Dispose()
        {
            foreach (var value in _previousValues)
            {
                Environment.SetEnvironmentVariable(value.Name, value.PreviousValue);
            }
        }
    }
}
