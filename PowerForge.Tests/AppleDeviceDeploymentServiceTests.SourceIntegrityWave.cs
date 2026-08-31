using PowerForge;

namespace PowerForge.Tests;

public sealed partial class AppleDeviceDeploymentServiceTests
{
    [Fact]
    public async Task BuildAsync_rejects_a_non_system_xcodebuild_before_tools_run()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "CasaRay.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            var runner = new CapturingProcessRunner(_ => Success("unexpected"));

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleDeviceDeploymentService(runner).BuildAsync(new AppleAppBuildRequest
                {
                    ProjectPath = project.FullName,
                    Scheme = "CasaRay",
                    Destination = "id=device-1",
                    XcodeBuildExecutable = "/tmp/xcodebuild-wrapper"
                }));

            Assert.Contains("trusted system tool '/usr/bin/xcodebuild'", error.Message, StringComparison.Ordinal);
            Assert.Empty(runner.Requests);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task BuildAsync_rejects_a_non_system_rsync_before_tools_run()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "CasaRay.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            var runner = new CapturingProcessRunner(_ => Success("unexpected"));

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleDeviceDeploymentService(runner).BuildAsync(new AppleAppBuildRequest
                {
                    ProjectPath = project.FullName,
                    Scheme = "CasaRay",
                    Destination = "id=device-1",
                    UseBuildMirror = true,
                    RsyncExecutable = "/tmp/rsync-wrapper"
                }));

            Assert.Contains("trusted system tool '/usr/bin/rsync'", error.Message, StringComparison.Ordinal);
            Assert.Empty(runner.Requests);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task DeployAsync_rejects_a_non_system_xcrun_before_building()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "CasaRay.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            var runner = new CapturingProcessRunner(_ => Success("unexpected"));

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleDeviceDeploymentService(runner).DeployAsync(
                    new AppleAppDeviceDeploymentRequest
                    {
                        ProjectPath = project.FullName,
                        Scheme = "CasaRay",
                        DeviceIdentifier = "device-1",
                        XcrunExecutable = "/tmp/xcrun-wrapper"
                    }));

            Assert.Contains("trusted system tool '/usr/bin/xcrun'", error.Message, StringComparison.Ordinal);
            Assert.Empty(runner.Requests);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task BuildAsync_rejects_additional_xcodebuild_arguments_before_tools_run()
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
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            InitializeGitRepository(root.FullName);
            var runner = new CapturingProcessRunner(_ => Success("unexpected"));

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleDeviceDeploymentService(runner).BuildAsync(new AppleAppBuildRequest
                {
                    ProjectPath = project.FullName,
                    Scheme = "CasaRay",
                    Destination = "id=device-1",
                    DerivedDataPath = ExternalOutputPath(root, "DerivedData"),
                    AdditionalArguments = ["-xcconfig", "/tmp/External.xcconfig"]
                }));

            Assert.Contains("do not accept AdditionalArguments", error.Message, StringComparison.Ordinal);
            Assert.Empty(runner.Requests);
        }
        finally
        {
            DeleteExternalOutputs(root);
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task BuildAsync_rejects_tracked_inputs_hidden_by_root_mirror_exclusions()
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
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            var build = Directory.CreateDirectory(Path.Combine(root.FullName, "build"));
            File.WriteAllText(Path.Combine(build.FullName, "schema.json"), "{}");
            InitializeGitRepository(root.FullName);
            var runner = new CapturingProcessRunner(_ => Success("unexpected"));

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleDeviceDeploymentService(runner).BuildAsync(new AppleAppBuildRequest
                {
                    ProjectPath = project.FullName,
                    BuildRoot = root.FullName,
                    Scheme = "CasaRay",
                    Destination = "id=device-1",
                    DerivedDataPath = ExternalOutputPath(root, "DerivedData"),
                    UseBuildMirror = true
                }));

            Assert.Contains("build/schema.json", error.Message, StringComparison.Ordinal);
            Assert.Contains("disable the build mirror", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(runner.Requests);
        }
        finally
        {
            DeleteExternalOutputs(root);
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task BuildAsync_rejects_a_mirror_replacement_after_the_rsync_completion_boundary()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            Guid.NewGuid().ToString("N")));
        var mirrorRoot = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests.Mirror",
            Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "CasaRay.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            InitializeGitRepository(root.FullName);
            var mirror = Path.Combine(mirrorRoot.FullName, "mirror");
            var runner = new MirrorPostCompletionMutationRunner();

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleDeviceDeploymentService(runner).BuildAsync(new AppleAppBuildRequest
                {
                    ProjectPath = project.FullName,
                    BuildRoot = root.FullName,
                    Scheme = "CasaRay",
                    Destination = "id=device-1",
                    DerivedDataPath = ExternalOutputPath(root, "DerivedData"),
                    UseBuildMirror = true,
                    BuildMirrorPath = mirror,
                    RsyncExecutable = "/usr/bin/rsync",
                    XcodeBuildExecutable = "/usr/bin/xcodebuild"
                }));

            Assert.Contains("local Apple build mirror changed", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(2, runner.Requests.Count);
        }
        finally
        {
            DeleteExternalOutputs(root);
            try { root.Delete(recursive: true); } catch { /* best effort */ }
            try { mirrorRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task BuildAsync_rejects_selected_scheme_execution_actions()
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
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            InitializeGitRepository(root.FullName);
            var schemePath = Path.Combine(
                project.FullName,
                "xcshareddata",
                "xcschemes",
                "CasaRay.xcscheme");
            File.WriteAllText(schemePath, "<Scheme><ExecutionAction /></Scheme>");
            RunGit(root.FullName, "add", schemePath);
            RunGit(root.FullName, "commit", "-m", "Add execution action");
            var runner = new CapturingProcessRunner(_ => Success("unexpected"));

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleDeviceDeploymentService(runner).BuildAsync(new AppleAppBuildRequest
                {
                    ProjectPath = project.FullName,
                    Scheme = "CasaRay",
                    Destination = "id=device-1",
                    DerivedDataPath = ExternalOutputPath(root, "DerivedData")
                }));

            Assert.Contains("scheme actions are not accepted", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(runner.Requests);
        }
        finally
        {
            DeleteExternalOutputs(root);
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task BuildAsync_rejects_a_derived_data_symlink_alias_into_source()
    {
        if (Path.DirectorySeparatorChar == '\\')
            return;

        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            Guid.NewGuid().ToString("N")));
        var alias = Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests.Alias",
            Guid.NewGuid().ToString("N"));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "CasaRay.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            InitializeGitRepository(root.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(alias)!);
            Directory.CreateSymbolicLink(alias, root.FullName);
            var runner = new CapturingProcessRunner(_ => Success("unexpected"));

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleDeviceDeploymentService(runner).BuildAsync(new AppleAppBuildRequest
                {
                    ProjectPath = project.FullName,
                    Scheme = "CasaRay",
                    Destination = "id=device-1",
                    DerivedDataPath = Path.Combine(alias, "DerivedData")
                }));

            Assert.Contains(nameof(AppleAppBuildRequest.DerivedDataPath), error.Message, StringComparison.Ordinal);
            Assert.Empty(runner.Requests);
        }
        finally
        {
            try { Directory.Delete(alias); } catch { /* best effort */ }
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task DeployAsync_rejects_a_transient_product_mutation_during_device_install()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "CasaRay.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            var app = Directory.CreateDirectory(ExternalOutputPath(root, "CasaRay.app"));
            var payload = Path.Combine(app.FullName, "payload");
            File.WriteAllText(payload, "approved");
            InitializeGitRepository(root.FullName);
            var runner = new CapturingProcessRunner(request =>
            {
                if (request.Arguments.Contains("install"))
                {
                    var snapshotPayload = Path.Combine(request.Arguments[^1], "payload");
                    File.WriteAllText(snapshotPayload, "replacement");
                    File.WriteAllText(snapshotPayload, "approved");
                    return Success("App installed:\n• bundleID: com.evotecit.casaray\n");
                }
                return Success("ok");
            });

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleDeviceDeploymentService(runner).DeployAsync(
                    new AppleAppDeviceDeploymentRequest
                    {
                        ProjectPath = project.FullName,
                        Scheme = "CasaRay",
                        AppPath = app.FullName,
                        DeviceIdentifier = "device-1",
                        BundleIdentifier = "com.evotecit.casaray",
                        Launch = false,
                        XcodeBuildExecutable = "/usr/bin/xcodebuild",
                        XcrunExecutable = "/usr/bin/xcrun"
                    }));

            Assert.Contains("private built Apple app snapshot changed", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteExternalOutputs(root);
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private sealed class MirrorPostCompletionMutationRunner : IProcessRunner
    {
        public List<ProcessRunRequest> Requests { get; } = new();

        public Task<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            request.InvokeStartBoundary();
            var result = Success("ok");
            if (request.FileName.Equals("/usr/bin/rsync", StringComparison.Ordinal))
            {
                var mirrorPath = request.Arguments[^1]
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                Directory.CreateDirectory(mirrorPath);
                var payload = Path.Combine(mirrorPath, "payload");
                File.WriteAllText(payload, "approved");
                request.InvokeCompletionBoundary(result);
                File.WriteAllText(payload, "replacement");
            }
            else
            {
                request.InvokeCompletionBoundary(result);
            }
            return Task.FromResult(result);
        }
    }
}
