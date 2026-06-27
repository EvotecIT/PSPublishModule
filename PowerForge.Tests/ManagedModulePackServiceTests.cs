using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModulePackServiceTests
{
    [Fact]
    public void Pack_creates_readable_module_package_from_manifest()
    {
        using var moduleRoot = new TemporaryDirectory();
        using var output = new TemporaryDirectory();
        CreateModule(moduleRoot.Path, "Company.Tools", "1.2.3", prerelease: null);
        var service = new ManagedModulePackService();

        var result = service.Pack(new ManagedModulePackRequest
        {
            ModulePath = moduleRoot.Path,
            OutputDirectory = output.Path
        });

        Assert.True(File.Exists(result.PackagePath));
        Assert.Equal("Company.Tools", result.Name);
        Assert.Equal("1.2.3", result.Version);
        Assert.Equal(2, result.FileCount);

        var metadata = new ManagedModulePackageReader().ReadMetadata(result.PackagePath);
        Assert.Equal("Company.Tools", metadata.Id);
        Assert.Equal("1.2.3", metadata.Version);
        Assert.Equal("Evotec", metadata.Authors);
        Assert.Equal(new[] { "automation", "company" }, metadata.Tags);
    }

    [Fact]
    public void Pack_appends_manifest_prerelease_label()
    {
        using var moduleRoot = new TemporaryDirectory();
        using var output = new TemporaryDirectory();
        CreateModule(moduleRoot.Path, "Company.Tools", "2.0.0", prerelease: "beta1");
        var service = new ManagedModulePackService();

        var result = service.Pack(new ManagedModulePackRequest
        {
            ModulePath = moduleRoot.Path,
            OutputDirectory = output.Path
        });

        Assert.Equal("2.0.0-beta1", result.Version);
        var metadata = new ManagedModulePackageReader().ReadMetadata(result.PackagePath);
        Assert.Equal("2.0.0-beta1", metadata.Version);
    }

    [Fact]
    public async Task Packed_module_can_be_installed_from_local_feed()
    {
        using var moduleRoot = new TemporaryDirectory();
        using var feed = new TemporaryDirectory();
        using var installRoot = new TemporaryDirectory();
        CreateModule(moduleRoot.Path, "Company.Tools", "1.0.0", prerelease: null);
        var package = new ManagedModulePackService().Pack(new ManagedModulePackRequest
        {
            ModulePath = moduleRoot.Path,
            OutputDirectory = feed.Path
        });

        var install = await new ManagedModuleInstallService(new NullLogger()).InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = package.Name,
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = installRoot.Path
        });

        Assert.Equal(ManagedModuleInstallStatus.Installed, install.Status);
        Assert.True(File.Exists(Path.Combine(installRoot.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
        Assert.True(File.Exists(Path.Combine(installRoot.Path, "Company.Tools", "1.0.0", "Company.Tools.psm1")));
    }

    [Fact]
    public async Task Publish_classifies_local_feed_duplicate_without_force()
    {
        using var moduleRoot = new TemporaryDirectory();
        using var feed = new TemporaryDirectory();
        CreateModule(moduleRoot.Path, "Company.Tools", "1.0.0", prerelease: null);
        var destinationPath = Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg");
        File.WriteAllText(destinationPath, "existing");
        var service = new ManagedModulePublishService(new NullLogger());

        var result = await service.PublishAsync(new ManagedModulePublishRequest
        {
            ModulePath = moduleRoot.Path,
            Repository = new ManagedModuleRepository("Local", feed.Path)
        });

        Assert.False(result.Published);
        Assert.True(result.Duplicate);
        Assert.Equal("existing", File.ReadAllText(destinationPath));
        Assert.Contains("already exists", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Publish_overwrites_local_feed_duplicate_with_force()
    {
        using var moduleRoot = new TemporaryDirectory();
        using var feed = new TemporaryDirectory();
        CreateModule(moduleRoot.Path, "Company.Tools", "1.0.0", prerelease: null);
        var destinationPath = Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg");
        File.WriteAllText(destinationPath, "existing");
        var service = new ManagedModulePublishService(new NullLogger());

        var result = await service.PublishAsync(new ManagedModulePublishRequest
        {
            ModulePath = moduleRoot.Path,
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Force = true
        });

        Assert.True(result.Published);
        Assert.False(result.Duplicate);
        Assert.NotEqual("existing", File.ReadAllText(destinationPath));
    }

    private static void CreateModule(string root, string name, string version, string? prerelease)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, name + ".psm1"), "function Get-CompanyTool { 'ok' }");
        File.WriteAllText(Path.Combine(root, name + ".psd1"), CreateManifest(name, version, prerelease));
    }

    private static string CreateManifest(string name, string version, string? prerelease)
    {
        var prereleaseLine = string.IsNullOrWhiteSpace(prerelease)
            ? string.Empty
            : $"            Prerelease = '{prerelease}'";

        return $$"""
@{
    RootModule = '{{name}}.psm1'
    ModuleVersion = '{{version}}'
    Author = 'Evotec'
    Description = 'Company tools module.'
    PrivateData = @{
        PSData = @{
            Tags = @('company', 'automation')
{{prereleaseLine}}
        }
    }
}
""";
    }
}
