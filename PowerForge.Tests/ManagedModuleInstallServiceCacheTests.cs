using PowerForge;

namespace PowerForge.Tests;

public sealed partial class ManagedModuleInstallServiceTests
{
    [Fact]
    public async Task InstallAsync_reuses_extracted_package_cache_when_package_cache_is_persistent()
    {
        using var feed = new TemporaryDirectory();
        using var firstModuleRoot = new TemporaryDirectory();
        using var secondModuleRoot = new TemporaryDirectory();
        using var packageCache = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        var service = new ManagedModuleInstallService(new NullLogger());

        var first = await service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = "Company.Tools",
            Version = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = firstModuleRoot.Path,
            PackageCacheDirectory = packageCache.Path
        });
        var second = await service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = "Company.Tools",
            Version = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = secondModuleRoot.Path,
            PackageCacheDirectory = packageCache.Path
        });

        Assert.False(first.Download?.FromCache);
        Assert.False(first.ExtractionFromCache);
        Assert.True(first.ExtractionCacheLockWaitElapsed >= TimeSpan.Zero);
        Assert.True(second.Download?.FromCache);
        Assert.True(second.ExtractionFromCache);
        Assert.True(second.ExtractionCacheLockWaitElapsed >= TimeSpan.Zero);
        Assert.Equal(first.FileCount, second.FileCount);
        Assert.Equal(first.ExtractedBytes, second.ExtractedBytes);
        Assert.True(File.Exists(Path.Combine(secondModuleRoot.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
    }

    [Fact]
    public async Task InstallAsync_falls_back_to_package_extraction_when_extracted_cache_is_incomplete()
    {
        using var feed = new TemporaryDirectory();
        using var firstModuleRoot = new TemporaryDirectory();
        using var secondModuleRoot = new TemporaryDirectory();
        using var packageCache = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        var service = new ManagedModuleInstallService(new NullLogger());

        var first = await service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = "Company.Tools",
            Version = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = firstModuleRoot.Path,
            PackageCacheDirectory = packageCache.Path
        });
        var cachedFile = Directory.EnumerateFiles(Path.Combine(packageCache.Path, ".x"), "*", SearchOption.AllDirectories)
            .First(path => !Path.GetFileName(path).Equals("m.txt", StringComparison.OrdinalIgnoreCase) &&
                           !Path.GetExtension(path).Equals(".lock", StringComparison.OrdinalIgnoreCase));
        File.Delete(cachedFile);

        var second = await service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = "Company.Tools",
            Version = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = secondModuleRoot.Path,
            PackageCacheDirectory = packageCache.Path
        });

        Assert.True(first.FileCount > 0);
        Assert.True(second.Download?.FromCache);
        Assert.False(second.ExtractionFromCache);
        Assert.True(second.ExtractionCacheLockWaitElapsed >= TimeSpan.Zero);
        Assert.Equal(first.FileCount, second.FileCount);
        Assert.True(File.Exists(Path.Combine(secondModuleRoot.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
    }
}
