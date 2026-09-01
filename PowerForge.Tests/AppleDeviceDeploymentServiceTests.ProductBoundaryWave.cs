using PowerForge;

namespace PowerForge.Tests;

public sealed partial class AppleDeviceDeploymentServiceTests
{
    [Fact]
    public async Task BuildAsync_preserves_a_standalone_app_path_outside_private_DerivedData()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(
                root.FullName,
                "CasaRay.xcodeproj"));
            File.WriteAllText(
                Path.Combine(project.FullName, "project.pbxproj"),
                string.Empty);
            var appPath = ExternalOutputPath(root, "Configured.app");
            var runner = new CapturingProcessRunner(_ =>
            {
                Directory.CreateDirectory(appPath);
                File.WriteAllText(Path.Combine(appPath, "CasaRay"), "built");
                return Success("ok");
            });
            InitializeGitRepository(root.FullName);

            var result = await new AppleDeviceDeploymentService(runner).BuildAsync(
                new AppleAppBuildRequest
                {
                    ProjectPath = project.FullName,
                    Scheme = "CasaRay",
                    Destination = "id=device-1",
                    DerivedDataPath = ExternalOutputPath(root, "DerivedData"),
                    AppPath = appPath,
                    XcodeBuildExecutable = "/usr/bin/xcodebuild"
                });

            Assert.True(result.Succeeded);
            Assert.Equal(Path.GetFullPath(appPath), result.AppPath);
            Assert.True(File.Exists(Path.Combine(result.AppPath, "CasaRay")));
        }
        finally
        {
            DeleteExternalOutputs(root);
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task BuildForDeploymentAsync_rejects_a_private_product_root_inside_source_before_writing()
    {
        if (Path.DirectorySeparatorChar == '\\')
            return;

        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            Guid.NewGuid().ToString("N")));
        var previousTemporaryDirectory = Environment.GetEnvironmentVariable("TMPDIR");
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(
                root.FullName,
                "CasaRay.xcodeproj"));
            File.WriteAllText(
                Path.Combine(project.FullName, "project.pbxproj"),
                string.Empty);
            InitializeGitRepository(root.FullName);
            Environment.SetEnvironmentVariable("TMPDIR", root.FullName);
            var runner = new CapturingProcessRunner(_ => Success("ok"));

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleDeviceDeploymentService(runner).BuildForDeploymentAsync(
                    new AppleAppBuildRequest
                    {
                        ProjectPath = project.FullName,
                        Scheme = "CasaRay",
                        Destination = "id=device-1",
                        DerivedDataPath = ExternalOutputPath(root, "DerivedData"),
                        XcodeBuildExecutable = "/usr/bin/xcodebuild"
                    }));

            Assert.Contains(
                "private Apple product directory",
                error.Message,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                "outside BuildRoot",
                error.Message,
                StringComparison.OrdinalIgnoreCase);
            Assert.Empty(runner.Requests);
            Assert.False(Directory.Exists(Path.Combine(
                root.FullName,
                "PowerForge")));
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "TMPDIR",
                previousTemporaryDirectory);
            DeleteExternalOutputs(root);
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task BuildForDeploymentAsync_rejects_a_linked_product_root_before_snapshotting()
    {
        if (Path.DirectorySeparatorChar == '\\')
            return;

        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            Guid.NewGuid().ToString("N")));
        var redirectedApp = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests.LinkedProduct",
            Guid.NewGuid().ToString("N"),
            "CasaRay.app"));
        var sentinel = Path.Combine(redirectedApp.FullName, "sentinel");
        File.WriteAllText(sentinel, "preserve");
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(
                root.FullName,
                "CasaRay.xcodeproj"));
            File.WriteAllText(
                Path.Combine(project.FullName, "project.pbxproj"),
                string.Empty);
            InitializeGitRepository(root.FullName);
            var runner = new LinkedProductRunner(redirectedApp.FullName);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleDeviceDeploymentService(runner).BuildForDeploymentAsync(
                    new AppleAppBuildRequest
                    {
                        ProjectPath = project.FullName,
                        Scheme = "CasaRay",
                        Destination = "id=device-1",
                        DerivedDataPath = ExternalOutputPath(root, "DerivedData"),
                        XcodeBuildExecutable = "/usr/bin/xcodebuild"
                    }));

            Assert.Contains(
                "must not be a symbolic link or reparse point",
                error.Message,
                StringComparison.OrdinalIgnoreCase);
            Assert.Single(runner.Requests);
            Assert.Equal("preserve", File.ReadAllText(sentinel));
            Assert.Single(redirectedApp.EnumerateFileSystemInfos());
        }
        finally
        {
            DeleteExternalOutputs(root);
            try { root.Delete(recursive: true); } catch { /* best effort */ }
            try { redirectedApp.Parent!.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private sealed class LinkedProductRunner : IProcessRunner
    {
        private readonly string _redirectedApp;

        internal LinkedProductRunner(string redirectedApp)
        {
            _redirectedApp = redirectedApp;
        }

        internal List<ProcessRunRequest> Requests { get; } = new();

        public Task<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            request.InvokePreStartBoundary();
            request.InvokeStartBoundary();
            var productRoot = AppleDeploymentTestFixture
                .TryResolveConfiguredBuildProductDirectory(request)
                ?? throw new InvalidOperationException("Private build product directory was not configured.");
            Directory.CreateDirectory(productRoot);
            Directory.CreateSymbolicLink(
                Path.Combine(productRoot, "CasaRay.app"),
                _redirectedApp);
            var result = Success("ok");
            request.InvokeCompletionBoundary(result);
            return Task.FromResult(result);
        }
    }
}
