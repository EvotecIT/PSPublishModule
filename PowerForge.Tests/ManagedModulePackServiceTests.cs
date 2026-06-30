using System.IO.Compression;
using System.Xml.Linq;
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
        Assert.Contains("PSModule", metadata.Tags);
        Assert.Contains("company", metadata.Tags);
        Assert.Contains("automation", metadata.Tags);

        using var archive = ZipFile.OpenRead(result.PackagePath);
        Assert.Contains(archive.Entries, entry => entry.FullName.Equals("package/services/metadata/core-properties/Company.Tools.1.2.3.psmdcp", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("application/vnd.openxmlformats-package.core-properties+xml", ReadEntry(archive, "[Content_Types].xml"), StringComparison.Ordinal);
        Assert.Contains("metadata/core-properties", ReadEntry(archive, "_rels/.rels"), StringComparison.Ordinal);
        Assert.Contains("<tags>PSModule company automation</tags>", ReadEntry(archive, "Company.Tools.nuspec"), StringComparison.Ordinal);
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
    public void Pack_writes_required_modules_as_nuspec_dependencies()
    {
        using var moduleRoot = new TemporaryDirectory();
        using var output = new TemporaryDirectory();
        CreateModule(
            moduleRoot.Path,
            "Company.Tools",
            "1.0.0",
            prerelease: null,
            requiredModules: "    RequiredModules = @(@{ ModuleName = 'Company.Core'; RequiredVersion = '2.0.0' }, 'Loose.Dependency')");
        var service = new ManagedModulePackService();

        var result = service.Pack(new ManagedModulePackRequest
        {
            ModulePath = moduleRoot.Path,
            OutputDirectory = output.Path
        });

        using var archive = ZipFile.OpenRead(result.PackagePath);
        var nuspec = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        using var stream = nuspec.Open();
        var document = XDocument.Load(stream);
        var dependencies = document.Descendants()
            .Where(static element => element.Name.LocalName == "dependency")
            .Select(static element => new
            {
                Id = element.Attribute("id")?.Value,
                Version = element.Attribute("version")?.Value
            })
            .ToArray();

        Assert.Contains(dependencies, dependency => dependency.Id == "Company.Core" && dependency.Version == "[2.0.0]");
        Assert.Contains(dependencies, dependency => dependency.Id == "Loose.Dependency" && dependency.Version is null);
    }

    [Fact]
    public void Pack_writes_comma_separated_required_modules_as_nuspec_dependencies()
    {
        using var moduleRoot = new TemporaryDirectory();
        using var output = new TemporaryDirectory();
        CreateModule(
            moduleRoot.Path,
            "Company.Tools",
            "1.0.0",
            prerelease: null,
            requiredModules: "    RequiredModules = 'Company.Core', 'Loose.Dependency'");
        var service = new ManagedModulePackService();

        var result = service.Pack(new ManagedModulePackRequest
        {
            ModulePath = moduleRoot.Path,
            OutputDirectory = output.Path
        });

        using var archive = ZipFile.OpenRead(result.PackagePath);
        var dependencyIds = ReadNuspecDependencyIds(archive);

        Assert.Contains("Company.Core", dependencyIds);
        Assert.Contains("Loose.Dependency", dependencyIds);
    }

    [Fact]
    public void Pack_rejects_unsafe_requested_name_before_building_package_path()
    {
        using var moduleRoot = new TemporaryDirectory();
        using var output = new TemporaryDirectory();
        CreateModule(moduleRoot.Path, "Company.Tools", "1.0.0", prerelease: null);
        var service = new ManagedModulePackService();

        var exception = Assert.Throws<ArgumentException>(() => service.Pack(new ManagedModulePackRequest
        {
            ModulePath = moduleRoot.Path,
            OutputDirectory = output.Path,
            Name = "..\\Escape"
        }));

        Assert.Contains("Unsafe package id", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFiles(output.Path));
    }

    [Fact]
    public void Pack_rejects_requested_name_that_disagrees_with_manifest()
    {
        using var moduleRoot = new TemporaryDirectory();
        using var output = new TemporaryDirectory();
        CreateModule(moduleRoot.Path, "Company.Tools", "1.0.0", prerelease: null);
        var service = new ManagedModulePackService();

        var exception = Assert.Throws<InvalidOperationException>(() => service.Pack(new ManagedModulePackRequest
        {
            ModulePath = moduleRoot.Path,
            OutputDirectory = output.Path,
            Name = "Other.Tools"
        }));

        Assert.Contains("does not match module manifest", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFiles(output.Path));
    }

    [Fact]
    public void Pack_uses_top_level_module_version_when_required_modules_precede_manifest_version()
    {
        using var moduleRoot = new TemporaryDirectory();
        using var output = new TemporaryDirectory();
        Directory.CreateDirectory(moduleRoot.Path);
        File.WriteAllText(Path.Combine(moduleRoot.Path, "Company.Tools.psm1"), "function Get-CompanyTool { 'ok' }");
        File.WriteAllText(Path.Combine(moduleRoot.Path, "Company.Tools.psd1"), """
@{
    RootModule = 'Company.Tools.psm1'
    RequiredModules = @{ ModuleName = 'Company.Core'; ModuleVersion = '9.9.9' }
    ModuleVersion = '1.0.0'
    Author = 'Evotec'
    Description = 'Company tools module.'
}
""");
        var service = new ManagedModulePackService();

        var result = service.Pack(new ManagedModulePackRequest
        {
            ModulePath = moduleRoot.Path,
            OutputDirectory = output.Path
        });

        Assert.Equal("1.0.0", result.Version);
        Assert.EndsWith("Company.Tools.1.0.0.nupkg", result.PackagePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Pack_uses_manifest_name_when_root_module_points_to_binary()
    {
        using var moduleRoot = new TemporaryDirectory();
        using var output = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(moduleRoot.Path, "bin"));
        File.WriteAllText(Path.Combine(moduleRoot.Path, "bin", "Company.Tools.Engine.dll"), string.Empty);
        File.WriteAllText(Path.Combine(moduleRoot.Path, "Company.Tools.psd1"), """
@{
    RootModule = 'bin/Company.Tools.Engine.dll'
    ModuleVersion = '1.0.0'
    Author = 'Evotec'
    Description = 'Company tools module.'
}
""");
        var service = new ManagedModulePackService();

        var result = service.Pack(new ManagedModulePackRequest
        {
            ModulePath = moduleRoot.Path,
            OutputDirectory = output.Path
        });

        Assert.Equal("Company.Tools", result.Name);
        Assert.EndsWith("Company.Tools.1.0.0.nupkg", result.PackagePath, StringComparison.OrdinalIgnoreCase);
        var metadata = new ManagedModulePackageReader().ReadMetadata(result.PackagePath);
        Assert.Equal("Company.Tools", metadata.Id);
        using var archive = ZipFile.OpenRead(result.PackagePath);
        Assert.Contains(archive.Entries, entry => entry.FullName.Equals("bin/Company.Tools.Engine.dll", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Pack_excludes_output_and_managed_metadata_without_dropping_manifest_references()
    {
        using var moduleRoot = new TemporaryDirectory();
        CreateModule(moduleRoot.Path, "Company.Tools", "1.0.0", prerelease: null);
        Directory.CreateDirectory(Path.Combine(moduleRoot.Path, "bin"));
        Directory.CreateDirectory(Path.Combine(moduleRoot.Path, "obj"));
        Directory.CreateDirectory(Path.Combine(moduleRoot.Path, ".powerforge"));
        var output = Path.Combine(moduleRoot.Path, "packages");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(moduleRoot.Path, "bin", "scratch.dll"), "scratch");
        File.WriteAllText(Path.Combine(moduleRoot.Path, "obj", "scratch.txt"), "scratch");
        File.WriteAllText(Path.Combine(moduleRoot.Path, ".powerforge", "receipt.json"), "{}");
        File.WriteAllText(Path.Combine(output, "old.nupkg"), "old");
        var service = new ManagedModulePackService();

        var result = service.Pack(new ManagedModulePackRequest
        {
            ModulePath = moduleRoot.Path,
            OutputDirectory = output
        });

        using var archive = ZipFile.OpenRead(result.PackagePath);
        var names = archive.Entries.Select(static entry => entry.FullName).ToArray();
        Assert.Contains("Company.Tools.psd1", names);
        Assert.Contains("Company.Tools.psm1", names);
        Assert.DoesNotContain(names, name => name.StartsWith("bin/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.StartsWith("obj/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.StartsWith(".powerforge/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.StartsWith("packages/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Pack_preserves_manifest_license_acceptance()
    {
        using var moduleRoot = new TemporaryDirectory();
        using var output = new TemporaryDirectory();
        Directory.CreateDirectory(moduleRoot.Path);
        File.WriteAllText(Path.Combine(moduleRoot.Path, "Company.Tools.psm1"), "function Get-CompanyTool { 'ok' }");
        File.WriteAllText(Path.Combine(moduleRoot.Path, "Company.Tools.psd1"), """
@{
    RootModule = 'Company.Tools.psm1'
    ModuleVersion = '1.0.0'
    Author = 'Evotec'
    Description = 'Company tools module.'
    PrivateData = @{
        PSData = @{
            RequireLicenseAcceptance = $true
        }
    }
}
""");
        var service = new ManagedModulePackService();

        var result = service.Pack(new ManagedModulePackRequest
        {
            ModulePath = moduleRoot.Path,
            OutputDirectory = output.Path
        });

        var metadata = new ManagedModulePackageReader().ReadMetadata(result.PackagePath);
        Assert.True(metadata.RequireLicenseAcceptance);
    }

    [Fact]
    public void Pack_rejects_version_that_disagrees_with_manifest()
    {
        using var moduleRoot = new TemporaryDirectory();
        using var output = new TemporaryDirectory();
        CreateModule(moduleRoot.Path, "Company.Tools", "1.0.0", prerelease: null);
        var service = new ManagedModulePackService();

        var exception = Assert.Throws<InvalidOperationException>(() => service.Pack(new ManagedModulePackRequest
        {
            ModulePath = moduleRoot.Path,
            OutputDirectory = output.Path,
            Version = "2.0.0"
        }));

        Assert.Contains("does not match module manifest version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Pack_omits_external_module_dependencies_from_nuspec_dependencies()
    {
        using var moduleRoot = new TemporaryDirectory();
        using var output = new TemporaryDirectory();
        CreateModule(
            moduleRoot.Path,
            "Company.Tools",
            "1.0.0",
            prerelease: null,
            requiredModules: "    RequiredModules = @(@{ ModuleName = 'External.Dependency'; RequiredVersion = '2.0.0' }, @{ ModuleName = 'Company.Core'; RequiredVersion = '3.0.0' })",
            externalModuleDependencies: "            ExternalModuleDependencies = @('external.dependency')");
        var service = new ManagedModulePackService();

        var result = service.Pack(new ManagedModulePackRequest
        {
            ModulePath = moduleRoot.Path,
            OutputDirectory = output.Path
        });

        using var archive = ZipFile.OpenRead(result.PackagePath);
        var dependencyIds = ReadNuspecDependencyIds(archive);
        var metadata = new ManagedModulePackageReader().ReadMetadata(result.PackagePath);

        Assert.Contains("Company.Core", dependencyIds);
        Assert.DoesNotContain("External.Dependency", dependencyIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("External.Dependency", metadata.ManifestDependencies.Select(static dependency => dependency.Id), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("external.dependency", metadata.ManifestExternalModuleDependencies, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(metadata.Dependencies, dependency => string.Equals(dependency.Id, "External.Dependency", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Pack_requires_manifest_metadata_unless_validation_is_skipped()
    {
        using var moduleRoot = new TemporaryDirectory();
        using var output = new TemporaryDirectory();
        Directory.CreateDirectory(moduleRoot.Path);
        File.WriteAllText(Path.Combine(moduleRoot.Path, "Company.Tools.psm1"), "function Get-CompanyTool { 'ok' }");
        File.WriteAllText(Path.Combine(moduleRoot.Path, "Company.Tools.psd1"), """
@{
    RootModule = 'Company.Tools.psm1'
    ModuleVersion = '1.0.0'
}
""");
        var service = new ManagedModulePackService();

        var exception = Assert.Throws<InvalidOperationException>(() => service.Pack(new ManagedModulePackRequest
        {
            ModulePath = moduleRoot.Path,
            OutputDirectory = output.Path
        }));
        var result = service.Pack(new ManagedModulePackRequest
        {
            ModulePath = moduleRoot.Path,
            OutputDirectory = output.Path,
            SkipModuleManifestValidate = true,
            Force = true
        });

        Assert.Contains("Description", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(result.PackagePath));
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
        Assert.True(result.Elapsed > TimeSpan.Zero);
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
        Assert.True(result.Elapsed > TimeSpan.Zero);
        Assert.NotEqual("existing", File.ReadAllText(destinationPath));
    }

    [Fact]
    public async Task Publish_stages_outside_local_feed_when_output_directory_is_repository()
    {
        using var moduleRoot = new TemporaryDirectory();
        using var feed = new TemporaryDirectory();
        CreateModule(moduleRoot.Path, "Company.Tools", "1.0.0", prerelease: null);
        var service = new ManagedModulePublishService(new NullLogger());

        var result = await service.PublishAsync(new ManagedModulePublishRequest
        {
            ModulePath = moduleRoot.Path,
            Repository = new ManagedModuleRepository("Local", feed.Path),
            OutputDirectory = feed.Path
        });

        Assert.True(result.Published);
        Assert.False(result.Duplicate);
        Assert.True(File.Exists(Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg")));
        Assert.NotEqual(feed.Path, Path.GetDirectoryName(result.PackagePath));
    }

    [Fact]
    public async Task Publish_checks_required_modules_in_target_repository_by_default()
    {
        using var moduleRoot = new TemporaryDirectory();
        using var feed = new TemporaryDirectory();
        TestPackageFactory.Create(Path.Combine(feed.Path, "Company.Core.2.0.0.nupkg"), "Company.Core", "2.0.0");
        CreateModule(
            moduleRoot.Path,
            "Company.Tools",
            "1.0.0",
            prerelease: null,
            requiredModules: "    RequiredModules = @(@{ ModuleName = 'Company.Core'; RequiredVersion = '2.0.0' })");
        var service = new ManagedModulePublishService(new NullLogger());

        var result = await service.PublishAsync(new ManagedModulePublishRequest
        {
            ModulePath = moduleRoot.Path,
            Repository = new ManagedModuleRepository("Local", feed.Path)
        });

        Assert.True(result.Published);
        Assert.True(File.Exists(Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg")));
    }

    [Fact]
    public async Task Publish_can_skip_required_module_repository_check()
    {
        using var moduleRoot = new TemporaryDirectory();
        using var feed = new TemporaryDirectory();
        CreateModule(
            moduleRoot.Path,
            "Company.Tools",
            "1.0.0",
            prerelease: null,
            requiredModules: "    RequiredModules = @(@{ ModuleName = 'Company.Core'; RequiredVersion = '2.0.0' })");
        var service = new ManagedModulePublishService(new NullLogger());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.PublishAsync(new ManagedModulePublishRequest
        {
            ModulePath = moduleRoot.Path,
            Repository = new ManagedModuleRepository("Local", feed.Path)
        }));
        var result = await service.PublishAsync(new ManagedModulePublishRequest
        {
            ModulePath = moduleRoot.Path,
            Repository = new ManagedModuleRepository("Local", feed.Path),
            SkipDependenciesCheck = true,
            Force = true
        });

        Assert.Contains("Required module dependency check failed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.Published);
    }

    [Fact]
    public async Task Publish_skips_external_module_dependencies_from_manifest_repository_check()
    {
        using var moduleRoot = new TemporaryDirectory();
        using var feed = new TemporaryDirectory();
        CreateModule(
            moduleRoot.Path,
            "Company.Tools",
            "1.0.0",
            prerelease: null,
            requiredModules: "    RequiredModules = @(@{ ModuleName = 'External.Dependency'; RequiredVersion = '2.0.0' })",
            externalModuleDependencies: "            ExternalModuleDependencies = @('external.dependency')");
        var service = new ManagedModulePublishService(new NullLogger());

        var result = await service.PublishAsync(new ManagedModulePublishRequest
        {
            ModulePath = moduleRoot.Path,
            Repository = new ManagedModuleRepository("Local", feed.Path)
        });

        Assert.True(result.Published);
        Assert.True(File.Exists(Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg")));

        var metadata = new ManagedModulePackageReader().ReadMetadata(result.PackagePath);
        Assert.Contains("External.Dependency", metadata.ManifestDependencies.Select(static dependency => dependency.Id), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("external.dependency", metadata.ManifestExternalModuleDependencies, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(metadata.Dependencies, dependency => string.Equals(dependency.Id, "External.Dependency", StringComparison.OrdinalIgnoreCase));

        using var archive = ZipFile.OpenRead(result.PackagePath);
        Assert.DoesNotContain("External.Dependency", ReadNuspecDependencyIds(archive), StringComparer.OrdinalIgnoreCase);
    }

    private static void CreateModule(
        string root,
        string name,
        string version,
        string? prerelease,
        string? requiredModules = null,
        string? externalModuleDependencies = null)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, name + ".psm1"), "function Get-CompanyTool { 'ok' }");
        File.WriteAllText(Path.Combine(root, name + ".psd1"), CreateManifest(name, version, prerelease, requiredModules, externalModuleDependencies));
    }

    private static string CreateManifest(string name, string version, string? prerelease, string? requiredModules, string? externalModuleDependencies)
    {
        var prereleaseLine = string.IsNullOrWhiteSpace(prerelease)
            ? string.Empty
            : $"            Prerelease = '{prerelease}'";
        var requiredModulesLine = string.IsNullOrWhiteSpace(requiredModules)
            ? string.Empty
            : requiredModules + Environment.NewLine;
        var externalDependenciesLine = string.IsNullOrWhiteSpace(externalModuleDependencies)
            ? string.Empty
            : externalModuleDependencies + Environment.NewLine;

        return $$"""
@{
    RootModule = '{{name}}.psm1'
    ModuleVersion = '{{version}}'
    Author = 'Evotec'
    Description = 'Company tools module.'
{{requiredModulesLine}}
    PrivateData = @{
        PSData = @{
            Tags = @('company', 'automation')
{{externalDependenciesLine}}
{{prereleaseLine}}
        }
    }
}
""";
    }

    private static string ReadEntry(ZipArchive archive, string name)
    {
        var entry = archive.Entries.Single(item => item.FullName.Equals(name, StringComparison.OrdinalIgnoreCase));
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static string[] ReadNuspecDependencyIds(ZipArchive archive)
    {
        var nuspec = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        using var stream = nuspec.Open();
        var document = XDocument.Load(stream);
        return document.Descendants()
            .Where(static element => element.Name.LocalName == "dependency")
            .Select(static element => element.Attribute("id")?.Value)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id!)
            .ToArray();
    }
}
