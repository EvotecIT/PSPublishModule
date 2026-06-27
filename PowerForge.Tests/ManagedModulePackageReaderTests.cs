using System.IO.Compression;
using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModulePackageReaderTests
{
    [Fact]
    public void ReadMetadata_reads_nuspec_metadata_and_dependencies()
    {
        using var temp = new TemporaryDirectory();
        var packagePath = Path.Combine(temp.Path, "Company.Tools.1.2.0.nupkg");
        TestPackageFactory.Create(
            packagePath,
            "Company.Tools",
            "1.2.0",
            dependencies: new[]
            {
                new TestDependency("Company.Core", "[1.0.0,2.0.0)", "net472"),
                new TestDependency("Company.Shared", "1.5.0", null)
            });

        var metadata = new ManagedModulePackageReader().ReadMetadata(packagePath);

        Assert.Equal("Company.Tools", metadata.Id);
        Assert.Equal("1.2.0", metadata.Version);
        Assert.Equal("expression:MIT", metadata.License);
        Assert.Equal(new[] { "automation", "company", "powershell" }, metadata.Tags);
        Assert.True(metadata.FileCount >= 1);
        Assert.True(metadata.PackageBytes > 0);
        Assert.True(metadata.UncompressedBytes > 0);
        Assert.Equal(2, metadata.Dependencies.Count);
        Assert.Contains(metadata.Dependencies, dependency =>
            dependency.Id == "Company.Core" &&
            dependency.VersionRange == "[1.0.0,2.0.0)" &&
            dependency.TargetFramework == "net472");
        Assert.Contains(metadata.Dependencies, dependency =>
            dependency.Id == "Company.Shared" &&
            dependency.VersionRange == "1.5.0" &&
            dependency.TargetFramework is null);
    }

    [Fact]
    public void ReadMetadata_marks_prerelease_versions()
    {
        using var temp = new TemporaryDirectory();
        var packagePath = Path.Combine(temp.Path, "Company.Tools.2.0.0-beta1.nupkg");
        TestPackageFactory.Create(packagePath, "Company.Tools", "2.0.0-beta1");

        var metadata = new ManagedModulePackageReader().ReadMetadata(packagePath);

        Assert.True(metadata.IsPrerelease);
    }

    [Fact]
    public void ReadMetadata_reads_module_manifest_metadata_and_required_modules()
    {
        using var temp = new TemporaryDirectory();
        var packagePath = Path.Combine(temp.Path, "Company.Tools.1.2.0-preview1.nupkg");
        TestPackageFactory.Create(
            packagePath,
            "Company.Tools",
            "1.2.0-preview1",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Company.Tools.psd1"] = """
                    @{
                        ModuleVersion = '1.2.0'
                        RequiredModules = @(
                            @{ ModuleName = 'Company.Core'; RequiredVersion = '1.0.0' },
                            @{ ModuleName = 'Company.Shared'; ModuleVersion = '2.0.0'; MaximumVersion = '2.9.9' }
                        )
                        PrivateData = @{
                            PSData = @{
                                Prerelease = 'preview1'
                            }
                        }
                    }
                    """
            });

        var metadata = new ManagedModulePackageReader().ReadMetadata(packagePath);

        Assert.Equal("Company.Tools.psd1", metadata.ModuleManifestPath);
        Assert.Equal("1.2.0", metadata.ModuleManifestVersion);
        Assert.Equal("preview1", metadata.ModuleManifestPrerelease);
        Assert.Equal(2, metadata.ManifestDependencies.Count);
        Assert.Contains(metadata.Dependencies, dependency =>
            dependency.Id == "Company.Core" &&
            dependency.VersionRange == "[1.0.0]");
        Assert.Contains(metadata.Dependencies, dependency =>
            dependency.Id == "Company.Shared" &&
            dependency.VersionRange == "[2.0.0,2.9.9]");
    }

    [Fact]
    public void ReadMetadata_rejects_manifest_name_that_disagrees_with_nuspec_id()
    {
        using var temp = new TemporaryDirectory();
        var packagePath = Path.Combine(temp.Path, "Company.Tools.1.0.0.nupkg");
        TestPackageFactory.Create(
            packagePath,
            "Company.Tools",
            "1.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Other.Tools.psd1"] = """
                    @{
                        ModuleVersion = '1.0.0'
                    }
                    """
            });

        var ex = Assert.Throws<InvalidOperationException>(() => new ManagedModulePackageReader().ReadMetadata(packagePath));
        Assert.Contains("does not match module manifest", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Company.Tools", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Other.Tools.psd1", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadMetadata_rejects_manifest_version_that_disagrees_with_nuspec_version()
    {
        using var temp = new TemporaryDirectory();
        var packagePath = Path.Combine(temp.Path, "Company.Tools.1.0.0.nupkg");
        TestPackageFactory.Create(
            packagePath,
            "Company.Tools",
            "1.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Company.Tools.psd1"] = """
                    @{
                        ModuleVersion = '2.0.0'
                    }
                    """
            });

        var ex = Assert.Throws<InvalidOperationException>(() => new ManagedModulePackageReader().ReadMetadata(packagePath));
        Assert.Contains("does not match module manifest version", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1.0.0", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2.0.0", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadMetadata_accepts_semantically_equivalent_manifest_version()
    {
        using var temp = new TemporaryDirectory();
        var packagePath = Path.Combine(temp.Path, "Company.Tools.1.2.nupkg");
        TestPackageFactory.Create(
            packagePath,
            "Company.Tools",
            "1.2",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Company.Tools.psd1"] = """
                    @{
                        ModuleVersion = '1.2.0'
                    }
                    """
            });

        var metadata = new ManagedModulePackageReader().ReadMetadata(packagePath);

        Assert.Equal("1.2", metadata.Version);
        Assert.Equal("1.2.0", metadata.ModuleManifestVersion);
    }

    [Fact]
    public void ReadMetadata_rejects_unsafe_archive_paths()
    {
        using var temp = new TemporaryDirectory();
        var packagePath = Path.Combine(temp.Path, "Company.Tools.1.0.0.nupkg");
        using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
        {
            var nuspec = archive.CreateEntry("Company.Tools.nuspec");
            using (var writer = new StreamWriter(nuspec.Open()))
            {
                writer.Write(TestPackageFactory.CreateNuspec("Company.Tools", "1.0.0"));
            }

            archive.CreateEntry("../escape.ps1");
        }

        var ex = Assert.Throws<InvalidOperationException>(() => new ManagedModulePackageReader().ReadMetadata(packagePath));
        Assert.Contains("unsafe path", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
