using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleBundleMetadataWriterTests
{
    [Fact]
    public void Create_flattens_saved_modules_and_dependencies()
    {
        var dependency = new ManagedModuleInstallResult
        {
            Name = "Company.Core",
            Version = "1.0.0",
            Status = ManagedModuleInstallStatus.Installed,
            RepositoryName = "Local",
            RepositorySource = "C:\\Feed",
            ModulePath = Path.Combine("C:\\Bundle", "Company.Core", "1.0.0"),
            Download = new ManagedModuleDownloadResult
            {
                PackagePath = "C:\\Cache\\company.core.1.0.0.nupkg",
                PackageSha256 = new string('a', 64)
            }
        };
        var result = new ManagedModuleInstallResult
        {
            Name = "Company.Tools",
            Version = "2.0.0",
            Status = ManagedModuleInstallStatus.Installed,
            RepositoryName = "Local",
            RepositorySource = "C:\\Feed",
            ModulePath = Path.Combine("C:\\Bundle", "Company.Tools", "2.0.0"),
            Download = new ManagedModuleDownloadResult
            {
                PackagePath = "C:\\Cache\\company.tools.2.0.0.nupkg",
                PackageSha256 = new string('b', 64)
            },
            DependencyResults = new[] { dependency }
        };

        var metadata = new ManagedModuleBundleMetadataWriter().Create(new[] { result });

        Assert.Equal(1, metadata.SchemaVersion);
        Assert.Equal("C:\\Bundle", metadata.ModuleRoot);
        Assert.Equal(new[] { "Company.Core", "Company.Tools" }, metadata.Modules.Select(entry => entry.Name));
        Assert.Contains(metadata.Modules, entry =>
            entry.Name == "Company.Core" &&
            entry.DependencyOf == "Company.Tools" &&
            entry.PackageSha256 == new string('a', 64));
    }

    [Fact]
    public void Create_omits_package_path_when_download_cache_was_removed()
    {
        var result = new ManagedModuleInstallResult
        {
            Name = "Company.Tools",
            Version = "2.0.0",
            Status = ManagedModuleInstallStatus.Installed,
            RepositoryName = "Local",
            RepositorySource = "C:\\Feed",
            ModulePath = Path.Combine("C:\\Bundle", "Company.Tools", "2.0.0"),
            Download = new ManagedModuleDownloadResult
            {
                PackagePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "company.tools.2.0.0.nupkg"),
                PackageSha256 = new string('b', 64)
            }
        };

        var metadata = new ManagedModuleBundleMetadataWriter().Create(new[] { result });

        var entry = Assert.Single(metadata.Modules);
        Assert.Null(entry.PackagePath);
        Assert.Equal(new string('b', 64), entry.PackageSha256);
    }

    [Fact]
    public void Create_uses_package_parent_as_root_for_nupkg_saves()
    {
        var result = new ManagedModuleInstallResult
        {
            Name = "Company.Tools",
            Version = "2.0.0",
            Status = ManagedModuleInstallStatus.Installed,
            RepositoryName = "Local",
            RepositorySource = "C:\\Feed",
            ModulePath = Path.Combine("C:\\Bundle", "Company.Tools.2.0.0.nupkg"),
            SavedAsNupkg = true,
            Download = new ManagedModuleDownloadResult
            {
                PackagePath = Path.Combine("C:\\Bundle", "Company.Tools.2.0.0.nupkg"),
                PackageSha256 = new string('b', 64)
            }
        };

        var metadata = new ManagedModuleBundleMetadataWriter().Create(new[] { result });

        Assert.Equal("C:\\Bundle", metadata.ModuleRoot);
        var entry = Assert.Single(metadata.Modules);
        Assert.Equal(Path.Combine("C:\\Bundle", "Company.Tools.2.0.0.nupkg"), entry.ModulePath);
    }
}
