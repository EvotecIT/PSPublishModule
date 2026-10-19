using System.Runtime.InteropServices;

namespace PowerForge.Tests;

public sealed partial class AppleAppArchiveServiceTests
{
    [Theory]
    [InlineData("unchanged")]
    [InlineData("new-clone")]
    [InlineData("replacement-clone")]
    [InlineData("clone-and-write-restore")]
    public async Task CreateArchiveAsync_exact_source_binds_checkout_binary_clone_sources(string scenario)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "App.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            RunGit(root.FullName, "init", "--quiet");
            var runner = new ExactPackageProcessRunner(root.FullName, checkoutCloneScenario: scenario);
            WritePackageLock(root.FullName, runner.RemoteUrl, runner.ApprovedRevision);
            CommitApprovedInputs(root.FullName);
            var service = new AppleAppArchiveService(runner);
            var request = new AppleAppArchiveRequest
            {
                ProjectPath = project.FullName,
                Scheme = "App",
                ArchivePath = Path.Combine(root.FullName, "App.xcarchive"),
                RequireExactPackageSnapshot = true
            };

            if (scenario == "unchanged")
            {
                var result = await service.CreateArchiveAsync(request);
                Assert.True(result.Succeeded);
                Assert.Equal(64, result.ArchiveSha256?.Length);
            }
            else
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateArchiveAsync(request));
            }
            Assert.False(Directory.Exists(runner.SourcePackagesRoot));
        }
        finally { root.Delete(recursive: true); }
    }

    [DllImport("libc", EntryPoint = "clonefile", SetLastError = true)]
    private static extern int ClonePackageFile(string source, string destination, uint flags);
}
