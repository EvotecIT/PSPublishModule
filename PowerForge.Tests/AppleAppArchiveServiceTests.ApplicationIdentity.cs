namespace PowerForge.Tests;

public sealed partial class AppleAppArchiveServiceTests
{
    [Theory]
    [InlineData(false, "1.0", "2", true)]
    [InlineData(true, "1.0", "2", true)]
    [InlineData(false, "0.9", "2", false)]
    [InlineData(true, "1.0", "1", false)]
    [InlineData(false, null, "2", false)]
    [InlineData(true, "1.0", null, false)]
    public async Task UploadArchiveAsync_requires_planned_identity_before_export_or_remote_start(
        bool macBundleLayout, string? actualVersion, string? actualBuild, bool matches)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var archive = Directory.CreateDirectory(Path.Combine(root.FullName, "Studio.xcarchive"));
            var app = Directory.CreateDirectory(Path.Combine(archive.FullName, "Products", "Applications", "Studio.app"));
            var bundleRoot = macBundleLayout ? Directory.CreateDirectory(Path.Combine(app.FullName, "Contents")) : app;
            File.WriteAllText(Path.Combine(bundleRoot.FullName, "Info.plist"), "fixture");
            var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["CFBundleIdentifier"] = "com.example.studio" };
            if (actualVersion is not null) values["CFBundleShortVersionString"] = actualVersion;
            if (actualBuild is not null) values["CFBundleVersion"] = actualBuild;
            var runner = new PrivacyProcessRunner(values);
            var remoteStarted = false;
            var request = new AppleAppArchiveUploadRequest
            {
                ArchivePath = archive.FullName,
                ExportPath = Path.Combine(root.FullName, "export"),
                BundleId = "com.example.studio",
                MarketingVersion = "1.0",
                BuildNumber = "2",
                RemoteMutationStarted = () => remoteStarted = true
            };
            var service = new AppleAppArchiveService(runner);
            if (matches)
            {
                Assert.True((await service.UploadArchiveAsync(request)).Succeeded);
                Assert.EndsWith("xcodebuild", runner.Requests[^1].FileName, StringComparison.Ordinal);
            }
            else
            {
                var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UploadArchiveAsync(request));
                Assert.Contains("does not match planned value", exception.Message, StringComparison.Ordinal);
                Assert.DoesNotContain(runner.Requests, item => item.FileName.EndsWith("xcodebuild", StringComparison.Ordinal));
                Assert.False(Directory.Exists(request.ExportPath));
                Assert.False(remoteStarted);
            }
        }
        finally { root.Delete(recursive: true); }
    }

    [Fact]
    public async Task UploadArchiveAsync_rejects_Xcode_version_rewriting_with_a_planned_identity()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var runner = new PrivacyProcessRunner(new Dictionary<string, string>());
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleAppArchiveService(runner).UploadArchiveAsync(new AppleAppArchiveUploadRequest
                {
                    ArchivePath = root.FullName,
                    MarketingVersion = "1.0",
                    BuildNumber = "2",
                    ManageAppVersionAndBuildNumber = true
                }));
            Assert.Contains("ManageAppVersionAndBuildNumber=false", exception.Message, StringComparison.Ordinal);
            Assert.Empty(runner.Requests);
        }
        finally { root.Delete(recursive: true); }
    }
}
