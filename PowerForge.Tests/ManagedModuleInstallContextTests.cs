using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleInstallContextTests
{
    [Fact]
    public void InstalledVersionCache_IsSharedAcrossBranchesAndKeepsVersionOrder()
    {
        using var moduleRoot = new TemporaryDirectory();
        var existingPath = Path.Combine(moduleRoot.Path, "Company.Core", "1.0.0");
        Directory.CreateDirectory(existingPath);
        var context = new ManagedModuleInstallContext();

        var initialVersions = context.GetInstalledVersions(moduleRoot.Path, "Company.Core");
        var branch = context.CreateBranch();
        branch.RecordInstalledVersion(moduleRoot.Path, "Company.Core", "1.2.0");
        branch.RecordInstalledVersion(moduleRoot.Path, "Company.Core", "1.1.0");
        branch.RecordInstalledVersion(moduleRoot.Path, "Company.Core", "1.2.0");

        Assert.Equal(new[] { "1.0.0" }, initialVersions);
        Assert.Equal(
            new[] { "1.0.0", "1.1.0", "1.2.0" },
            context.GetInstalledVersions(moduleRoot.Path, "Company.Core"));
    }
}
