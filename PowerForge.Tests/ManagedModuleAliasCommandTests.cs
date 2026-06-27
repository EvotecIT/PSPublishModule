using System.Management.Automation;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed class ManagedModuleAliasCommandTests
{
    [Theory]
    [InlineData("Find-PublicModule", "Find-ManagedModule")]
    [InlineData("Install-PublicModule", "Install-ManagedModule")]
    [InlineData("Publish-PublicModule", "Publish-ManagedModule")]
    [InlineData("Save-PublicModule", "Save-ManagedModule")]
    [InlineData("Update-PublicModule", "Update-ManagedModule")]
    public void Managed_module_public_aliases_resolve_to_managed_commands(string aliasName, string commandName)
    {
        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Get-Command")
            .AddArgument(aliasName);

        var command = Assert.IsType<AliasInfo>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.Equal(commandName, command.Definition);
    }

    [Fact]
    public void PublishManagedModule_exposes_publish_compatibility_switches()
    {
        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Get-Command")
            .AddArgument("Publish-ManagedModule");

        var command = Assert.IsType<CmdletInfo>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.True(command.Parameters.ContainsKey("SkipDependenciesCheck"));
        Assert.True(command.Parameters.ContainsKey("SkipModuleManifestValidate"));
    }

    [Fact]
    public void UpdateManagedModule_exposes_loaded_module_safety_parameters()
    {
        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Get-Command")
            .AddArgument("Update-ManagedModule");

        var command = Assert.IsType<CmdletInfo>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.True(command.Parameters.ContainsKey("LoadedModule"));
        Assert.True(command.Parameters.ContainsKey("AllowLoadedModuleUpdate"));
    }

    [Theory]
    [InlineData("Find-ManagedModule")]
    [InlineData("Install-ManagedModule")]
    [InlineData("Measure-ManagedModule")]
    [InlineData("Publish-ManagedModule")]
    [InlineData("Save-ManagedModule")]
    [InlineData("Update-ManagedModule")]
    public void Managed_module_commands_expose_repository_profile_parameter(string commandName)
    {
        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Get-Command")
            .AddArgument(commandName);

        var command = Assert.IsType<CmdletInfo>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.True(command.Parameters.ContainsKey("ProfileName"));
    }

    [Fact]
    public void FindManagedModule_uses_repository_profile_source()
    {
        using var feed = new TemporaryDirectory();
        using var profileRoot = new TemporaryDirectory();
        using var profileScope = UseProfileStore(profileRoot.Path);
        TestPackageFactory.Create(Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"), "Company.Tools", "1.0.0");
        new ModuleRepositoryProfileStore().SaveProfile(new ModuleRepositoryProfile
        {
            Name = "Company",
            Provider = PrivateGalleryProvider.NuGet,
            RepositoryName = "CompanyModules",
            RepositoryUri = feed.Path,
            RepositorySourceUri = "https://example.invalid/v2",
            RepositoryPublishUri = "https://example.invalid/v2"
        });

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Find-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("ProfileName", "Company");

        var result = Assert.IsType<ManagedModuleVersionInfo>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.Equal("Company.Tools", result.Name);
        Assert.Equal("1.0.0", result.Version);
        Assert.Equal("CompanyModules", result.RepositoryName);
    }

    [Fact]
    public void PublishManagedModule_uses_repository_profile_publish_source()
    {
        using var moduleRoot = new TemporaryDirectory();
        using var feed = new TemporaryDirectory();
        using var profileRoot = new TemporaryDirectory();
        using var profileScope = UseProfileStore(profileRoot.Path);
        CreateModule(moduleRoot.Path, "Company.Tools", "1.0.0");
        new ModuleRepositoryProfileStore().SaveProfile(new ModuleRepositoryProfile
        {
            Name = "Company",
            Provider = PrivateGalleryProvider.NuGet,
            RepositoryName = "CompanyModules",
            RepositoryUri = "https://example.invalid/v3/index.json",
            RepositorySourceUri = "https://example.invalid/v2",
            RepositoryPublishUri = feed.Path
        });

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Publish-ManagedModule")
            .AddParameter("Path", moduleRoot.Path)
            .AddParameter("ProfileName", "Company")
            .AddParameter("SkipDependenciesCheck");

        var result = Assert.IsType<ManagedModulePublishResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.True(result.Published);
        Assert.Equal(feed.Path, result.RepositorySource);
        Assert.True(File.Exists(Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg")));
    }

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

    private static IDisposable UseProfileStore(string root)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "profiles.json");
        return new TestEnvironmentVariable("POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH", path);
    }

    private static void CreateModule(string root, string name, string version)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, name + ".psm1"), "function Get-CompanyTool { 'ok' }");
        File.WriteAllText(Path.Combine(root, name + ".psd1"), $$"""
@{
    RootModule = '{{name}}.psm1'
    ModuleVersion = '{{version}}'
    Author = 'Evotec'
    Description = 'Company tools module.'
    PrivateData = @{
        PSData = @{
            Tags = @('company', 'automation')
        }
    }
}
""");
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
