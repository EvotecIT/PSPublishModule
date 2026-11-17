using System.Text.Json;
using Xunit;

namespace PowerForge.Tests;

public sealed class AppleMacAppBundleDeploymentTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeployBundle_VerifiesTheMappedExecutableInode(bool staleInode)
    {
        using var fixture = new BundleFixture();
        var runner = fixture.CreateRunner(staleInode);
        var service = new AppleMacAppDeploymentService(runner);
        var request = new AppleMacAppDeploymentRequest { InstallRoot = fixture.InstallRoot };
        var approved = AppleMacAppDeploymentService.GetBundleSha256(fixture.Source);
        if (staleInode)
        {
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeployBundleAsync(fixture.Source, approved, request));
            Assert.Contains("different executable inode", failure.Message, StringComparison.Ordinal);
        }
        else
        {
            var result = await service.DeployBundleAsync(fixture.Source, approved, request);
            Assert.True(result.Succeeded);
            Assert.Equal(2468, result.ProcessId);
            Assert.Equal(fixture.InstalledExecutable, result.ExecutablePath);
            Assert.Equal("123:456", result.ExecutableFileIdentity);
            Assert.Equal("new executable", File.ReadAllText(fixture.InstalledExecutable));
            Assert.Equal(64, result.ExecutableSha256!.Length);
        }
        var register = fixture.Commands.FindIndex(command => command.EndsWith("/lsregister", StringComparison.Ordinal));
        var launch = fixture.Commands.FindIndex(command => command == "/usr/bin/open");
        Assert.True(register >= 0 && register < launch);
    }

    [Fact]
    public async Task DeployBundle_RejectsChangedApprovedInputBeforeInstallation()
    {
        using var fixture = new BundleFixture();
        var approved = AppleMacAppDeploymentService.GetBundleSha256(fixture.Source);
        File.WriteAllText(Path.Combine(fixture.Source, "Contents", "MacOS", "Fixture"), "unapproved replacement");
        var service = new AppleMacAppDeploymentService(fixture.CreateRunner(false));
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeployBundleAsync(
            fixture.Source, approved, new AppleMacAppDeploymentRequest { InstallRoot = fixture.InstallRoot }));
        Assert.Contains("differs from the approved", failure.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(fixture.InstallRoot, "Fixture.app")));
        Assert.Empty(fixture.Commands);
    }

    [Fact]
    public async Task DeployBundle_RefusesToTerminateAnotherInstallation()
    {
        using var fixture = new BundleFixture();
        var service = new AppleMacAppDeploymentService(fixture.CreateRunner(false, foreignBundle: true));
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeployBundleAsync(
            fixture.Source, AppleMacAppDeploymentService.GetBundleSha256(fixture.Source),
            new AppleMacAppDeploymentRequest { InstallRoot = fixture.InstallRoot }));
        Assert.Contains("another location", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("/bin/kill", fixture.Commands);
        Assert.DoesNotContain("/usr/bin/ditto", fixture.Commands);
    }

    [Fact]
    public async Task DeployBundle_StopsBeforeInstallationWhenProcessIdentityChanges()
    {
        using var fixture = new BundleFixture();
        var service = new AppleMacAppDeploymentService(fixture.CreateRunner(false, staleProcess: true));
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeployBundleAsync(
            fixture.Source, AppleMacAppDeploymentService.GetBundleSha256(fixture.Source),
            new AppleMacAppDeploymentRequest { InstallRoot = fixture.InstallRoot }));
        Assert.Contains("Application identity changed before termination", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("/bin/kill", fixture.Commands);
        Assert.DoesNotContain("/usr/bin/ditto", fixture.Commands);
        Assert.False(File.Exists(fixture.InstalledExecutable));
    }

    private sealed class BundleFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "powerforge-bundle-" + Guid.NewGuid().ToString("N"));
        internal string Source { get; }
        internal string InstallRoot { get; }
        internal string InstalledExecutable => Path.Combine(InstallRoot, "Fixture.app", "Contents", "MacOS", "Fixture");
        internal List<string> Commands { get; } = new();
        internal BundleFixture()
        {
            Source = Path.Combine(_root, "input", "Fixture.app");
            InstallRoot = Directory.CreateDirectory(Path.Combine(_root, "Applications")).FullName;
            Directory.CreateDirectory(Path.Combine(Source, "Contents", "MacOS"));
            File.WriteAllText(Path.Combine(Source, "Contents", "Info.plist"), "fixture metadata");
            File.WriteAllText(Path.Combine(Source, "Contents", "MacOS", "Fixture"), "new executable");
        }
        internal IProcessRunner CreateRunner(bool staleInode, bool foreignBundle = false, bool staleProcess = false)
            => new BundleRunner(this, staleInode, foreignBundle, staleProcess);
        public void Dispose() => Directory.Delete(_root, recursive: true);
        private sealed class BundleRunner(BundleFixture fixture, bool staleInode, bool foreignBundle, bool staleProcess) : IProcessRunner
        {
            private bool _launched;
            public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
            {
                fixture.Commands.Add(request.FileName);
                var output = string.Empty;
                switch (request.FileName)
                {
                    case "/usr/bin/id": output = "501"; break;
                    case "/usr/bin/stat": output = request.Arguments.Last() == "/dev/console" ? "501" : "123:456"; break;
                    case "/usr/libexec/PlistBuddy": output = request.Arguments[1].EndsWith("CFBundleIdentifier", StringComparison.Ordinal) ? "com.example.fixture" : "Fixture"; break;
                    case "/usr/bin/osascript":
                        if (request.Arguments.Count > 5 && staleProcess)
                            return Task.FromResult(new ProcessRunResult(1, string.Empty, "Application identity changed before termination", request.FileName, TimeSpan.Zero, false));
                        output = !_launched && !foreignBundle && !staleProcess ? "[]" : JsonSerializer.Serialize(new[] { new { pid = 2468, executable = fixture.InstalledExecutable, bundle = foreignBundle ? "/another/Fixture.app.old" : Path.Combine(fixture.InstallRoot, "Fixture.app"), started = 1700000000.25 } });
                        break;
                    case "/usr/bin/ditto":
                        CopyTree(request.Arguments[0], request.Arguments[1]);
                        break;
                    case "/usr/bin/open":
                        Assert.Contains("--new", request.Arguments);
                        _launched = true;
                        break;
                    case "/usr/sbin/lsof": output = "p2468\nftxt\nD0x7b\ni" + (staleInode ? "999" : "456") + "\nn" + fixture.InstalledExecutable + "\n"; break;
                }
                return Task.FromResult(new ProcessRunResult(0, output, string.Empty, request.FileName, TimeSpan.Zero, false));
            }
            private static void CopyTree(string source, string target)
            {
                Directory.CreateDirectory(target);
                foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
                foreach (var directory in Directory.GetDirectories(source)) CopyTree(directory, Path.Combine(target, Path.GetFileName(directory)));
            }
        }
    }
}
