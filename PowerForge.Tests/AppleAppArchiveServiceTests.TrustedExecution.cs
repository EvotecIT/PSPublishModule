namespace PowerForge.Tests;

public sealed partial class AppleAppArchiveServiceTests
{
    [Fact]
    public async Task UploadArchiveAsync_exact_source_uses_system_xcodebuild_without_parent_environment()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var archive = Directory.CreateDirectory(Path.Combine(root.FullName, "App.xcarchive"));
            var runner = new CapturingProcessRunner();

            var result = await new AppleAppArchiveService(runner).UploadArchiveAsync(new AppleAppArchiveUploadRequest
            {
                ArchivePath = archive.FullName,
                ExportPath = Path.Combine(root.FullName, "export"),
                XcodeBuildExecutable = "/usr/bin/xcodebuild",
                RequireTrustedSystemTools = true
            });

            Assert.True(result.Succeeded);
            var request = Assert.Single(runner.Requests);
            Assert.Equal("/usr/bin/xcodebuild", request.FileName);
            Assert.False(request.InheritEnvironment);
            Assert.Equal("/usr/bin:/bin:/usr/sbin:/sbin", request.EnvironmentVariables?["PATH"]);
            Assert.False(request.EnvironmentVariables?.ContainsKey("DEVELOPER_DIR"));
            Assert.False(request.EnvironmentVariables?.ContainsKey("TOOLCHAINS"));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task UploadArchiveAsync_exact_source_rejects_custom_xcodebuild()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var archive = Directory.CreateDirectory(Path.Combine(root.FullName, "App.xcarchive"));
            var runner = new CapturingProcessRunner();

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleAppArchiveService(runner).UploadArchiveAsync(new AppleAppArchiveUploadRequest
                {
                    ArchivePath = archive.FullName,
                    ExportPath = Path.Combine(root.FullName, "export"),
                    XcodeBuildExecutable = "/tmp/xcodebuild",
                    RequireTrustedSystemTools = true
                }));

            Assert.Contains("system Xcode build tool", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(runner.Requests);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }
}
