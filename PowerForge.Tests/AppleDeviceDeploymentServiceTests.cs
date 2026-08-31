using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class AppleDeviceDeploymentServiceTests
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
            var derived = ExternalOutputPath(root, "DerivedData");
            var runner = new CapturingProcessRunner(_ => Success("ok"));
            var service = new AppleDeviceDeploymentService(runner);
            InitializeGitRepository(root.FullName);
            var revision = AppleBuildProvenance.RequireLocalSourceRevision(
                root.FullName);

            var result = await service.BuildAsync(new AppleAppBuildRequest
            {
                ProjectPath = project.FullName,
                Scheme = "Tactra",
                DeviceIdentifier = "3DA86114-A96C-5109-970A-B52EA186B0E9",
                DerivedDataPath = derived,
                XcodeBuildExecutable = "/usr/bin/xcodebuild"
            });

            Assert.True(result.Succeeded);
            Assert.Equal($"id=3DA86114-A96C-5109-970A-B52EA186B0E9", result.Destination);
            Assert.Equal(Path.Combine(result.DerivedDataPath, "Build", "Products", "Debug-iphoneos", "Tactra.app"), result.AppPath);
            Assert.Single(runner.Requests);
            var request = runner.Requests[0];
            Assert.Equal("/usr/bin/xcodebuild", request.FileName);
            Assert.False(request.InheritEnvironment);
            Assert.Equal("/usr/bin:/bin:/usr/sbin:/sbin", request.EnvironmentVariables?["PATH"]);
            Assert.DoesNotContain("DEVELOPER_DIR", request.EnvironmentVariables!.Keys);
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
                result.DerivedDataPath,
                "-allowProvisioningUpdates",
                "build",
                $"POWERFORGE_SOURCE_REVISION={revision}"
            }, request.Arguments);
        }
        finally
        {
            DeleteExternalOutputs(root);
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task BuildAsync_rejects_a_non_git_source_before_xcodebuild()
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
            var runner = new CapturingProcessRunner(_ => Success("unexpected"));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => new AppleDeviceDeploymentService(runner).BuildAsync(
                    new AppleAppBuildRequest
                    {
                        ProjectPath = project.FullName,
                        Scheme = "CasaRay",
                        DeviceIdentifier = "device-1",
                        DerivedDataPath = Path.Combine(
                            Path.GetTempPath(),
                            "PowerForge.Tests.DerivedData",
                            Guid.NewGuid().ToString("N")),
                        XcodeBuildExecutable = "/usr/bin/xcodebuild",
                        BuildRoot = root.FullName
                    }));

            Assert.Contains("provenance is required", exception.Message);
            Assert.Empty(runner.Requests);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task BuildAsync_rejects_a_project_outside_the_declared_build_root_before_tools_run()
    {
        var projectRoot = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(), "PowerForge.Tests.Project", Guid.NewGuid().ToString("N")));
        var buildRoot = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(), "PowerForge.Tests.BuildRoot", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "CasaRay.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            File.WriteAllText(Path.Combine(buildRoot.FullName, "tracked.txt"), "fixture");
            InitializeGitRepository(buildRoot.FullName);
            var runner = new CapturingProcessRunner(_ => Success("unexpected"));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleDeviceDeploymentService(runner).BuildAsync(new AppleAppBuildRequest
                {
                    ProjectPath = project.FullName,
                    BuildRoot = buildRoot.FullName,
                    Scheme = "CasaRay",
                    Destination = "id=device-1",
                    UseBuildMirror = true
                }));

            Assert.Contains("must be contained", exception.Message, StringComparison.Ordinal);
            Assert.Empty(runner.Requests);
        }
        finally
        {
            try { projectRoot.Delete(recursive: true); } catch { /* best effort */ }
            try { buildRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task BuildAsync_preserves_declared_root_containment_inside_one_repository()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests.SharedRepository",
            Guid.NewGuid().ToString("N")));
        try
        {
            var declaredRoot = Directory.CreateDirectory(Path.Combine(
                root.FullName,
                "Declared"));
            var project = Directory.CreateDirectory(Path.Combine(
                root.FullName,
                "Elsewhere",
                "CasaRay.xcodeproj"));
            File.WriteAllText(
                Path.Combine(project.FullName, "project.pbxproj"),
                string.Empty);
            InitializeGitRepository(root.FullName);
            var runner = new CapturingProcessRunner(_ => Success("unexpected"));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleDeviceDeploymentService(runner).BuildAsync(
                    new AppleAppBuildRequest
                    {
                        ProjectPath = project.FullName,
                        BuildRoot = declaredRoot.FullName,
                        Scheme = "CasaRay",
                        Destination = "id=device-1"
                    }));

            Assert.Contains("must be contained", exception.Message, StringComparison.Ordinal);
            Assert.Empty(runner.Requests);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task BuildAsync_rejects_a_project_reached_through_a_symbolic_link()
    {
        if (Path.DirectorySeparatorChar == '\\')
            return;

        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(), "PowerForge.Tests.BuildRoot", Guid.NewGuid().ToString("N")));
        var external = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(), "PowerForge.Tests.ExternalProject", Guid.NewGuid().ToString("N")));
        try
        {
            var externalProject = Directory.CreateDirectory(Path.Combine(external.FullName, "CasaRay.xcodeproj"));
            File.WriteAllText(Path.Combine(externalProject.FullName, "project.pbxproj"), string.Empty);
            var linkedProject = Path.Combine(root.FullName, "CasaRay.xcodeproj");
            Directory.CreateSymbolicLink(linkedProject, externalProject.FullName);
            File.WriteAllText(Path.Combine(root.FullName, "tracked.txt"), "fixture");
            InitializeGitRepository(root.FullName);
            var runner = new CapturingProcessRunner(_ => Success("unexpected"));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleDeviceDeploymentService(runner).BuildAsync(new AppleAppBuildRequest
                {
                    ProjectPath = linkedProject,
                    BuildRoot = root.FullName,
                    Scheme = "CasaRay",
                    Destination = "id=device-1",
                    UseBuildMirror = true
                }));

            Assert.Contains("symbolic link", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(runner.Requests);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
            try { external.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task BuildAsync_rejects_a_tracked_build_input_symlink_that_escapes_the_source_root()
    {
        if (Path.DirectorySeparatorChar == '\\')
            return;

        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(), "PowerForge.Tests.BuildRoot", Guid.NewGuid().ToString("N")));
        var external = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(), "PowerForge.Tests.ExternalInput", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "CasaRay.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            var externalInput = Path.Combine(external.FullName, "Local.xcconfig");
            File.WriteAllText(externalInput, "SETTING = external");
            File.CreateSymbolicLink(Path.Combine(root.FullName, "Local.xcconfig"), externalInput);
            InitializeGitRepository(root.FullName);
            var runner = new CapturingProcessRunner(_ => Success("unexpected"));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleDeviceDeploymentService(runner).BuildAsync(new AppleAppBuildRequest
                {
                    ProjectPath = project.FullName,
                    BuildRoot = root.FullName,
                    Scheme = "CasaRay",
                    Destination = "id=device-1",
                    UseBuildMirror = true
                }));

            Assert.Contains("Local.xcconfig", exception.Message, StringComparison.Ordinal);
            Assert.Contains("symbolic link", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(runner.Requests);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
            try { external.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task BuildAsync_rejects_ignored_build_inputs_before_rsync_or_xcodebuild()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "CasaRay.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            File.WriteAllText(Path.Combine(root.FullName, ".gitignore"), "Local.xcconfig\n");
            InitializeGitRepository(root.FullName);
            File.WriteAllText(Path.Combine(root.FullName, "Local.xcconfig"), "SETTING = local");
            var runner = new CapturingProcessRunner(_ => Success("unexpected"));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleDeviceDeploymentService(runner).BuildAsync(new AppleAppBuildRequest
                {
                    ProjectPath = project.FullName,
                    BuildRoot = root.FullName,
                    Scheme = "CasaRay",
                    Destination = "id=device-1",
                    UseBuildMirror = true
                }));

            Assert.Contains("Local.xcconfig", exception.Message, StringComparison.Ordinal);
            Assert.Empty(runner.Requests);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task BuildAsync_defaults_provenance_to_the_repository_top_level()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests.RepositoryRoot",
            Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(
                root.FullName,
                "Apps",
                "CasaRay.xcodeproj"));
            File.WriteAllText(
                Path.Combine(project.FullName, "project.pbxproj"),
                string.Empty);
            File.WriteAllText(
                Path.Combine(root.FullName, ".gitignore"),
                "Local.xcconfig\n");
            InitializeGitRepository(root.FullName);
            File.WriteAllText(
                Path.Combine(root.FullName, "Local.xcconfig"),
                "SETTING = repository-local");
            var runner = new CapturingProcessRunner(_ => Success("unexpected"));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleDeviceDeploymentService(runner).BuildAsync(
                    new AppleAppBuildRequest
                    {
                        ProjectPath = project.FullName,
                        Scheme = "CasaRay",
                        Destination = "id=device-1",
                        DerivedDataPath = ExternalOutputPath(root, "DerivedData")
                    }));

            Assert.Contains("Local.xcconfig", exception.Message, StringComparison.Ordinal);
            Assert.Empty(runner.Requests);
        }
        finally
        {
            DeleteExternalOutputs(root);
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task BuildAsync_rejects_absolute_xcode_inputs_outside_the_source()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            Guid.NewGuid().ToString("N")));
        var external = Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests.External",
            Guid.NewGuid().ToString("N") + ".xcconfig");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(external)!);
            File.WriteAllText(external, "SETTING = outside");
            var project = Directory.CreateDirectory(Path.Combine(
                root.FullName,
                "CasaRay.xcodeproj"));
            File.WriteAllText(
                Path.Combine(project.FullName, "project.pbxproj"),
                $$"""
                {
                    objects = {
                        AA0000000000000000000001 = {
                            isa = PBXFileReference;
                            path = "{{external}}";
                            sourceTree = "<absolute>";
                        };
                    };
                }
                """);
            InitializeGitRepository(root.FullName);
            var runner = new CapturingProcessRunner(_ => Success("unexpected"));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => new AppleDeviceDeploymentService(runner).BuildAsync(
                    new AppleAppBuildRequest
                    {
                        ProjectPath = project.FullName,
                        Scheme = "CasaRay",
                        Destination = "id=device-1",
                        DerivedDataPath = ExternalOutputPath(root, "DerivedData")
                    }));

            Assert.Contains(
                "Absolute Xcode project inputs",
                exception.Message,
                StringComparison.Ordinal);
            Assert.Empty(runner.Requests);
        }
        finally
        {
            DeleteExternalOutputs(root);
            try { root.Delete(recursive: true); } catch { /* best effort */ }
            try { File.Delete(external); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task BuildAsync_rejects_ignored_generated_inputs_without_a_build_mirror()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "CasaRay.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            File.WriteAllText(Path.Combine(root.FullName, ".gitignore"), ".build/\n");
            InitializeGitRepository(root.FullName);
            Directory.CreateDirectory(Path.Combine(root.FullName, ".build"));
            File.WriteAllText(
                Path.Combine(root.FullName, ".build", "generated.xcconfig"),
                "SETTING = local");
            var runner = new CapturingProcessRunner(_ => Success("unexpected"));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleDeviceDeploymentService(runner).BuildAsync(new AppleAppBuildRequest
                {
                    ProjectPath = project.FullName,
                    BuildRoot = root.FullName,
                    Scheme = "CasaRay",
                    Destination = "id=device-1",
                    DerivedDataPath = Path.Combine(
                        Path.GetTempPath(),
                        "PowerForge.Tests.DerivedData",
                        Guid.NewGuid().ToString("N"))
                }));

            Assert.Contains(".build/generated.xcconfig", exception.Message, StringComparison.Ordinal);
            Assert.Empty(runner.Requests);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BuildAsync_rejects_output_paths_that_overlap_the_build_root(bool useDerivedDataPath)
    {
        var parent = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(), "PowerForge.Tests.OutputRoot", Guid.NewGuid().ToString("N")));
        var root = Directory.CreateDirectory(Path.Combine(parent.FullName, "source"));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "CasaRay.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            InitializeGitRepository(root.FullName);
            var runner = new CapturingProcessRunner(_ => Success("unexpected"));
            var safeDerivedDataPath = Path.Combine(
                Path.GetTempPath(),
                "PowerForge.Tests.DerivedData",
                Guid.NewGuid().ToString("N"));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleDeviceDeploymentService(runner).BuildAsync(new AppleAppBuildRequest
                {
                    ProjectPath = project.FullName,
                    BuildRoot = root.FullName,
                    Scheme = "CasaRay",
                    Destination = "id=device-1",
                    DerivedDataPath = useDerivedDataPath ? parent.FullName : safeDerivedDataPath,
                    AppPath = useDerivedDataPath ? null : parent.FullName
                }));

            Assert.Contains(
                useDerivedDataPath ? nameof(AppleAppBuildRequest.DerivedDataPath) : nameof(AppleAppBuildRequest.AppPath),
                exception.Message,
                StringComparison.Ordinal);
            Assert.Contains("must be outside BuildRoot", exception.Message, StringComparison.Ordinal);
            Assert.Empty(runner.Requests);
        }
        finally
        {
            try { parent.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task BuildAsync_rejects_derived_data_inside_the_build_root()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(), "PowerForge.Tests.OutputRoot", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "CasaRay.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            InitializeGitRepository(root.FullName);
            var runner = new CapturingProcessRunner(_ => Success("unexpected"));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleDeviceDeploymentService(runner).BuildAsync(new AppleAppBuildRequest
                {
                    ProjectPath = project.FullName,
                    BuildRoot = root.FullName,
                    Scheme = "CasaRay",
                    Destination = "id=device-1",
                    DerivedDataPath = Path.Combine(root.FullName, "DerivedData")
                }));

            Assert.Contains(nameof(AppleAppBuildRequest.DerivedDataPath), exception.Message, StringComparison.Ordinal);
            Assert.Contains("must be outside BuildRoot", exception.Message, StringComparison.Ordinal);
            Assert.Empty(runner.Requests);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task BuildAsync_uses_volume_case_rules_for_build_root_containment()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(), "PowerForge.Tests.CaseRoot", Guid.NewGuid().ToString("N") + "Aa"));
        try
        {
            if (FrameworkCompatibility.GetPathStringComparisonForPath(root.FullName) !=
                StringComparison.OrdinalIgnoreCase)
            {
                return;
            }

            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "CasaRay.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            InitializeGitRepository(root.FullName);
            var runner = new CapturingProcessRunner(_ => Success("ok"));
            var differentlyCasedRoot = ToggleFirstLetterCase(root.FullName);

            var result = await new AppleDeviceDeploymentService(runner).BuildAsync(
                new AppleAppBuildRequest
                {
                    ProjectPath = project.FullName,
                    BuildRoot = differentlyCasedRoot,
                    Scheme = "CasaRay",
                    Destination = "id=device-1",
                    DerivedDataPath = ExternalOutputPath(root, "DerivedData")
                });

            Assert.True(result.Succeeded);
            Assert.Single(runner.Requests);
        }
        finally
        {
            DeleteExternalOutputs(root);
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task BuildAsync_binds_source_revision_without_allowing_an_argument_override()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "CasaRay.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            File.WriteAllText(
                Path.Combine(root.FullName, ".gitignore"),
                "DerivedData/\nOtherDerivedData/\n");
            InitializeGitRepository(root.FullName);
            var runner = new CapturingProcessRunner(_ => Success("ok"));
            var service = new AppleDeviceDeploymentService(runner);
            var revision = AppleBuildProvenance.RequireLocalSourceRevision(
                root.FullName);

            var result = await service.BuildAsync(new AppleAppBuildRequest
            {
                ProjectPath = project.FullName,
                Scheme = "CasaRay",
                DeviceIdentifier = "3DA86114-A96C-5109-970A-B52EA186B0E9",
                DerivedDataPath = ExternalOutputPath(root, "DerivedData"),
                XcodeBuildExecutable = "/usr/bin/xcodebuild",
                BuildRoot = root.FullName
            });

            Assert.True(result.Succeeded);
            Assert.Contains($"POWERFORGE_SOURCE_REVISION={revision}", runner.Requests[0].Arguments);
            Assert.Equal(revision, result.SourceRevision);

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.BuildAsync(new AppleAppBuildRequest
            {
                ProjectPath = project.FullName,
                Scheme = "CasaRay",
                DeviceIdentifier = "3DA86114-A96C-5109-970A-B52EA186B0E9",
                DerivedDataPath = ExternalOutputPath(root, "OtherDerivedData"),
                XcodeBuildExecutable = "/usr/bin/xcodebuild",
                BuildRoot = root.FullName,
                AdditionalArguments = [$"POWERFORGE_SOURCE_REVISION={new string('b', 40)}"]
            }));
        }
        finally
        {
            DeleteExternalOutputs(root);
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task BuildAsync_rejects_a_clean_source_that_changes_during_xcodebuild()
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
            var projectFile = Path.Combine(project.FullName, "project.pbxproj");
            File.WriteAllText(projectFile, "clean");
            File.WriteAllText(
                Path.Combine(root.FullName, ".gitignore"),
                "DerivedData/\n");
            InitializeGitRepository(root.FullName);
            var runner = new CapturingProcessRunner(_ =>
            {
                File.WriteAllText(projectFile, "changed while building");
                return Success("ok");
            });

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => new AppleDeviceDeploymentService(runner).BuildAsync(
                    new AppleAppBuildRequest
                    {
                        ProjectPath = project.FullName,
                        Scheme = "CasaRay",
                        DeviceIdentifier = "device-1",
                        DerivedDataPath = ExternalOutputPath(root, "DerivedData"),
                        XcodeBuildExecutable = "/usr/bin/xcodebuild",
                        BuildRoot = root.FullName
                    }));

            Assert.Contains("source changed", exception.Message);
        }
        finally
        {
            DeleteExternalOutputs(root);
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task BuildAsync_rejects_a_transient_source_write_restored_during_xcodebuild()
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
            var projectFile = Path.Combine(project.FullName, "project.pbxproj");
            File.WriteAllText(projectFile, "clean");
            InitializeGitRepository(root.FullName);
            var runner = new CapturingProcessRunner(_ =>
            {
                File.WriteAllText(projectFile, "transient");
                File.WriteAllText(projectFile, "clean");
                return Success("ok");
            });

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => new AppleDeviceDeploymentService(runner).BuildAsync(
                    new AppleAppBuildRequest
                    {
                        ProjectPath = project.FullName,
                        Scheme = "CasaRay",
                        DeviceIdentifier = "device-1",
                        DerivedDataPath = Path.Combine(
                            Path.GetTempPath(),
                            "PowerForge.Tests.DerivedData",
                            Guid.NewGuid().ToString("N")),
                        XcodeBuildExecutable = "/usr/bin/xcodebuild",
                        BuildRoot = root.FullName
                    }));

            Assert.Contains("changed", exception.Message);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task BuildAsync_rejects_a_transient_mirror_write_during_xcodebuild()
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
            var project = Directory.CreateDirectory(Path.Combine(
                root.FullName,
                "CasaRay.xcodeproj"));
            File.WriteAllText(
                Path.Combine(project.FullName, "project.pbxproj"),
                "clean");
            InitializeGitRepository(root.FullName);
            var mirror = Path.Combine(mirrorRoot.FullName, "mirror");
            var processIndex = 0;
            var runner = new CapturingProcessRunner(_ =>
            {
                processIndex++;
                if (processIndex == 2)
                {
                    var transientPath = Path.Combine(mirror, "transient");
                    File.WriteAllText(transientPath, "unapproved");
                    File.Delete(transientPath);
                }
                return Success("ok");
            });

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => new AppleDeviceDeploymentService(runner).BuildAsync(
                    new AppleAppBuildRequest
                    {
                        ProjectPath = project.FullName,
                        Scheme = "CasaRay",
                        Destination = "id=device-1",
                        DerivedDataPath = Path.Combine(
                            Path.GetTempPath(),
                            "PowerForge.Tests.DerivedData",
                            Guid.NewGuid().ToString("N")),
                        UseBuildMirror = true,
                        BuildRoot = root.FullName,
                        BuildMirrorPath = mirror,
                        RsyncExecutable = "/usr/bin/rsync",
                        XcodeBuildExecutable = "/usr/bin/xcodebuild"
                    }));

            Assert.Contains("changed", exception.Message);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
            try { mirrorRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task BuildAsync_keeps_live_source_monitored_during_mirrored_xcodebuild()
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
            var project = Directory.CreateDirectory(Path.Combine(
                root.FullName,
                "CasaRay.xcodeproj"));
            var projectFile = Path.Combine(project.FullName, "project.pbxproj");
            File.WriteAllText(projectFile, "clean");
            InitializeGitRepository(root.FullName);
            var processIndex = 0;
            var runner = new CapturingProcessRunner(_ =>
            {
                processIndex++;
                if (processIndex == 2)
                {
                    File.WriteAllText(projectFile, "transient");
                    File.WriteAllText(projectFile, "clean");
                }
                return Success("ok");
            });

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => new AppleDeviceDeploymentService(runner).BuildAsync(
                    new AppleAppBuildRequest
                    {
                        ProjectPath = project.FullName,
                        Scheme = "CasaRay",
                        Destination = "id=device-1",
                        DerivedDataPath = ExternalOutputPath(root, "DerivedData"),
                        UseBuildMirror = true,
                        BuildRoot = root.FullName,
                        BuildMirrorPath = Path.Combine(mirrorRoot.FullName, "mirror"),
                        RsyncExecutable = "/usr/bin/rsync",
                        XcodeBuildExecutable = "/usr/bin/xcodebuild"
                    }));

            Assert.Contains("source changed", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteExternalOutputs(root);
            try { root.Delete(recursive: true); } catch { /* best effort */ }
            try { mirrorRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task BuildAsync_rejects_a_mirror_alias_to_the_source_before_rsync()
    {
        if (Path.DirectorySeparatorChar == '\\')
            return;

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
            var project = Directory.CreateDirectory(Path.Combine(
                root.FullName,
                "CasaRay.xcodeproj"));
            File.WriteAllText(
                Path.Combine(project.FullName, "project.pbxproj"),
                "clean");
            InitializeGitRepository(root.FullName);
            var mirror = Path.Combine(mirrorRoot.FullName, "mirror");
            Directory.CreateSymbolicLink(mirror, root.FullName);
            var runner = new CapturingProcessRunner(_ => Success("unexpected"));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => new AppleDeviceDeploymentService(runner).BuildAsync(
                    new AppleAppBuildRequest
                    {
                        ProjectPath = project.FullName,
                        Scheme = "CasaRay",
                        Destination = "id=device-1",
                        DerivedDataPath = ExternalOutputPath(root, "DerivedData"),
                        UseBuildMirror = true,
                        BuildRoot = root.FullName,
                        BuildMirrorPath = mirror,
                        RsyncExecutable = "/usr/bin/rsync",
                        XcodeBuildExecutable = "/usr/bin/xcodebuild"
                    }));

            Assert.Contains("physically overlap", exception.Message, StringComparison.Ordinal);
            Assert.True(Directory.Exists(Path.Combine(root.FullName, ".git")));
            Assert.Empty(runner.Requests);
        }
        finally
        {
            DeleteExternalOutputs(root);
            try { root.Delete(recursive: true); } catch { /* best effort */ }
            try { mirrorRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task BuildAsync_discovers_single_app_when_product_name_differs_from_scheme()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "Tactra.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            var derived = ExternalOutputPath(root, "DerivedData");
            var service = new AppleDeviceDeploymentService(
                new AlternativeProductRunner("Debug-maccatalyst", "Tactra.app"));
            InitializeGitRepository(root.FullName);

            var result = await service.BuildAsync(new AppleAppBuildRequest
            {
                ProjectPath = project.FullName,
                Scheme = "TactraMac",
                Platform = ApplePlatform.macOS,
                ArchiveVariant = AppleArchiveVariant.MacCatalyst,
                DerivedDataPath = derived,
                XcodeBuildExecutable = "/usr/bin/xcodebuild"
            });

            Assert.True(result.Succeeded);
            Assert.Equal(
                Path.Combine(
                    result.DerivedDataPath,
                    "Build",
                    "Products",
                    "Debug-maccatalyst",
                    "Tactra.app"),
                result.AppPath);
        }
        finally
        {
            DeleteExternalOutputs(root);
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
            var derived = ExternalOutputPath(root, "DerivedData");
            var runner = new CapturingProcessRunner(_ => Success("ok"));
            var service = new AppleDeviceDeploymentService(runner);
            InitializeGitRepository(root.FullName);

            var result = await service.BuildAsync(new AppleAppBuildRequest
            {
                ProjectPath = project.FullName,
                Scheme = "Tactra",
                Platform = ApplePlatform.macOS,
                Destination = "platform=macOS",
                DerivedDataPath = derived,
                XcodeBuildExecutable = "/usr/bin/xcodebuild"
            });

            Assert.True(result.Succeeded);
            Assert.Equal(Path.Combine(result.DerivedDataPath, "Build", "Products", "Debug", "Tactra.app"), result.AppPath);
        }
        finally
        {
            DeleteExternalOutputs(root);
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task BuildAsync_uses_maccatalyst_destination_and_product_directory()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "CasaRay.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            var derived = ExternalOutputPath(root, "DerivedData");
            var runner = new CapturingProcessRunner(_ => Success("ok"));
            var service = new AppleDeviceDeploymentService(runner);
            InitializeGitRepository(root.FullName);

            var result = await service.BuildAsync(new AppleAppBuildRequest
            {
                ProjectPath = project.FullName,
                Scheme = "CasaRay",
                Platform = ApplePlatform.macOS,
                ArchiveVariant = AppleArchiveVariant.MacCatalyst,
                DerivedDataPath = derived,
                XcodeBuildExecutable = "/usr/bin/xcodebuild"
            });

            Assert.True(result.Succeeded);
            Assert.Equal("generic/platform=macOS,variant=Mac Catalyst", result.Destination);
            Assert.Equal(Path.Combine(result.DerivedDataPath, "Build", "Products", "Debug-maccatalyst", "CasaRay.app"), result.AppPath);
        }
        finally
        {
            DeleteExternalOutputs(root);
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
            var derived = ExternalOutputPath(root, "DerivedData");
            var runner = new CapturingProcessRunner(_ => Success("ok"));
            var service = new AppleDeviceDeploymentService(runner);
            InitializeGitRepository(root.FullName);

            var result = await service.BuildAsync(new AppleAppBuildRequest
            {
                ProjectPath = project.FullName,
                Scheme = "Tactra",
                Destination = "id=device-1",
                DerivedDataPath = derived,
                UseBuildMirror = true,
                BuildRoot = root.FullName,
                BuildMirrorPath = mirror,
                RsyncExecutable = "/usr/bin/rsync",
                XcodeBuildExecutable = "/usr/bin/xcodebuild"
            });

            Assert.True(result.Succeeded);
            var physicalMirror = AppleReleaseArtifactService.ResolvePhysicalPath(
                mirror);
            Assert.Equal(physicalMirror, result.BuildMirrorPath);
            Assert.Equal(2, runner.Requests.Count);
            Assert.Equal("/usr/bin/rsync", runner.Requests[0].FileName);
            Assert.False(runner.Requests[0].InheritEnvironment);
            Assert.Contains("--delete", runner.Requests[0].Arguments);
            Assert.Contains("--delete-excluded", runner.Requests[0].Arguments);
            Assert.Contains("/.git", runner.Requests[0].Arguments);
            Assert.Contains("/.build", runner.Requests[0].Arguments);
            Assert.Contains("/.swiftpm", runner.Requests[0].Arguments);
            Assert.Contains("/build", runner.Requests[0].Arguments);
            Assert.Contains("/DerivedData", runner.Requests[0].Arguments);
            Assert.DoesNotContain(".build", runner.Requests[0].Arguments);
            Assert.DoesNotContain(".git", runner.Requests[0].Arguments);
            Assert.DoesNotContain("build", runner.Requests[0].Arguments);
            Assert.Contains(root.FullName + Path.DirectorySeparatorChar, runner.Requests[0].Arguments);
            Assert.Contains(physicalMirror + Path.DirectorySeparatorChar, runner.Requests[0].Arguments);

            var buildRequest = runner.Requests[1];
            Assert.Equal("/usr/bin/xcodebuild", buildRequest.FileName);
            Assert.False(buildRequest.InheritEnvironment);
            Assert.Equal(physicalMirror, buildRequest.WorkingDirectory);
            Assert.Contains(Path.Combine(physicalMirror, "Tactra.xcodeproj"), buildRequest.Arguments);
        }
        finally
        {
            DeleteExternalOutputs(root);
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
            var app = Directory.CreateDirectory(ExternalOutputPath(root, "Tactra.app"));
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
            DeleteExternalOutputs(root);
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
            var derived = ExternalOutputPath(root, "DerivedData");
            var app = Directory.CreateDirectory(Path.Combine(
                derived, "Build", "Products", "Debug-iphoneos", "Tactra.app"));
            var runner = new CapturingProcessRunner(request =>
            {
                if (request.Arguments.Contains("install"))
                    return Success("App installed:\n• bundleID: com.evotecit.tactra\n");

                return Success("ok");
            });
            var service = new AppleDeviceDeploymentService(runner);
            InitializeGitRepository(root.FullName);

            var result = await service.DeployAsync(new AppleAppDeviceDeploymentRequest
            {
                ProjectPath = project.FullName,
                Scheme = "Tactra",
                DerivedDataPath = derived,
                DeviceIdentifier = "device-1",
                BundleIdentifier = "com.evotecit.tactra",
                Launch = true,
                XcodeBuildExecutable = "/usr/bin/xcodebuild",
                XcrunExecutable = "/usr/bin/xcrun"
            });

            Assert.True(result.Succeeded);
            Assert.NotNull(result.Install);
            Assert.NotNull(result.Launch);
            Assert.Equal(3, runner.Requests.Count);
            Assert.Equal("/usr/bin/xcodebuild", runner.Requests[0].FileName);
            Assert.All(runner.Requests, request => Assert.False(request.InheritEnvironment));
            Assert.All(
                runner.Requests,
                request => Assert.DoesNotContain("DEVELOPER_DIR", request.EnvironmentVariables!.Keys));
            Assert.Equal(
                new[] { "devicectl", "device", "install", "app", "--device", "device-1" },
                runner.Requests[1].Arguments.Take(6));
            Assert.EndsWith("Tactra.app", runner.Requests[1].Arguments[6], StringComparison.Ordinal);
            Assert.NotEqual(app.FullName, runner.Requests[1].Arguments[6]);
            Assert.True(Directory.Exists(result.Build.AppPath));
            Assert.StartsWith(
                Path.Combine(
                    AppleReleaseArtifactService.ResolvePhysicalPath(derived),
                    "PowerForge",
                    "DeploymentProducts"),
                result.Build.AppPath,
                StringComparison.Ordinal);
            Assert.Equal(result.Build.AppPath, result.Install!.AppPath);
            var privateDerivedDataRoot = ReadArgumentValue(
                runner.Requests[0],
                "-derivedDataPath");
            Assert.False(Directory.Exists(privateDerivedDataRoot));
            var privateProductRoot = runner.Requests[0].Arguments.Single(argument =>
                    argument.StartsWith("CONFIGURATION_BUILD_DIR=", StringComparison.Ordinal))
                .Substring("CONFIGURATION_BUILD_DIR=".Length);
            Assert.False(Directory.Exists(privateProductRoot));
            Assert.Equal(new[] { "devicectl", "device", "process", "launch", "--device", "device-1", "com.evotecit.tactra" }, runner.Requests[2].Arguments);
        }
        finally
        {
            DeleteExternalOutputs(root);
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
            var derived = ExternalOutputPath(root, "DerivedData");
            var app = Directory.CreateDirectory(Path.Combine(
                derived, "Build", "Products", "Debug-iphoneos", "Tactra.app"));
            var runner = new CapturingProcessRunner(request =>
            {
                if (request.Arguments.Contains("install"))
                    return Success("App installed:\n• bundleID: com.evotecit.tactra\n");

                return Success("ok");
            });
            var service = new AppleDeviceDeploymentService(runner);
            InitializeGitRepository(root.FullName);

            var result = await service.DeployAsync(new AppleAppDeviceDeploymentRequest
            {
                ProjectPath = project.FullName,
                Scheme = "Tactra",
                DerivedDataPath = derived,
                Destination = "id=device-1",
                BundleIdentifier = "com.evotecit.tactra",
                Launch = true,
                XcodeBuildExecutable = "/usr/bin/xcodebuild",
                XcrunExecutable = "/usr/bin/xcrun"
            });

            Assert.True(result.Succeeded);
            Assert.Equal(
                new[] { "devicectl", "device", "install", "app", "--device", "device-1" },
                runner.Requests[1].Arguments.Take(6));
            Assert.EndsWith("Tactra.app", runner.Requests[1].Arguments[6], StringComparison.Ordinal);
            Assert.NotEqual(app.FullName, runner.Requests[1].Arguments[6]);
            Assert.Equal(new[] { "devicectl", "device", "process", "launch", "--device", "device-1", "com.evotecit.tactra" }, runner.Requests[2].Arguments);
        }
        finally
        {
            DeleteExternalOutputs(root);
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task DeployAsync_passes_profile_environment_and_restarts_existing_app()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "CasaRay.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            var derived = ExternalOutputPath(root, "DerivedData");
            _ = Directory.CreateDirectory(Path.Combine(
                derived, "Build", "Products", "Debug-iphoneos", "CasaRay.app"));
            var runner = new CapturingProcessRunner(request => request.Arguments.Contains("install")
                ? Success("App installed:\n• bundleID: com.evotecit.casaray\n")
                : Success("ok"));
            var service = new AppleDeviceDeploymentService(runner);
            InitializeGitRepository(root.FullName);

            var result = await service.DeployAsync(new AppleAppDeviceDeploymentRequest
            {
                ProjectPath = project.FullName,
                Scheme = "CasaRay",
                DerivedDataPath = derived,
                DeviceIdentifier = "device-1",
                BundleIdentifier = "com.evotecit.casaray",
                Launch = true,
                LaunchEnvironment = new Dictionary<string, string>
                {
                    ["CASARAY_ENABLE_SANDBOX_PURCHASES"] = "1"
                },
                LaunchArguments = new[] { "--sample" },
                TerminateExisting = true,
                XcodeBuildExecutable = "/usr/bin/xcodebuild",
                XcrunExecutable = "/usr/bin/xcrun"
            });

            Assert.True(result.Succeeded);
            var launch = runner.Requests[2].Arguments;
            Assert.Equal("--environment-variables", launch[6]);
            Assert.Equal("{\"CASARAY_ENABLE_SANDBOX_PURCHASES\":\"1\"}", launch[7]);
            Assert.Equal("--terminate-existing", launch[8]);
            Assert.Equal("com.evotecit.casaray", launch[9]);
            Assert.Equal("--sample", launch[10]);
        }
        finally
        {
            DeleteExternalOutputs(root);
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task DeployAsync_preserves_install_success_but_marks_locked_launch_incomplete()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "Tactra.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            var derived = ExternalOutputPath(root, "DerivedData");
            _ = Directory.CreateDirectory(Path.Combine(
                derived, "Build", "Products", "Debug-iphoneos", "Tactra.app"));
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
                        "/usr/bin/xcrun",
                        TimeSpan.FromMilliseconds(1),
                        timedOut: false);

                return Success("ok");
            });
            var service = new AppleDeviceDeploymentService(runner);
            InitializeGitRepository(root.FullName);

            var result = await service.DeployAsync(new AppleAppDeviceDeploymentRequest
            {
                ProjectPath = project.FullName,
                Scheme = "Tactra",
                DerivedDataPath = derived,
                DeviceIdentifier = "device-1",
                BundleIdentifier = "com.evotecit.tactra",
                Launch = true,
                XcodeBuildExecutable = "/usr/bin/xcodebuild",
                XcrunExecutable = "/usr/bin/xcrun"
            });

            Assert.True(result.Succeeded);
            Assert.False(result.RequestedStagesSucceeded);
            Assert.NotNull(result.Install);
            Assert.True(result.Install.Succeeded);
            Assert.NotNull(result.Launch);
            Assert.False(result.Launch.Succeeded);
            Assert.True(result.Launch.DeviceLocked);
        }
        finally
        {
            DeleteExternalOutputs(root);
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static ProcessRunResult Success(string stdOut)
        => new(0, stdOut, string.Empty, "tool", TimeSpan.FromMilliseconds(1), false);

    private static string ExternalOutputPath(DirectoryInfo sourceRoot, string leaf)
        => Path.Combine(
            sourceRoot.Parent!.FullName,
            sourceRoot.Name + ".outputs",
            leaf);

    private static string ToggleFirstLetterCase(string path)
    {
        var parent = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Test path must have a parent directory.");
        var name = Path.GetFileName(path);
        var first = char.IsUpper(name[0])
            ? char.ToLowerInvariant(name[0])
            : char.ToUpperInvariant(name[0]);
        return Path.Combine(parent, first + name.Substring(1));
    }

    private static void DeleteExternalOutputs(DirectoryInfo sourceRoot)
    {
        var outputRoot = Path.Combine(
            sourceRoot.Parent!.FullName,
            sourceRoot.Name + ".outputs");
        try
        {
            if (Directory.Exists(outputRoot))
                Directory.Delete(outputRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for test outputs.
        }
    }

    private static void InitializeGitRepository(string workingDirectory)
    {
        WriteSharedSchemes(workingDirectory);
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

    private static void WriteSharedSchemes(string workingDirectory)
        => AppleDeploymentTestFixture.WriteSharedSchemes(
            workingDirectory,
            "CasaRay",
            "Tactra",
            "TactraMac");

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
            request.InvokePreStartBoundary();
            request.InvokeStartBoundary();
            var result = _execute(request);
            if (result.Succeeded)
                AppleDeploymentTestFixture.MaterializeConfiguredBuildProduct(request);
            request.InvokeCompletionBoundary(result);
            return Task.FromResult(result);
        }
    }
}
