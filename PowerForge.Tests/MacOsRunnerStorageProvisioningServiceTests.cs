using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PowerForge.Tests;

public sealed class MacOsRunnerStorageProvisioningServiceTests
{
    [Fact]
    public void Provision_DryRun_PlansDurableRunnerStorageWithoutChangingFiles()
    {
        using var sandbox = new Sandbox();
        var fixture = sandbox.CreateFixture();
        var service = fixture.CreateService();

        var result = service.Provision(fixture.CreateSpec(dryRun: true));

        Assert.True(result.DryRun);
        Assert.False(result.AlreadyConfigured);
        Assert.Contains(result.Steps, step => step.Id == "core-simulator-image" && step.Changed);
        Assert.Contains(result.Steps, step => step.Id == "core-simulator-mount" && step.Changed);
        Assert.Contains(result.Steps, step => step.Id == "cache-nuget-packages" && step.Changed);
        Assert.Contains(result.Steps, step => step.Id == "runner-wrapper" && step.Changed);
        Assert.False(Directory.Exists(result.CoreSimulatorImagePath));
        Assert.False(File.Exists(result.RunnerWrapperPath));
    }

    [Fact]
    public void Provision_Apply_RefusesToMutateWhileRunnerListenerIsActive()
    {
        using var sandbox = new Sandbox();
        var fixture = sandbox.CreateFixture();
        fixture.ProcessRunner.ActiveRunnerRoot = fixture.RunnerRoot;
        var service = fixture.CreateService();

        var exception = Assert.Throws<InvalidOperationException>(
            () => service.Provision(fixture.CreateSpec(dryRun: false)));

        Assert.Contains("runner is active", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(fixture.StateRoot, "CoreSimulator.sparsebundle")));
    }

    [Fact]
    public void Provision_Apply_CreatesSparseBundleLinksConfigurationAndWrapper()
    {
        using var sandbox = new Sandbox();
        var fixture = sandbox.CreateFixture();
        var service = fixture.CreateService();

        var result = service.Provision(fixture.CreateSpec(dryRun: false));

        Assert.False(result.DryRun);
        Assert.True(Directory.Exists(result.CoreSimulatorImagePath));
        Assert.True(File.Exists(result.RunnerWrapperPath));
        Assert.Contains("hdiutil attach", File.ReadAllText(result.RunnerWrapperPath), StringComparison.Ordinal);
        Assert.Contains("refusing to start", File.ReadAllText(result.RunnerWrapperPath), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            Path.Combine(fixture.StateRoot, "caches", "nuget-packages"),
            ResolveLink(Path.Combine(fixture.HomeRoot, ".nuget", "packages")));
        Assert.Equal(
            Path.Combine(fixture.StateRoot, "caches", "org.swift.swiftpm"),
            ResolveLink(Path.Combine(fixture.HomeRoot, "Library", "Caches", "org.swift.swiftpm")));
        Assert.Contains(
            "NUGET_PACKAGES=" + Path.Combine(fixture.StateRoot, "caches", "nuget-packages"),
            File.ReadAllText(Path.Combine(fixture.RunnerRoot, ".env")),
            StringComparison.Ordinal);
        Assert.StartsWith(
            "# Runner locale\nLANG=en_US.UTF-8\n",
            File.ReadAllText(Path.Combine(fixture.RunnerRoot, ".env")),
            StringComparison.Ordinal);
        Assert.Contains(
            Path.GetRelativePath(fixture.RunnerRoot, fixture.WorkRoot).Replace(Path.DirectorySeparatorChar, '/'),
            File.ReadAllText(Path.Combine(fixture.RunnerRoot, ".runner")),
            StringComparison.Ordinal);
        Assert.Contains(
            fixture.ProcessRunner.Requests,
            request => request.FileName == "/usr/bin/plutil"
                       && request.Arguments.Contains("-replace")
                       && request.Arguments.Contains("ProgramArguments"));
        Assert.True(File.Exists(Path.Combine(result.BackupRootPath, "configuration", "runner.json")));
        Assert.True(File.Exists(Path.Combine(result.BackupRootPath, "configuration", Path.GetFileName(fixture.LaunchAgentPath))));
    }

    [Fact]
    public void Provision_DryRun_RejectsNonMacOsHosts()
    {
        using var sandbox = new Sandbox();
        var fixture = sandbox.CreateFixture();
        var service = new MacOsRunnerStorageProvisioningService(
            new NullLogger(),
            fixture.ProcessRunner,
            () => false,
            () => fixture.Now,
            () => fixture.HomeRoot,
            (value, _) => Path.GetFullPath(value!),
            _ => { },
            _ => fixture.ExternalVolumeRoot,
            _ => "TEST-VOLUME-UUID");

        Assert.Throws<PlatformNotSupportedException>(
            () => service.Provision(fixture.CreateSpec(dryRun: true)));
    }

    [Fact]
    public void Provision_DryRun_RequiresValidatedExternalStorage()
    {
        using var sandbox = new Sandbox();
        var fixture = sandbox.CreateFixture();
        var service = new MacOsRunnerStorageProvisioningService(
            new NullLogger(),
            fixture.ProcessRunner,
            () => true,
            () => fixture.Now,
            () => fixture.HomeRoot,
            (value, _) => Path.GetFullPath(value!),
            _ => throw new InvalidOperationException("External runner storage must use APFS."),
            _ => fixture.ExternalVolumeRoot,
            _ => "TEST-VOLUME-UUID");

        var exception = Assert.Throws<InvalidOperationException>(
            () => service.Provision(fixture.CreateSpec(dryRun: true)));

        Assert.Contains("must use APFS", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Provision_DryRun_RejectsExternalPathSymlinkEscape()
    {
        using var sandbox = new Sandbox();
        var fixture = sandbox.CreateFixture();
        Directory.CreateDirectory(fixture.ExternalVolumeRoot);
        var internalTarget = Path.Combine(fixture.Root, "internal-state");
        Directory.CreateDirectory(internalTarget);
        Directory.CreateSymbolicLink(fixture.StateRoot, internalTarget);
        var service = fixture.CreateService();

        var exception = Assert.Throws<InvalidOperationException>(
            () => service.Provision(fixture.CreateSpec(dryRun: true)));

        Assert.Contains("symbolic link", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Provision_Apply_SecondRunIsNoOpAndKeepsOriginalBackup()
    {
        using var sandbox = new Sandbox();
        var fixture = sandbox.CreateFixture();
        var service = fixture.CreateService();

        var first = service.Provision(fixture.CreateSpec(dryRun: false));
        var requestCount = fixture.ProcessRunner.Requests.Count;
        var second = service.Provision(fixture.CreateSpec(dryRun: false));

        Assert.Equal(first.BackupRootPath, second.BackupRootPath);
        Assert.EndsWith("runner-storage-original", second.BackupRootPath, StringComparison.Ordinal);
        Assert.True(second.AlreadyConfigured);
        Assert.All(second.Steps, step => Assert.True(step.Skipped));
        Assert.Equal(requestCount + 4, fixture.ProcessRunner.Requests.Count);
        Assert.True(File.Exists(Path.Combine(second.BackupRootPath, "configuration", "runner.json")));
    }

    private static string ResolveLink(string path)
    {
        var info = new DirectoryInfo(path);
        Assert.NotNull(info.LinkTarget);
        return Path.GetFullPath(Path.Combine(info.Parent!.FullName, info.LinkTarget!));
    }

    private sealed class Fixture
    {
        public Fixture(string root)
        {
            Root = root;
            HomeRoot = Path.Combine(root, "home");
            RunnerRoot = Path.Combine(root, "actions-runner");
            ExternalVolumeRoot = Path.Combine(root, "external");
            StateRoot = Path.Combine(ExternalVolumeRoot, "runner-state");
            WorkRoot = Path.Combine(ExternalVolumeRoot, "work");
            CoreSimulatorRoot = Path.Combine(HomeRoot, "Library", "Developer", "CoreSimulator");
            LaunchAgentPath = Path.Combine(HomeRoot, "Library", "LaunchAgents", "actions.runner.test.plist");
            Now = new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

            Directory.CreateDirectory(RunnerRoot);
            Directory.CreateDirectory(CoreSimulatorRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(LaunchAgentPath)!);
            Directory.CreateDirectory(Path.Combine(HomeRoot, ".nuget", "packages"));
            Directory.CreateDirectory(Path.Combine(HomeRoot, "Library", "Caches", "ms-playwright"));
            Directory.CreateDirectory(Path.Combine(HomeRoot, "Library", "Caches", "org.swift.swiftpm"));
            File.WriteAllText(Path.Combine(RunnerRoot, "runsvc.sh"), "#!/bin/bash\n");
            File.WriteAllText(Path.Combine(RunnerRoot, ".runner"), "{\"workFolder\":\"_work\"}\n");
            File.WriteAllText(Path.Combine(RunnerRoot, ".env"), "# Runner locale\nLANG=en_US.UTF-8\n");
            File.WriteAllText(Path.Combine(RunnerRoot, ".service"), LaunchAgentPath + "\n");
            File.WriteAllText(LaunchAgentPath, "<plist />\n");
            ProcessRunner = new FakeProcessRunner(StateRoot, CoreSimulatorRoot);
            ProcessRunner.ConfiguredRunnerRoot = RunnerRoot;
        }

        public string Root { get; }
        public string HomeRoot { get; }
        public string RunnerRoot { get; }
        public string ExternalVolumeRoot { get; }
        public string StateRoot { get; }
        public string WorkRoot { get; }
        public string CoreSimulatorRoot { get; }
        public string LaunchAgentPath { get; }
        public DateTimeOffset Now { get; }
        public FakeProcessRunner ProcessRunner { get; }

        public MacOsRunnerStorageProvisioningSpec CreateSpec(bool dryRun)
            => new()
            {
                RunnerRootPath = RunnerRoot,
                StateRootPath = StateRoot,
                WorkRootPath = WorkRoot,
                CoreSimulatorPath = CoreSimulatorRoot,
                LaunchAgentPath = LaunchAgentPath,
                DryRun = dryRun
            };

        public MacOsRunnerStorageProvisioningService CreateService()
            => new(
                new NullLogger(),
                ProcessRunner,
                () => true,
                () => Now,
                () => HomeRoot,
                (value, _) => Path.GetFullPath(value!),
                _ => { },
                _ => ExternalVolumeRoot,
                _ => "TEST-VOLUME-UUID");
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        private readonly string _stateRoot;
        private readonly string _coreSimulatorRoot;
        private bool _normalImageMounted;
        private string? _launchWrapper;

        public FakeProcessRunner(string stateRoot, string coreSimulatorRoot)
        {
            _stateRoot = stateRoot;
            _coreSimulatorRoot = coreSimulatorRoot;
        }

        public string? ActiveRunnerRoot { get; set; }
        public string ConfiguredRunnerRoot { get; set; } = string.Empty;
        public List<ProcessRunRequest> Requests { get; } = new();

        public Task<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (request.FileName == "/usr/bin/pgrep")
            {
                return Task.FromResult(string.IsNullOrWhiteSpace(ActiveRunnerRoot)
                    ? Result(1)
                    : Result(0, $"123 {ActiveRunnerRoot}/bin/Runner.Listener run --startuptype service\n"));
            }

            if (request.FileName == "/sbin/mount")
            {
                var output = _normalImageMounted
                    ? $"/dev/disk99s1 on {_coreSimulatorRoot} (apfs, local)\n"
                    : string.Empty;
                return Task.FromResult(Result(0, output));
            }

            if (request.FileName == "/usr/bin/hdiutil" && request.Arguments.SequenceEqual(new[] { "info" }))
            {
                var image = Path.Combine(_stateRoot, "CoreSimulator.sparsebundle");
                var output = _normalImageMounted
                    ? $"image-path      : {image}\nmount-point    : {_coreSimulatorRoot}\n"
                    : string.Empty;
                return Task.FromResult(Result(0, output));
            }

            if (request.FileName == "/usr/bin/hdiutil" && request.Arguments.Contains("create"))
            {
                Directory.CreateDirectory(request.Arguments.Last());
                return Task.FromResult(Result(0));
            }

            if (request.FileName == "/usr/bin/hdiutil" && request.Arguments.Contains("attach"))
            {
                var mountIndex = request.Arguments.ToList().IndexOf("-mountpoint");
                if (mountIndex >= 0 && request.Arguments[mountIndex + 1] == _coreSimulatorRoot)
                    _normalImageMounted = true;
                return Task.FromResult(Result(0));
            }

            if (request.FileName == "/usr/bin/plutil" && request.Arguments.Contains("-extract"))
            {
                var key = request.Arguments[1];
                return Task.FromResult(Result(
                    0,
                    key == "WorkingDirectory"
                        ? ConfiguredRunnerRoot + "\n"
                        : (_launchWrapper ?? "/old/runner/runsvc.sh") + "\n"));
            }

            if (request.FileName == "/usr/bin/plutil" && request.Arguments.Contains("-replace"))
            {
                var jsonIndex = request.Arguments.ToList().IndexOf("-json");
                var values = System.Text.Json.JsonSerializer.Deserialize<string[]>(request.Arguments[jsonIndex + 1]);
                _launchWrapper = values?.Single();
                return Task.FromResult(Result(0));
            }

            if (request.FileName == "/usr/bin/ditto")
            {
                Directory.CreateDirectory(request.Arguments.Last());
                return Task.FromResult(Result(0));
            }

            return Task.FromResult(Result(0));
        }

        private static ProcessRunResult Result(int exitCode, string stdout = "", string stderr = "")
            => new(exitCode, stdout, stderr, "fake", TimeSpan.Zero, timedOut: false);
    }

    private sealed class Sandbox : IDisposable
    {
        private readonly string _path = Path.Combine(
            Path.GetTempPath(),
            "PowerForgeRunnerStorage-" + Guid.NewGuid().ToString("N"));

        public Sandbox()
        {
            Directory.CreateDirectory(_path);
        }

        public Fixture CreateFixture() => new(_path);

        public void Dispose()
        {
            try
            {
                Directory.Delete(_path, recursive: true);
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }
    }
}
