namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationProviderPackageTests
{
    [Fact]
    public async Task BuilderReplacesPackageAfterTemporaryWindowsReadLockClears()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = ProviderFixture.Create();
        var packagePath = fixture.PackagePath("replace.nupkg");
        fixture.BuildPackage("replace.nupkg");
        fixture.Manifest.PackageVersion = "1.0.1";
        using var handle = File.Open(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var build = Task.Run(() => fixture.BuildPackage("replace.nupkg"));
        try
        {
            await Task.Delay(400);
            Assert.False(build.IsCompleted, "Replacement must wait while the original package is locked.");
        }
        finally
        {
            handle.Dispose();
        }

        var result = await build;
        Assert.Equal("1.0.1", Assert.Single(result.Lock.Packages).PackageVersion);
        Assert.Empty(Directory.GetFiles(fixture.RootPath, "*.tmp"));
    }

    [Fact]
    public void BuilderPreservesPackageAndCleansCandidateWhenWindowsReadLockPersists()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = ProviderFixture.Create();
        var packagePath = fixture.PackagePath("replace.nupkg");
        fixture.BuildPackage("replace.nupkg");
        var originalHash = Hash(packagePath);
        fixture.Manifest.PackageVersion = "1.0.1";
        using var handle = File.Open(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        Assert.Throws<IOException>(() => fixture.BuildPackage("replace.nupkg"));

        Assert.Equal(originalHash, Hash(packagePath));
        Assert.Empty(Directory.GetFiles(fixture.RootPath, "*.tmp"));
    }
}
