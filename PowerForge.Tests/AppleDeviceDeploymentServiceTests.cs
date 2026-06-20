using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed class AppleDeviceDeploymentServiceTests
{
    [Fact]
    public async Task GetDevicesAsync_parses_available_devices()
    {
        var output = """
Name       Hostname                    Identifier                             State                Model
--------   -------------------------   ------------------------------------   ------------------   ------------------------------
EvoPhone   EvoPhone.coredevice.local   3DA86114-A96C-5109-970A-B52EA186B0E9   connected           iPhone 17 Pro Max (iPhone18,2)
LabPhone   LabPhone.coredevice.local   22222222-2222-2222-2222-222222222222   available (paired)  iPhone 15
OldPhone   OldPhone.coredevice.local   11111111-1111-1111-1111-111111111111   unavailable          iPhone 13
""";
        var runner = new CapturingProcessRunner(_ => Success(output));
        var service = new AppleDeviceDeploymentService(runner);

        var devices = await service.GetDevicesAsync(new AppleDeviceListRequest
        {
            XcrunExecutable = "xcrun-test"
        });

        Assert.Equal(2, devices.Count);
        var device = devices[0];
        Assert.Equal("EvoPhone", device.Name);
        Assert.Equal("3DA86114-A96C-5109-970A-B52EA186B0E9", device.Identifier);
        Assert.Equal("connected", device.State);
        Assert.Equal("iPhone 17 Pro Max (iPhone18,2)", device.Model);
        Assert.Equal("xcrun-test", runner.Requests[0].FileName);
        Assert.Equal(new[] { "devicectl", "list", "devices" }, runner.Requests[0].Arguments);
    }

    [Fact]
    public async Task GetDevicesAsync_throws_when_devicectl_fails()
    {
        var runner = new CapturingProcessRunner(_ => new ProcessRunResult(
            72,
            string.Empty,
            "developer tools are not configured",
            "xcrun-test",
            TimeSpan.FromMilliseconds(1),
            timedOut: false));
        var service = new AppleDeviceDeploymentService(runner);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetDevicesAsync(new AppleDeviceListRequest
        {
            XcrunExecutable = "xcrun-test"
        }));

        Assert.Contains("devicectl list devices failed", ex.Message, StringComparison.Ordinal);
        Assert.Contains("developer tools are not configured", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_builds_xcodebuild_device_command()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "Tactra.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            var derived = Path.Combine(root.FullName, "DerivedData");
            var runner = new CapturingProcessRunner(_ => Success("ok"));
            var service = new AppleDeviceDeploymentService(runner);

            var result = await service.BuildAsync(new AppleAppBuildRequest
            {
                ProjectPath = project.FullName,
                Scheme = "Tactra",
                DeviceIdentifier = "3DA86114-A96C-5109-970A-B52EA186B0E9",
                DerivedDataPath = derived,
                XcodeBuildExecutable = "xcodebuild-test"
            });

            Assert.True(result.Succeeded);
            Assert.Equal($"id=3DA86114-A96C-5109-970A-B52EA186B0E9", result.Destination);
            Assert.Equal(Path.Combine(derived, "Build", "Products", "Debug-iphoneos", "Tactra.app"), result.AppPath);
            Assert.Single(runner.Requests);
            var request = runner.Requests[0];
            Assert.Equal("xcodebuild-test", request.FileName);
            Assert.Equal(new[]
            {
                "-project",
                project.FullName,
                "-scheme",
                "Tactra",
                "-configuration",
                "Debug",
                "-destination",
                "id=3DA86114-A96C-5109-970A-B52EA186B0E9",
                "-derivedDataPath",
                derived,
                "-allowProvisioningUpdates",
                "build"
            }, request.Arguments);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task BuildAsync_uses_plain_configuration_directory_for_macos_app_path()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "Tactra.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            var derived = Path.Combine(root.FullName, "DerivedData");
            var runner = new CapturingProcessRunner(_ => Success("ok"));
            var service = new AppleDeviceDeploymentService(runner);

            var result = await service.BuildAsync(new AppleAppBuildRequest
            {
                ProjectPath = project.FullName,
                Scheme = "Tactra",
                Platform = ApplePlatform.macOS,
                Destination = "platform=macOS",
                DerivedDataPath = derived,
                XcodeBuildExecutable = "xcodebuild-test"
            });

            Assert.True(result.Succeeded);
            Assert.Equal(Path.Combine(derived, "Build", "Products", "Debug", "Tactra.app"), result.AppPath);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task BuildAsync_uses_rsync_mirror_and_rewrites_project_path()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var mirrorRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Mirror", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "Tactra.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            var mirror = Path.Combine(mirrorRoot.FullName, "mirror");
            var derived = Path.Combine(root.FullName, "DerivedData");
            var runner = new CapturingProcessRunner(_ => Success("ok"));
            var service = new AppleDeviceDeploymentService(runner);

            var result = await service.BuildAsync(new AppleAppBuildRequest
            {
                ProjectPath = project.FullName,
                Scheme = "Tactra",
                Destination = "id=device-1",
                DerivedDataPath = derived,
                UseBuildMirror = true,
                BuildRoot = root.FullName,
                BuildMirrorPath = mirror,
                RsyncExecutable = "rsync-test",
                XcodeBuildExecutable = "xcodebuild-test"
            });

            Assert.True(result.Succeeded);
            Assert.Equal(mirror, result.BuildMirrorPath);
            Assert.Equal(2, runner.Requests.Count);
            Assert.Equal("rsync-test", runner.Requests[0].FileName);
            Assert.Contains("--delete", runner.Requests[0].Arguments);
            Assert.Contains(root.FullName + Path.DirectorySeparatorChar, runner.Requests[0].Arguments);
            Assert.Contains(mirror + Path.DirectorySeparatorChar, runner.Requests[0].Arguments);

            var buildRequest = runner.Requests[1];
            Assert.Equal("xcodebuild-test", buildRequest.FileName);
            Assert.Equal(mirror, buildRequest.WorkingDirectory);
            Assert.Contains(Path.Combine(mirror, "Tactra.xcodeproj"), buildRequest.Arguments);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
            try { mirrorRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task InstallAsync_runs_devicectl_and_parses_output()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var app = Directory.CreateDirectory(Path.Combine(root.FullName, "Tactra.app"));
            var output = """
App installed:
• bundleID: com.evotecit.tactra
• installationURL: file:///private/var/containers/Bundle/Application/ABC/Tactra.app/
""";
            var runner = new CapturingProcessRunner(_ => Success(output));
            var service = new AppleDeviceDeploymentService(runner);

            var result = await service.InstallAsync(new AppleAppInstallRequest
            {
                AppPath = app.FullName,
                DeviceIdentifier = "device-1",
                XcrunExecutable = "xcrun-test"
            });

            Assert.True(result.Succeeded);
            Assert.Equal("com.evotecit.tactra", result.BundleIdentifier);
            Assert.Equal("file:///private/var/containers/Bundle/Application/ABC/Tactra.app/", result.InstallationUrl);
            Assert.Single(runner.Requests);
            Assert.Equal(new[] { "devicectl", "device", "install", "app", "--device", "device-1", app.FullName }, runner.Requests[0].Arguments);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task DeployAsync_builds_installs_and_launches()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "Tactra.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            var app = Directory.CreateDirectory(Path.Combine(root.FullName, "Tactra.app"));
            var runner = new CapturingProcessRunner(request =>
            {
                if (request.Arguments.Contains("install"))
                    return Success("App installed:\n• bundleID: com.evotecit.tactra\n");

                return Success("ok");
            });
            var service = new AppleDeviceDeploymentService(runner);

            var result = await service.DeployAsync(new AppleAppDeviceDeploymentRequest
            {
                ProjectPath = project.FullName,
                Scheme = "Tactra",
                AppPath = app.FullName,
                DeviceIdentifier = "device-1",
                BundleIdentifier = "com.evotecit.tactra",
                Launch = true,
                XcodeBuildExecutable = "xcodebuild-test",
                XcrunExecutable = "xcrun-test"
            });

            Assert.True(result.Succeeded);
            Assert.NotNull(result.Install);
            Assert.NotNull(result.Launch);
            Assert.Equal(3, runner.Requests.Count);
            Assert.Equal("xcodebuild-test", runner.Requests[0].FileName);
            Assert.Equal(new[] { "devicectl", "device", "install", "app", "--device", "device-1", app.FullName }, runner.Requests[1].Arguments);
            Assert.Equal(new[] { "devicectl", "device", "process", "launch", "--device", "device-1", "com.evotecit.tactra" }, runner.Requests[2].Arguments);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task DeployAsync_reuses_id_destination_for_install_and_launch()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "Tactra.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            var app = Directory.CreateDirectory(Path.Combine(root.FullName, "Tactra.app"));
            var runner = new CapturingProcessRunner(request =>
            {
                if (request.Arguments.Contains("install"))
                    return Success("App installed:\n• bundleID: com.evotecit.tactra\n");

                return Success("ok");
            });
            var service = new AppleDeviceDeploymentService(runner);

            var result = await service.DeployAsync(new AppleAppDeviceDeploymentRequest
            {
                ProjectPath = project.FullName,
                Scheme = "Tactra",
                AppPath = app.FullName,
                Destination = "id=device-1",
                BundleIdentifier = "com.evotecit.tactra",
                Launch = true,
                XcodeBuildExecutable = "xcodebuild-test",
                XcrunExecutable = "xcrun-test"
            });

            Assert.True(result.Succeeded);
            Assert.Equal(new[] { "devicectl", "device", "install", "app", "--device", "device-1", app.FullName }, runner.Requests[1].Arguments);
            Assert.Equal(new[] { "devicectl", "device", "process", "launch", "--device", "device-1", "com.evotecit.tactra" }, runner.Requests[2].Arguments);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task DeployAsync_treats_locked_device_launch_as_successful_deployment()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "Tactra.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            var app = Directory.CreateDirectory(Path.Combine(root.FullName, "Tactra.app"));
            var runner = new CapturingProcessRunner(request =>
            {
                if (request.Arguments.Contains("install"))
                    return Success("App installed:\n• bundleID: com.evotecit.tactra\n");

                if (request.Arguments.Contains("launch"))
                    return new ProcessRunResult(
                        1,
                        string.Empty,
                        """
                        ERROR: The application failed to launch. (com.apple.dt.CoreDeviceError error 10002 (0x2712))
                               BundleIdentifier = com.evotecit.tactra
                                   The request was denied by service delegate (SBMainWorkspace) for reason: Locked ("Unable to launch com.evotecit.tactra because the device was not, or could not be, unlocked").
                                   BSErrorCodeDescription = Locked
                        """,
                        "xcrun-test",
                        TimeSpan.FromMilliseconds(1),
                        timedOut: false);

                return Success("ok");
            });
            var service = new AppleDeviceDeploymentService(runner);

            var result = await service.DeployAsync(new AppleAppDeviceDeploymentRequest
            {
                ProjectPath = project.FullName,
                Scheme = "Tactra",
                AppPath = app.FullName,
                DeviceIdentifier = "device-1",
                BundleIdentifier = "com.evotecit.tactra",
                Launch = true,
                XcodeBuildExecutable = "xcodebuild-test",
                XcrunExecutable = "xcrun-test"
            });

            Assert.True(result.Succeeded);
            Assert.NotNull(result.Install);
            Assert.True(result.Install.Succeeded);
            Assert.NotNull(result.Launch);
            Assert.False(result.Launch.Succeeded);
            Assert.True(result.Launch.DeviceLocked);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static ProcessRunResult Success(string stdOut)
        => new(0, stdOut, string.Empty, "tool", TimeSpan.FromMilliseconds(1), false);

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
