using System.Management.Automation;
using PSPublishModule;

namespace PowerForge.Tests;

[Collection("ModuleRepositoryProfileEnvironment")]
public sealed class ManagedModuleAliasCommandTests
{
    [Theory]
    [InlineData("Find-PublicModule")]
    [InlineData("Install-PublicModule")]
    [InlineData("Publish-PublicModule")]
    [InlineData("Save-PublicModule")]
    [InlineData("Update-PublicModule")]
    public void Managed_module_public_aliases_are_not_exported(string aliasName)
    {
        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Get-Command")
            .AddParameter("Name", aliasName)
            .AddParameter("ErrorAction", ActionPreference.Ignore);

        var results = ps.Invoke();
        ps.Streams.Error.Clear();

        Assert.Empty(ps.Streams.Error);
        Assert.Empty(results);
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
    [InlineData("Install-ManagedModule")]
    [InlineData("Save-ManagedModule")]
    [InlineData("Update-ManagedModule")]
    public void Managed_module_delivery_commands_expose_trust_policy_parameters(string commandName)
    {
        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Get-Command")
            .AddArgument(commandName);

        var command = Assert.IsType<CmdletInfo>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.True(command.Parameters.ContainsKey("TrustPolicy"));
        Assert.True(command.Parameters.ContainsKey("RequireTrustedRepository"));
        Assert.True(command.Parameters.ContainsKey("AllowedAuthor"));
    }

    [Theory]
    [InlineData("Find-ManagedModule")]
    [InlineData("Install-ManagedModule")]
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

    [Theory]
    [InlineData("Find-ManagedModule")]
    [InlineData("Install-ManagedModule")]
    [InlineData("Publish-ManagedModule")]
    [InlineData("Save-ManagedModule")]
    [InlineData("Update-ManagedModule")]
    public void Managed_module_repository_commands_expose_proxy_parameters(string commandName)
    {
        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Get-Command")
            .AddArgument(commandName);

        var command = Assert.IsType<CmdletInfo>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.True(command.Parameters.ContainsKey("Proxy"));
        Assert.True(command.Parameters.ContainsKey("ProxyCredential"));
    }

    [Fact]
    public void FindManagedModule_exposes_psresourceget_search_parameters()
    {
        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Get-Command")
            .AddArgument("Find-ManagedModule");

        var command = Assert.IsType<CmdletInfo>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.True(command.Parameters.ContainsKey("Tag"));
        Assert.True(command.Parameters.ContainsKey("ResourceType"));
        Assert.True(command.Parameters.ContainsKey("IncludeDependencies"));
        Assert.Contains("Tags", command.Parameters["Tag"].Aliases);
        Assert.Contains("Type", command.Parameters["ResourceType"].Aliases);
    }

    [Theory]
    [InlineData("Find-ManagedModule", "Name", "ModuleName")]
    [InlineData("Find-ManagedModule", "Repository", "Source")]
    [InlineData("Find-ManagedModule", "Repository", "RepositoryUri")]
    [InlineData("Find-ManagedModule", "Prerelease", "AllowPrerelease")]
    [InlineData("Install-ManagedModule", "Version", "RequiredVersion")]
    [InlineData("Install-ManagedModule", "Repository", "Source")]
    [InlineData("Install-ManagedModule", "Repository", "RepositoryUri")]
    [InlineData("Install-ManagedModule", "Prerelease", "AllowPrerelease")]
    [InlineData("Install-ManagedModule", "ModuleRoot", "Path")]
    [InlineData("Install-ManagedModule", "SkipDependencyCheck", "SkipDependenciesCheck")]
    [InlineData("Save-ManagedModule", "Version", "RequiredVersion")]
    [InlineData("Save-ManagedModule", "Repository", "Source")]
    [InlineData("Save-ManagedModule", "Repository", "RepositoryUri")]
    [InlineData("Save-ManagedModule", "Prerelease", "AllowPrerelease")]
    [InlineData("Save-ManagedModule", "Path", "DestinationPath")]
    [InlineData("Save-ManagedModule", "SkipDependencyCheck", "SkipDependenciesCheck")]
    [InlineData("Update-ManagedModule", "Version", "RequiredVersion")]
    [InlineData("Update-ManagedModule", "Repository", "Source")]
    [InlineData("Update-ManagedModule", "Repository", "RepositoryUri")]
    [InlineData("Update-ManagedModule", "Prerelease", "AllowPrerelease")]
    [InlineData("Update-ManagedModule", "ModuleRoot", "Path")]
    [InlineData("Update-ManagedModule", "SkipDependencyCheck", "SkipDependenciesCheck")]
    [InlineData("Repair-ManagedModule", "Version", "RequiredVersion")]
    [InlineData("Repair-ManagedModule", "Repository", "Source")]
    [InlineData("Repair-ManagedModule", "Repository", "RepositoryUri")]
    [InlineData("Repair-ManagedModule", "Prerelease", "AllowPrerelease")]
    [InlineData("Repair-ManagedModule", "ModuleRoot", "Path")]
    [InlineData("Publish-ManagedModule", "Path", "ModulePath")]
    [InlineData("Publish-ManagedModule", "Repository", "RepositoryUri")]
    [InlineData("Publish-ManagedModule", "Repository", "Source")]
    [InlineData("Publish-ManagedModule", "ApiKey", "NuGetApiKey")]
    [InlineData("Publish-ManagedModule", "ApiKeyFilePath", "NuGetApiKeyPath")]
    [InlineData("Publish-ManagedModule", "OutputDirectory", "DestinationPath")]
    public void Managed_module_commands_expose_safe_migration_aliases(string commandName, string parameterName, string aliasName)
    {
        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Get-Command")
            .AddArgument(commandName);

        var command = Assert.IsType<CmdletInfo>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.True(command.Parameters.TryGetValue(parameterName, out var parameter), $"Parameter '{parameterName}' was not found on {commandName}.");
        Assert.Contains(aliasName, parameter.Aliases);
    }

    [Theory]
    [InlineData("Install-ManagedModule")]
    [InlineData("Save-ManagedModule")]
    [InlineData("Update-ManagedModule")]
    [InlineData("Repair-ManagedModule")]
    public void Managed_module_delivery_commands_do_not_expose_unsafe_migration_switches(string commandName)
    {
        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Get-Command")
            .AddArgument(commandName);

        var command = Assert.IsType<CmdletInfo>(Assert.Single(ps.Invoke()).BaseObject);
        var aliases = command.Parameters.Values
            .SelectMany(static parameter => parameter.Aliases)
            .ToArray();

        AssertNoPowerShellErrors(ps);
        Assert.False(command.Parameters.ContainsKey("TrustRepository"), $"{commandName} should use RequireTrustedRepository instead of a misleading TrustRepository switch.");
        Assert.False(command.Parameters.ContainsKey("SkipPublisherCheck"), $"{commandName} should not expose SkipPublisherCheck until managed publisher-check semantics exist.");
        Assert.DoesNotContain("TrustRepository", aliases);
        Assert.DoesNotContain("SkipPublisherCheck", aliases);
    }

    [Fact]
    public void Managed_module_proxy_credential_requires_proxy()
    {
        using var feed = new TemporaryDirectory();
        using var ps = CreatePowerShellWithModuleImported();
        ps.AddScript($$"""
            $secure = ConvertTo-SecureString 'secret' -AsPlainText -Force
            $proxyCredential = [pscredential]::new('proxy-user', $secure)
            Find-ManagedModule -Name Company.Tools -Repository '{{EscapePowerShellString(feed.Path)}}' -ProxyCredential $proxyCredential
            """);

        ps.Invoke();

        Assert.Contains(ps.Streams.Error, error => error.Exception.Message.Contains("ProxyCredential requires Proxy", StringComparison.OrdinalIgnoreCase));
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
    public void FindManagedModule_accepts_local_feed_file_uri()
    {
        using var feed = new TemporaryDirectory();
        TestPackageFactory.Create(Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"), "Company.Tools", "1.0.0");
        var feedUri = new Uri(feed.Path).AbsoluteUri;

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Find-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feedUri);

        var result = Assert.IsType<ManagedModuleVersionInfo>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.Equal("Company.Tools", result.Name);
        Assert.Equal("1.0.0", result.Version);
    }

    [Fact]
    public void FindManagedModule_wildcard_all_versions_expands_each_match()
    {
        using var feed = new TemporaryDirectory();
        TestPackageFactory.Create(Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"), "Company.Tools", "1.0.0");
        TestPackageFactory.Create(Path.Combine(feed.Path, "Company.Tools.1.1.0.nupkg"), "Company.Tools", "1.1.0");
        TestPackageFactory.Create(Path.Combine(feed.Path, "Company.Core.2.0.0.nupkg"), "Company.Core", "2.0.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Find-ManagedModule")
            .AddParameter("Name", "Company.*")
            .AddParameter("Repository", feed.Path)
            .AddParameter("AllVersions");

        var result = ps.Invoke()
            .Select(static item => Assert.IsType<ManagedModuleVersionInfo>(item.BaseObject))
            .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Version, ManagedModuleVersionComparer.Instance)
            .ToArray();

        AssertNoPowerShellErrors(ps);
        Assert.Equal(new[] { "Company.Core:2.0.0", "Company.Tools:1.0.0", "Company.Tools:1.1.0" },
            result.Select(static item => item.Name + ":" + item.Version));
    }

    [Fact]
    public void FindManagedModule_filters_local_feed_by_tag()
    {
        using var feed = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            tags: "automation reporting");
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Data.1.0.0.nupkg"),
            "Company.Data",
            "1.0.0",
            tags: "database storage");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Find-ManagedModule")
            .AddParameter("Name", "Company.*")
            .AddParameter("Repository", feed.Path)
            .AddParameter("Tag", new[] { "auto*" })
            .AddParameter("Type", "Module");

        var result = Assert.IsType<ManagedModuleVersionInfo>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.Equal("Company.Tools", result.Name);
        Assert.Equal("Module", result.ResourceType);
        Assert.Contains("automation", result.Tags);
    }

    [Fact]
    public void FindManagedModule_include_dependencies_returns_satisfying_dependency_resources()
    {
        using var feed = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Core", "[2.0.0, )", null) });
        TestPackageFactory.Create(Path.Combine(feed.Path, "Company.Core.1.0.0.nupkg"), "Company.Core", "1.0.0");
        TestPackageFactory.Create(Path.Combine(feed.Path, "Company.Core.2.1.0.nupkg"), "Company.Core", "2.1.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Find-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("IncludeDependencies");

        var result = ps.Invoke()
            .Select(static item => Assert.IsType<ManagedModuleVersionInfo>(item.BaseObject))
            .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        AssertNoPowerShellErrors(ps);
        Assert.Equal(new[] { "Company.Core:2.1.0", "Company.Tools:1.0.0" },
            result.Select(static item => item.Name + ":" + item.Version));
    }

    [Fact]
    public void InstallManagedModule_requires_trusted_repository_profile_when_requested()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        using var profileRoot = new TemporaryDirectory();
        using var profileScope = UseProfileStore(profileRoot.Path);
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Company.Tools.psd1"] = "@{ ModuleVersion = '1.0.0' }"
            });
        new ModuleRepositoryProfileStore().SaveProfile(new ModuleRepositoryProfile
        {
            Name = "Company",
            Provider = PrivateGalleryProvider.NuGet,
            RepositoryName = "CompanyModules",
            RepositoryUri = feed.Path,
            Trusted = false
        });

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Install-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("ProfileName", "Company")
            .AddParameter("Scope", ManagedModuleInstallScope.Custom)
            .AddParameter("ModuleRoot", moduleRoot.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("RequireTrustedRepository");

        var exception = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());

        Assert.Contains("not trusted", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools")));
    }

    [Fact]
    public void InstallManagedModule_rejects_raw_repository_when_trust_is_required()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Company.Tools.psd1"] = "@{ ModuleVersion = '1.0.0' }"
            });

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Install-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("Scope", ManagedModuleInstallScope.Custom)
            .AddParameter("ModuleRoot", moduleRoot.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("RequireTrustedRepository");

        var exception = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());

        Assert.Contains("not trusted", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools")));
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
        Assert.Equal("https://example.invalid/v2", result.RepositorySource);
        Assert.Equal(feed.Path, Path.GetDirectoryName(result.PublishSource));
        Assert.True(File.Exists(Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg")));
    }

    [Fact]
    public void PublishManagedModule_uses_profile_source_for_dependency_checks_and_publish_uri_for_upload()
    {
        using var moduleRoot = new TemporaryDirectory();
        using var readFeed = new TemporaryDirectory();
        using var publishFeed = new TemporaryDirectory();
        using var profileRoot = new TemporaryDirectory();
        using var profileScope = UseProfileStore(profileRoot.Path);
        CreateModule(
            moduleRoot.Path,
            "Company.Tools",
            "1.0.0",
            requiredModules: "    RequiredModules = @(@{ ModuleName = 'Company.Core'; RequiredVersion = '2.0.0' })");
        TestPackageFactory.Create(Path.Combine(readFeed.Path, "Company.Core.2.0.0.nupkg"), "Company.Core", "2.0.0");
        new ModuleRepositoryProfileStore().SaveProfile(new ModuleRepositoryProfile
        {
            Name = "Company",
            Provider = PrivateGalleryProvider.NuGet,
            RepositoryName = "CompanyModules",
            RepositoryUri = "https://example.invalid/v3/index.json",
            RepositorySourceUri = readFeed.Path,
            RepositoryPublishUri = publishFeed.Path
        });

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Publish-ManagedModule")
            .AddParameter("Path", moduleRoot.Path)
            .AddParameter("ProfileName", "Company");

        var result = Assert.IsType<ManagedModulePublishResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.True(result.Published);
        Assert.Equal(readFeed.Path, result.RepositorySource);
        Assert.Equal(publishFeed.Path, Path.GetDirectoryName(result.PublishSource));
        Assert.False(File.Exists(Path.Combine(readFeed.Path, "Company.Tools.1.0.0.nupkg")));
        Assert.True(File.Exists(Path.Combine(publishFeed.Path, "Company.Tools.1.0.0.nupkg")));
    }

    [Fact]
    public void InitializeManagedModuleRepository_rejects_nuget_source_only_profile()
    {
        using var profileRoot = new TemporaryDirectory();
        using var profileScope = UseProfileStore(profileRoot.Path);
        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Initialize-ManagedModuleRepository")
            .AddParameter("ProfileName", "Company")
            .AddParameter("Provider", PrivateGalleryProvider.NuGet)
            .AddParameter("RepositoryName", "CompanyModules")
            .AddParameter("RepositorySourceUri", "https://packages.example.test/api/v2")
            .AddParameter("SkipConnect");

        var exception = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());

        Assert.Contains("RepositoryUri", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstallManagedModule_rejects_single_expected_hash_for_multiple_modules()
    {
        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Install-ManagedModule")
            .AddParameter("Name", new[] { "Company.One", "Company.Two" })
            .AddParameter("ExpectedPackageSha256", new string('a', 64));

        var exception = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());

        Assert.Contains("ExpectedPackageSha256", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exactly one module", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ManagedModuleCommandSupport_resolves_builtin_psgallery_without_native_registration()
    {
        var source = ManagedModuleCommandSupport.ResolveRepositorySource(
            null!,
            ManagedModuleCommandSupport.DefaultRepositoryName,
            out var resolvedRegisteredRepositoryName,
            out var trusted);

        Assert.Equal(ManagedModuleCommandSupport.DefaultRepositorySource, source);
        Assert.Null(resolvedRegisteredRepositoryName);
        Assert.False(trusted);
    }

    [Fact]
    public void ManagedModuleCommandSupport_names_explicit_non_gallery_urls_from_source()
    {
        var repositoryName = ManagedModuleCommandSupport.ResolveRepositoryName(
            ManagedModuleCommandSupport.DefaultRepositoryName,
            "https://packages.example.test/nuget/v3/index.json");

        Assert.Equal("packages.example.test", repositoryName);
    }

    [Fact]
    public void NewConfigurationPublish_resolves_required_module_source_profile()
    {
        using var profileRoot = new TemporaryDirectory();
        using var profileScope = UseProfileStore(profileRoot.Path);
        new ModuleRepositoryProfileStore().SaveProfile(new ModuleRepositoryProfile
        {
            Name = "InternalUpstream",
            Provider = PrivateGalleryProvider.NuGet,
            RepositoryName = "CompanyUpstream",
            RepositoryUri = "https://packages.example.test/nuget/v3/index.json",
            RepositorySourceUri = "https://packages.example.test/nuget/v2",
            RepositoryPublishUri = "https://packages.example.test/nuget/v2"
        });

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("New-ConfigurationPublish")
            .AddParameter("Type", PublishDestination.PowerShellGallery)
            .AddParameter("ApiKey", "token")
            .AddParameter("RepositoryName", "CompanyTarget")
            .AddParameter("Tool", PublishTool.ManagedModule)
            .AddParameter("PublishRequiredModules")
            .AddParameter("RequiredModuleSourceRepository", "InternalUpstream")
            .AddParameter("Enabled");

        var segment = Assert.IsType<ConfigurationPublishSegment>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.Equal("CompanyUpstream", segment.Configuration.RequiredModuleSourceRepository);
        Assert.Equal("https://packages.example.test/nuget/v3/index.json", segment.Configuration.RequiredModuleSourceRepositoryUri);
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

    private static void CreateModule(string root, string name, string version, string? requiredModules = null)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, name + ".psm1"), "function Get-CompanyTool { 'ok' }");
        File.WriteAllText(Path.Combine(root, name + ".psd1"), $$"""
@{
    RootModule = '{{name}}.psm1'
    ModuleVersion = '{{version}}'
    Author = 'Evotec'
    Description = 'Company tools module.'
{{requiredModules}}
    PrivateData = @{
        PSData = @{
            Tags = @('company', 'automation')
        }
    }
}
""");
    }

    private static string EscapePowerShellString(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

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
