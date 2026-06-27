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
}
