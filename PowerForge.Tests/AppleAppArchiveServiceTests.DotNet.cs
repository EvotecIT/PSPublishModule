namespace PowerForge.Tests;

public sealed partial class AppleAppArchiveServiceTests
{
    [Fact]
    public async Task DotNetArchiveRejectsMobileAndSwiftSnapshotBeforeStartingProcesses()
    {
        var runner = new CapturingProcessRunner();
        var service = new AppleAppArchiveService(runner);
        var request = new AppleAppArchiveRequest {
            ProjectPath = "missing-publish-config.json", Scheme = "App", DotNetPublishInstallerId = "store", Platform = ApplePlatform.iOS
        };
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateArchiveAsync(request));
        request.Platform = ApplePlatform.macOS;
        request.RequireExactPackageSnapshot = true;
        await Assert.ThrowsAsync<NotSupportedException>(() => service.CreateArchiveAsync(request));
        Assert.Empty(runner.Requests);
    }
}
