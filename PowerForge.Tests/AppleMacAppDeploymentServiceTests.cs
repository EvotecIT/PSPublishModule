using System.Diagnostics;
using PowerForge;

namespace PowerForge.Tests;

public sealed class AppleMacAppDeploymentServiceTests
{
    [Fact]
    public async Task DeployAsync_replaces_existing_app_and_launches_selected_profile()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "CasaRay.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            var derived = Directory.CreateDirectory(Path.Combine(root.FullName, "DerivedData"));
            var source = Directory.CreateDirectory(Path.Combine(derived.FullName, "Build", "Products", "Debug-maccatalyst", "CasaRay.app"));
            File.WriteAllText(Path.Combine(source.FullName, "version.txt"), "new");
            var installRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "Applications"));
            var existing = Directory.CreateDirectory(Path.Combine(installRoot.FullName, "CasaRay.app"));
            File.WriteAllText(Path.Combine(existing.FullName, "version.txt"), "old");

            var runner = new CapturingProcessRunner(request =>
            {
                if (request.FileName == "ditto-test")
                {
                    CopyDirectory(request.Arguments[0], request.Arguments[1]);
                    return Success("copied");
                }
                if (request.FileName == "open-test")
                {
                    var destination = Path.Combine(installRoot.FullName, "CasaRay.app");
                    Assert.Throws<InvalidOperationException>(() => AppleMacAppBundleReplacement.AcquireInstallLock(destination));
                }
                return Success("ok");
            });
            var service = new AppleMacAppDeploymentService(runner);
            InitializeGitRepository(root.FullName);

            var result = await service.DeployAsync(new AppleMacAppDeploymentRequest
            {
                ProjectPath = project.FullName,
                Scheme = "CasaRay",
                Platform = ApplePlatform.macOS,
                ArchiveVariant = AppleArchiveVariant.MacCatalyst,
                DerivedDataPath = derived.FullName,
                InstallRoot = installRoot.FullName,
                Launch = true,
                LaunchEnvironment = new Dictionary<string, string>
                {
                    ["CASARAY_ENABLE_SANDBOX_PURCHASES"] = "1"
                },
                XcodeBuildExecutable = "xcodebuild-test",
                DittoExecutable = "ditto-test",
                OpenExecutable = "open-test"
            });

            Assert.True(result.Succeeded);
            Assert.Equal("new", File.ReadAllText(Path.Combine(installRoot.FullName, "CasaRay.app", "version.txt")));
            Assert.DoesNotContain(Directory.EnumerateDirectories(installRoot.FullName), path => path.Contains("powerforge-backup", StringComparison.Ordinal));
            var terminate = Assert.Single(runner.Requests, request => request.FileName == "/usr/bin/pkill");
            Assert.Equal("-f", terminate.Arguments[0]);
            Assert.StartsWith("^", terminate.Arguments[1], StringComparison.Ordinal);
            Assert.Contains(
                System.Text.RegularExpressions.Regex.Escape(Path.Combine(installRoot.FullName, "CasaRay.app", "Contents", "MacOS")),
                terminate.Arguments[1],
                StringComparison.Ordinal);
            var launch = Assert.Single(runner.Requests, request => request.FileName == "open-test");
            Assert.Equal(new[]
            {
                "--new", "--fresh", "--env", "CASARAY_ENABLE_SANDBOX_PURCHASES=1",
                Path.Combine(installRoot.FullName, "CasaRay.app")
            }, launch.Arguments);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task DeployAsync_removes_partial_stage_when_copy_fails()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "CasaRay.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            var derived = Directory.CreateDirectory(Path.Combine(root.FullName, "DerivedData"));
            Directory.CreateDirectory(Path.Combine(derived.FullName, "Build", "Products", "Debug-maccatalyst", "CasaRay.app"));
            var installRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "Applications"));

            var runner = new CapturingProcessRunner(request =>
            {
                if (request.FileName == "ditto-test")
                {
                    Directory.CreateDirectory(request.Arguments[1]);
                    File.WriteAllText(Path.Combine(request.Arguments[1], "partial"), string.Empty);
                    return new ProcessRunResult(1, string.Empty, "copy failed", "tool", TimeSpan.FromMilliseconds(1), false);
                }
                return Success("ok");
            });
            InitializeGitRepository(root.FullName);

            var result = await new AppleMacAppDeploymentService(runner).DeployAsync(new AppleMacAppDeploymentRequest
            {
                ProjectPath = project.FullName,
                Scheme = "CasaRay",
                Platform = ApplePlatform.macOS,
                ArchiveVariant = AppleArchiveVariant.MacCatalyst,
                DerivedDataPath = derived.FullName,
                InstallRoot = installRoot.FullName,
                Launch = false,
                XcodeBuildExecutable = "xcodebuild-test",
                DittoExecutable = "ditto-test"
            });

            Assert.False(result.Succeeded);
            Assert.False(result.Install?.Succeeded);
            Assert.DoesNotContain(Directory.EnumerateDirectories(installRoot.FullName), path => path.Contains("powerforge-stage", StringComparison.Ordinal));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task DeployAsync_recovers_interrupted_backup_before_replacement()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "CasaRay.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            var derived = Directory.CreateDirectory(Path.Combine(root.FullName, "DerivedData"));
            var source = Directory.CreateDirectory(Path.Combine(derived.FullName, "Build", "Products", "Debug-maccatalyst", "CasaRay.app"));
            File.WriteAllText(Path.Combine(source.FullName, "version.txt"), "new");
            var installRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "Applications"));
            var orphanedBackup = Directory.CreateDirectory(Path.Combine(installRoot.FullName, ".CasaRay.app.powerforge-backup-interrupted"));
            File.WriteAllText(Path.Combine(orphanedBackup.FullName, "version.txt"), "old");

            var runner = new CapturingProcessRunner(request =>
            {
                if (request.FileName == "ditto-test")
                    CopyDirectory(request.Arguments[0], request.Arguments[1]);
                return Success("ok");
            });
            InitializeGitRepository(root.FullName);

            var result = await new AppleMacAppDeploymentService(runner).DeployAsync(new AppleMacAppDeploymentRequest
            {
                ProjectPath = project.FullName,
                Scheme = "CasaRay",
                Platform = ApplePlatform.macOS,
                ArchiveVariant = AppleArchiveVariant.MacCatalyst,
                DerivedDataPath = derived.FullName,
                InstallRoot = installRoot.FullName,
                Launch = false,
                XcodeBuildExecutable = "xcodebuild-test",
                DittoExecutable = "ditto-test"
            });

            Assert.True(result.Succeeded);
            Assert.Equal("new", File.ReadAllText(Path.Combine(installRoot.FullName, "CasaRay.app", "version.txt")));
            Assert.DoesNotContain(Directory.EnumerateDirectories(installRoot.FullName), path => path.Contains("powerforge-", StringComparison.Ordinal));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void AcquireInstallLock_rejects_overlapping_install()
    {
        var destination = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"), "CasaRay.app");
        using var first = AppleMacAppBundleReplacement.AcquireInstallLock(destination);

        Assert.Throws<InvalidOperationException>(() => AppleMacAppBundleReplacement.AcquireInstallLock(destination));
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static ProcessRunResult Success(string stdOut)
        => new(0, stdOut, string.Empty, "tool", TimeSpan.FromMilliseconds(1), false);

    private static void InitializeGitRepository(string workingDirectory)
    {
        RunGit(workingDirectory, "init");
        RunGit(workingDirectory, "config", "user.name", "PowerForge Tests");
        RunGit(
            workingDirectory,
            "config",
            "user.email",
            "powerforge-tests@example.invalid");
        RunGit(workingDirectory, "add", ".");
        RunGit(workingDirectory, "commit", "-m", "fixture");
    }

    private static void RunGit(
        string workingDirectory,
        params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(standardError);
    }

    private sealed class CapturingProcessRunner : IProcessRunner
    {
        private readonly Func<ProcessRunRequest, ProcessRunResult> _execute;

        public CapturingProcessRunner(Func<ProcessRunRequest, ProcessRunResult> execute)
        {
            _execute = execute;
        }

        public List<ProcessRunRequest> Requests { get; } = new();

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(_execute(request));
        }
    }
}
