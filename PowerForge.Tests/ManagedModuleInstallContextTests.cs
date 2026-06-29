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

    [Fact]
    public void EnumerateInstalledVersions_IgnoresManagedStageDirectories()
    {
        using var moduleRoot = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(moduleRoot.Path, "Company.Core", "1.0.0"));
        Directory.CreateDirectory(Path.Combine(moduleRoot.Path, "Company.Core", ".pfmm-stage-abcd"));

        var versions = ManagedModuleInstallContext.EnumerateInstalledVersions(moduleRoot.Path, "Company.Core");

        Assert.Equal(new[] { "1.0.0" }, versions);
    }

    [Fact]
    public async Task DependencyVersionSelectionCache_IsSharedAcrossBranchesAndRetriesFailures()
    {
        var context = new ManagedModuleInstallContext();
        var branch = context.CreateBranch();
        var attempts = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.GetOrAddDependencyVersionSelection(
            "repository|Company.Core|[1.0.0,2.0.0)",
            () =>
            {
                attempts++;
                throw new InvalidOperationException("Temporary version lookup failure.");
            }));

        var selected = await context.GetOrAddDependencyVersionSelection(
            "repository|Company.Core|[1.0.0,2.0.0)",
            () =>
            {
                attempts++;
                return Task.FromResult("1.2.0");
            });
        var branchSelected = await branch.GetOrAddDependencyVersionSelection(
            "repository|Company.Core|[1.0.0,2.0.0)",
            () =>
            {
                attempts++;
                return Task.FromResult("1.3.0");
            });

        Assert.Equal("1.2.0", selected.Version);
        Assert.False(selected.Shared);
        Assert.Equal("1.2.0", branchSelected.Version);
        Assert.True(branchSelected.Shared);
        Assert.Equal(2, attempts);
    }
}
