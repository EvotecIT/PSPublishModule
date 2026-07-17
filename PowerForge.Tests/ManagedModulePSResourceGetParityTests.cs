using System.Management.Automation;
using PSPublishModule;

namespace PowerForge.Tests;

[Collection("ModuleRepositoryProfileEnvironment")]
public sealed class ManagedModulePSResourceGetParityTests
{
    [Theory]
    [InlineData("1.2.0", "1.2.0", true)]
    [InlineData("1.2.1", "1.2.0", false)]
    [InlineData("1.9.0", "1.*", true)]
    [InlineData("2.0.0", "1.*", false)]
    [InlineData("1.5.0", "[1.2.0,2.0.0)", true)]
    [InlineData("2.0.0", "[1.2.0,2.0.0)", false)]
    [InlineData("1.2.0-preview.1", "1.2.0-preview.1", true)]
    [InlineData("1.5.0", ">=1.0.0 <2.0.0", true)]
    [InlineData("1.5.0", "<2.0.0 >=1.0.0", true)]
    [InlineData("1.5.0", ">=2.0.0 >=1.0.0 <3.0.0", false)]
    [InlineData("2.5.0", ">=2.0.0 >=1.0.0 <3.0.0", true)]
    [InlineData("2.5.0", ">=1.0.0 <2.0.0 <3.0.0", false)]
    [InlineData("2.0.0", ">=2.0.0 >2.0.0 <3.0.0", false)]
    public void VersionSelector_matches_PSResourceGet_version_expressions(
        string version,
        string expression,
        bool expected)
        => Assert.Equal(expected, ManagedModuleVersionSelector.IsMatch(version, expression));

    [Theory]
    [InlineData("foo")]
    [InlineData("1.*.0")]
    [InlineData("[1.0.0,foo)")]
    [InlineData("[1.0.0,2.0.0")]
    [InlineData("1.0.0,2.0.0")]
    [InlineData("[2.0.0,1.0.0]")]
    [InlineData(">=2.0.0 <1.0.0")]
    [InlineData("<1.0.0 >=2.0.0")]
    [InlineData(">=1.0.0 <1.0.0")]
    [InlineData("(1.0.0,1.0.0]")]
    public void VersionSelector_rejects_invalid_version_expressions(string expression)
        => Assert.Throws<ArgumentException>(() => ManagedModuleVersionSelector.IsMatch("1.0.0", expression));

    [Theory]
    [InlineData(null, false)]
    [InlineData("1.*", false)]
    [InlineData("[1.0.0,2.0.0)", false)]
    [InlineData("1.2.0", true)]
    [InlineData("[1.2.0]", true)]
    public void VersionSelector_allows_unlisted_versions_only_for_exact_pins(string? expression, bool expected)
    {
        var version = new ManagedModuleVersionInfo
        {
            Name = "Company.Tools",
            Version = "1.2.0",
            Listed = false
        };

        Assert.Equal(expected, ManagedModuleVersionSelector.IsSelectable(version, expression));
    }

    [Fact]
    public void VersionSelector_rejects_invalid_repository_version_metadata()
    {
        var version = new ManagedModuleVersionInfo
        {
            Name = "Company.Tools",
            Version = "invalid",
            Listed = true
        };

        Assert.Throws<ArgumentException>(() => ManagedModuleVersionSelector.IsSelectable(version, "0.0.0"));
    }

    [Fact]
    public void FindManagedModule_version_range_returns_every_matching_module_version()
    {
        using var feed = new TemporaryDirectory();
        CreatePackage(feed.Path, "Company.Tools", "1.0.0");
        CreatePackage(feed.Path, "Company.Tools", "1.5.0");
        CreatePackage(feed.Path, "Company.Tools", "2.0.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Find-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("Version", "[1.0.0,2.0.0)");

        var results = ps.Invoke()
            .Select(static item => Assert.IsType<ManagedModuleVersionInfo>(item.BaseObject))
            .OrderBy(static item => item.Version, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        AssertNoPowerShellErrors(ps);
        Assert.Equal(new[] { "1.0.0", "1.5.0" }, results.Select(static item => item.Version));
    }

    [Fact]
    public void FindManagedModule_exact_prerelease_version_does_not_require_a_second_switch()
    {
        using var feed = new TemporaryDirectory();
        CreatePackage(feed.Path, "Company.Tools", "1.0.0-preview.1");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Find-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("Version", "1.0.0-preview.1");

        var result = Assert.IsType<ManagedModuleVersionInfo>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.Equal("1.0.0-preview.1", result.Version);
    }

    [Fact]
    public void FindManagedModule_applies_first_after_wildcard_version_filtering()
    {
        using var feed = new TemporaryDirectory();
        CreatePackage(feed.Path, "Company.Alpha", "2.0.0");
        CreatePackage(feed.Path, "Company.Beta", "1.0.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Find-ManagedModule")
            .AddParameter("Name", "Company.*")
            .AddParameter("Repository", feed.Path)
            .AddParameter("Version", "1.0.0")
            .AddParameter("First", 1);

        var result = Assert.IsType<ManagedModuleVersionInfo>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.Equal("Company.Beta", result.Name);
        Assert.Equal("1.0.0", result.Version);
    }

    [Fact]
    public void FindManagedModule_output_pipes_to_exact_install()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        CreatePackage(feed.Path, "Company.Tools", "1.0.0");
        CreatePackage(feed.Path, "Company.Tools", "2.0.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Find-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("Version", "1.0.0")
            .AddCommand("Install-ManagedModule")
            .AddParameter("Scope", ManagedModuleInstallScope.Custom)
            .AddParameter("ModuleRoot", moduleRoot.Path);

        var result = Assert.IsType<ManagedModuleInstallResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.Equal("1.0.0", result.Version);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "2.0.0")));
    }

    [Fact]
    public void FindManagedModule_output_pipes_to_save_with_PowerShellGet_metadata_in_current_location()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Dependency.2.0.0.nupkg"),
            "Company.Dependency",
            "2.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Company.Dependency.psd1"] = "@{ RootModule = 'Company.Dependency.psm1'; ModuleVersion = '2.0.0'; FunctionsToExport = @('Get-CompanyDependency') }",
                ["Company.Dependency.psm1"] = "function Get-CompanyDependency { 'ok' }"
            });
        CreatePackage(
            feed.Path,
            "Company.Tools",
            "1.0.0",
            new[] { new TestDependency("Company.Dependency", "[2.0.0,3.0.0)", targetFramework: null) });

        using var ps = CreatePowerShellWithModuleImported();
        SetLocation(ps, destination.Path);
        ps.AddCommand("Find-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("Version", "1.0.0")
            .AddCommand("Save-ManagedModule")
            .AddParameter("IncludeXml");

        var result = Assert.IsType<ManagedModuleInstallResult>(Assert.Single(ps.Invoke()).BaseObject);
        var metadataPath = Path.Combine(result.ModulePath, "PSGetModuleInfo.xml");

        AssertNoPowerShellErrors(ps);
        Assert.Equal(destination.Path, result.ModuleRoot);
        Assert.True(File.Exists(metadataPath));
        var metadata = Assert.IsType<PSObject>(PSSerializer.Deserialize(File.ReadAllText(metadataPath)));
        Assert.Equal("Company.Tools", metadata.Properties["Name"].Value);
        Assert.Equal("1.0.0", metadata.Properties["Version"].Value);
        Assert.Equal(1, metadata.Properties["Type"].Value);
        Assert.Equal("Company", metadata.Properties["CompanyName"].Value);
        Assert.Contains("Get-CompanyTool", File.ReadAllText(metadataPath), StringComparison.Ordinal);
        Assert.Equal(result.RepositoryName, metadata.Properties["Repository"].Value);
        Assert.Equal(result.RepositorySource, metadata.Properties["RepositorySourceLocation"].Value);
        Assert.Equal(result.ModulePath, metadata.Properties["InstalledLocation"].Value);
        var publishedDate = Assert.IsType<DateTime>(metadata.Properties["PublishedDate"].Value);
        var installedDate = Assert.IsType<DateTime>(metadata.Properties["InstalledDate"].Value);
        Assert.Equal(installedDate, publishedDate);
        var serializedDependencies = metadata.Properties["Dependencies"].Value;
        if (serializedDependencies is PSObject dependencyCollection)
            serializedDependencies = dependencyCollection.BaseObject;
        var dependencies = Assert.IsAssignableFrom<System.Collections.IEnumerable>(serializedDependencies)
            .Cast<object>()
            .ToArray();
        var dependency = PSObject.AsPSObject(Assert.Single(dependencies));
        Assert.Equal("Company.Dependency", dependency.Properties["Name"].Value);
        Assert.Equal("[2.0.0,3.0.0)", dependency.Properties["VersionRange"].Value);
        Assert.Null(dependency.Properties["Version"]);

        var dependencyResult = Assert.Single(result.DependencyResults);
        var dependencyMetadataPath = Path.Combine(dependencyResult.ModulePath, "PSGetModuleInfo.xml");
        var dependencyMetadata = Assert.IsType<PSObject>(PSSerializer.Deserialize(File.ReadAllText(dependencyMetadataPath)));
        Assert.Equal(dependencyResult.ModulePath, dependencyMetadata.Properties["InstalledLocation"].Value);
    }

    [Fact]
    public void GetManagedModule_output_pipes_to_exact_uninstall()
    {
        using var moduleRoot = new TemporaryDirectory();
        var modulePath = Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0");
        Directory.CreateDirectory(modulePath);
        File.WriteAllText(
            Path.Combine(modulePath, "Company.Tools.psd1"),
            "@{ RootModule = 'Company.Tools.psm1'; ModuleVersion = '1.0.0' }");
        File.WriteAllText(Path.Combine(modulePath, "Company.Tools.psm1"), "function Get-CompanyTool { 'ok' }");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Get-ManagedModule")
            .AddParameter("ModulePath", new[] { moduleRoot.Path })
            .AddParameter("Name", "Company.Tools")
            .AddCommand("Uninstall-ManagedModule")
            .AddParameter("Confirm", false);

        var result = Assert.IsType<ManagedModuleUninstallResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.Equal("Company.Tools", result.Name);
        Assert.Equal("1.0.0", result.Version);
        Assert.False(Directory.Exists(modulePath));
    }

    [Fact]
    public void UninstallManagedModule_rejects_incomplete_installed_input()
    {
        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Uninstall-ManagedModule")
            .AddParameter("InputObject", new[]
            {
                new ModuleStateInstalledModuleResult
                {
                    Name = "Company.Tools",
                    Version = "1.0.0"
                }
            })
            .AddParameter("Plan");

        var exception = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());

        Assert.Contains("InstalledLocation", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Install-ManagedModule")]
    [InlineData("Save-ManagedModule")]
    public void FindManagedModule_pipeline_preserves_managed_profile_identity_and_trust(string targetCommand)
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        using var profileRoot = new TemporaryDirectory();
        using var profileScope = UseProfileStore(profileRoot.Path);
        CreatePackage(feed.Path, "Company.Tools", "1.0.0");
        new ModuleRepositoryProfileStore().SaveProfile(new ModuleRepositoryProfile
        {
            Name = "CompanyProfile",
            Provider = PrivateGalleryProvider.NuGet,
            RepositoryName = "CompanyModules",
            RepositoryUri = feed.Path,
            Trusted = true
        });

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Find-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Version", "1.0.0")
            .AddParameter("ProfileName", "CompanyProfile")
            .AddCommand(targetCommand)
            .AddParameter("RequireTrustedRepository");
        if (string.Equals(targetCommand, "Install-ManagedModule", StringComparison.Ordinal))
        {
            ps.AddParameter("Scope", ManagedModuleInstallScope.Custom)
                .AddParameter("ModuleRoot", destination.Path);
        }
        else
        {
            ps.AddParameter("Path", destination.Path);
        }

        var result = Assert.IsType<ManagedModuleInstallResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.Equal("CompanyModules", result.RepositoryName);
        Assert.Equal(feed.Path, result.RepositorySource);
    }

    [Fact]
    public void PublishManagedModule_publishes_existing_nupkg_without_repacking()
    {
        using var packages = new TemporaryDirectory();
        using var repository = new TemporaryDirectory();
        var packagePath = Path.Combine(packages.Path, "Company.Tools.1.0.0.nupkg");
        TestPackageFactory.Create(packagePath, "Company.Tools", "1.0.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Publish-ManagedModule")
            .AddParameter("NupkgPath", packagePath)
            .AddParameter("Repository", repository.Path)
            .AddParameter("SkipDependenciesCheck");

        var result = Assert.IsType<ManagedModulePublishResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.True(result.Published);
        Assert.Equal("Company.Tools", result.Name);
        Assert.Equal("1.0.0", result.Version);
        Assert.True(File.Exists(Path.Combine(repository.Path, Path.GetFileName(packagePath))));
    }

    [Fact]
    public void PublishManagedModule_uses_output_directory_as_the_local_target_for_existing_nupkg()
    {
        using var packages = new TemporaryDirectory();
        using var output = new TemporaryDirectory();
        var packagePath = Path.Combine(packages.Path, "Company.Tools.1.0.0.nupkg");
        TestPackageFactory.Create(packagePath, "Company.Tools", "1.0.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Publish-ManagedModule")
            .AddParameter("NupkgPath", packagePath)
            .AddParameter("OutputDirectory", output.Path)
            .AddParameter("SkipDependenciesCheck");

        var result = Assert.IsType<ManagedModulePublishResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.True(result.Published);
        Assert.False(result.Duplicate);
        Assert.True(File.Exists(Path.Combine(output.Path, Path.GetFileName(packagePath))));
    }

    [Fact]
    public void PublishManagedModule_validates_existing_nupkg_before_replacing_output_copy()
    {
        using var packages = new TemporaryDirectory();
        using var output = new TemporaryDirectory();
        using var repository = new TemporaryDirectory();
        var packageName = "Company.Tools.1.0.0.nupkg";
        var packagePath = Path.Combine(packages.Path, packageName);
        var outputPath = Path.Combine(output.Path, packageName);
        File.WriteAllText(packagePath, "not a NuGet package");
        TestPackageFactory.Create(outputPath, "Company.Tools", "1.0.0");
        var originalOutput = File.ReadAllBytes(outputPath);

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Publish-ManagedModule")
            .AddParameter("NupkgPath", packagePath)
            .AddParameter("Repository", repository.Path)
            .AddParameter("OutputDirectory", output.Path)
            .AddParameter("SkipDependenciesCheck")
            .AddParameter("Force");

        Assert.Throws<CmdletInvocationException>(() => ps.Invoke());
        Assert.Equal(originalOutput, File.ReadAllBytes(outputPath));
        Assert.False(File.Exists(Path.Combine(repository.Path, packageName)));
    }

    [Theory]
    [InlineData("Install-ManagedModule")]
    [InlineData("Save-ManagedModule")]
    public void Expected_package_hash_rejects_multiple_pipeline_targets_before_writes(string targetCommand)
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        CreatePackage(feed.Path, "Company.One", "1.0.0");
        CreatePackage(feed.Path, "Company.Two", "1.0.0");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Find-ManagedModule")
            .AddParameter("Name", new[] { "Company.One", "Company.Two" })
            .AddParameter("Repository", feed.Path)
            .AddCommand(targetCommand)
            .AddParameter("ExpectedPackageSha256", new string('a', 64));
        if (string.Equals(targetCommand, "Install-ManagedModule", StringComparison.Ordinal))
        {
            ps.AddParameter("Scope", ManagedModuleInstallScope.Custom)
                .AddParameter("ModuleRoot", destination.Path);
        }
        else
        {
            ps.AddParameter("Path", destination.Path);
        }

        var exception = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());

        Assert.Contains("exactly one module", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(destination.Path));
    }

    [Theory]
    [InlineData("Find-ManagedModule", "Version")]
    [InlineData("Install-ManagedModule", "InputObject")]
    [InlineData("Save-ManagedModule", "InputObject")]
    [InlineData("Save-ManagedModule", "IncludeXml")]
    [InlineData("Uninstall-ManagedModule", "InputObject")]
    [InlineData("Publish-ManagedModule", "NupkgPath")]
    public void Managed_module_cmdlets_expose_PSResourceGet_module_parameters(string commandName, string parameterName)
    {
        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Get-Command").AddArgument(commandName);

        var command = Assert.IsType<CmdletInfo>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.True(command.Parameters.ContainsKey(parameterName));
    }

    [Fact]
    public void Existing_package_publish_parameter_set_does_not_accept_ignored_pack_overrides()
    {
        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Get-Command").AddArgument("Publish-ManagedModule");

        var command = Assert.IsType<CmdletInfo>(Assert.Single(ps.Invoke()).BaseObject);
        var parameterSet = Assert.Single(command.ParameterSets, static set => set.Name == "NupkgPathParameterSet");
        var parameterNames = parameterSet.Parameters.Select(static parameter => parameter.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        AssertNoPowerShellErrors(ps);
        Assert.DoesNotContain("ManifestPath", parameterNames);
        Assert.DoesNotContain("Name", parameterNames);
        Assert.DoesNotContain("Version", parameterNames);
        Assert.DoesNotContain("SkipModuleManifestValidate", parameterNames);
    }

    [Fact]
    public void Installed_object_uninstall_parameter_set_does_not_accept_ignored_selection_overrides()
    {
        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Get-Command").AddArgument("Uninstall-ManagedModule");

        var command = Assert.IsType<CmdletInfo>(Assert.Single(ps.Invoke()).BaseObject);
        var parameterSet = Assert.Single(command.ParameterSets, static set => set.Name == "InputObjectParameterSet");
        var parameterNames = parameterSet.Parameters.Select(static parameter => parameter.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        AssertNoPowerShellErrors(ps);
        Assert.DoesNotContain("Version", parameterNames);
        Assert.DoesNotContain("Prerelease", parameterNames);
        Assert.DoesNotContain("Scope", parameterNames);
        Assert.DoesNotContain("ModuleRoot", parameterNames);
    }

    private static void CreatePackage(
        string repositoryPath,
        string name,
        string version,
        IReadOnlyList<TestDependency>? dependencies = null)
        => TestPackageFactory.Create(
            Path.Combine(repositoryPath, name + "." + version + ".nupkg"),
            name,
            version,
            dependencies: dependencies,
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [name + ".psd1"] = "@{ RootModule = '" + name + ".psm1'; ModuleVersion = '" + version + "'; Author = 'Company'; CompanyName = 'Company'; Description = 'Company tools'; FunctionsToExport = @('Get-CompanyTool') }",
                [name + ".psm1"] = "function Get-CompanyTool { 'ok' }"
            });

    private static IDisposable UseProfileStore(string root)
        => new TestEnvironmentVariable(
            "POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH",
            Path.Combine(root, "profiles.json"));

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

    private static void SetLocation(PowerShell ps, string path)
    {
        ps.AddCommand("Set-Location").AddParameter("LiteralPath", path);
        _ = ps.Invoke();
        AssertNoPowerShellErrors(ps);
        ps.Commands.Clear();
    }

    private static void AssertNoPowerShellErrors(PowerShell ps)
    {
        if (ps.HadErrors)
            throw new InvalidOperationException(string.Join(Environment.NewLine, ps.Streams.Error.Select(static error => error.ToString())));
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
