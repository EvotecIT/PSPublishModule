using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleInstallContextTests
{
    [Fact]
    public async Task BufferedPackagePrefetch_IsNotRestartedAfterAnotherBranchTakesIt()
    {
        using var context = new ManagedModuleInstallContext();
        var branch = context.CreateBranch();
        using var package = new ManagedModuleBufferedPackage(new ManagedModuleDownloadResult(), new MemoryStream());
        var download = new TaskCompletionSource<ManagedModuleBufferedPackage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var downloads = 0;
        Task<ManagedModuleBufferedPackage> Download(CancellationToken cancellationToken)
        {
            downloads++;
            return download.Task;
        }

        context.StartBufferedPackagePrefetch("shared", Download, CancellationToken.None);
        var taken = branch.TryTakeBufferedPackagePrefetchAsync("shared", CancellationToken.None);
        context.StartBufferedPackagePrefetch("shared", Download, CancellationToken.None);
        download.SetResult(package);

        Assert.Same(package, await taken);
        Assert.Equal(1, downloads);
        Assert.Null(await context.TryTakeBufferedPackagePrefetchAsync("shared", CancellationToken.None));
    }

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
    public void EnumerateInstalledVersions_AppendsManifestPrereleaseForFlatInstall()
    {
        using var moduleRoot = new TemporaryDirectory();
        var modulePath = Path.Combine(moduleRoot.Path, "Company.Core");
        Directory.CreateDirectory(modulePath);
        File.WriteAllText(
            Path.Combine(modulePath, "Company.Core.psd1"),
            """
            @{
                ModuleVersion = '1.0.0'
                PrivateData = @{
                    PSData = @{
                        Prerelease = 'beta1'
                    }
                }
            }
            """);

        var versions = ManagedModuleInstallContext.EnumerateInstalledVersions(moduleRoot.Path, "Company.Core");

        Assert.Equal(new[] { "1.0.0-beta1" }, versions);
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
