namespace PowerForge.Tests;

public sealed partial class AppleDeviceDeploymentServiceTests
{
    [Fact]
    public async Task DeployAsync_builds_from_a_private_materialized_package_checkout()
    {
        var fixture = CreateLocalPackageBuildFixture();
        try
        {
            var runner = new LocalPackageBuildRunner(
                fixture.RemoteRoot,
                fixture.RemoteUrl,
                mutateDuringBuild: false);

            var result = await new AppleDeviceDeploymentService(runner).DeployAsync(
                new AppleAppDeviceDeploymentRequest
                {
                    ProjectPath = fixture.ProjectPath,
                    Scheme = "CasaRay",
                    ProductName = "CasaRay",
                    DerivedDataPath = fixture.DerivedDataPath,
                    DeviceIdentifier = "device-1",
                    BundleIdentifier = "com.evotecit.casaray",
                    Launch = false,
                    XcodeBuildExecutable = "/usr/bin/xcodebuild",
                    XcrunExecutable = "/usr/bin/xcrun"
                });

            Assert.True(result.Succeeded);
            Assert.Equal(3, runner.Requests.Count);
            var resolve = runner.Requests[0];
            var build = runner.Requests[1];
            Assert.Contains("-resolvePackageDependencies", resolve.Arguments);
            Assert.Contains("-clonedSourcePackagesDirPath", build.Arguments);
            Assert.Contains("-disableAutomaticPackageResolution", build.Arguments);
            Assert.Contains("-onlyUsePackageVersionsFromResolvedFile", build.Arguments);
            Assert.False(build.InheritEnvironment);
            Assert.Equal("1", build.EnvironmentVariables!["GIT_CONFIG_NOSYSTEM"]);
            Assert.Equal(
                ReadArgumentValue(resolve, "-clonedSourcePackagesDirPath"),
                ReadArgumentValue(build, "-clonedSourcePackagesDirPath"));
            var productRoot = build.Arguments.Single(argument =>
                    argument.StartsWith("CONFIGURATION_BUILD_DIR=", StringComparison.Ordinal))
                .Substring("CONFIGURATION_BUILD_DIR=".Length);
            Assert.False(Directory.Exists(productRoot));
            Assert.False(Directory.Exists(runner.SourcePackagesRoot));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task DeployAsync_rejects_a_materialized_package_mutation_during_local_build()
    {
        var fixture = CreateLocalPackageBuildFixture();
        try
        {
            var runner = new LocalPackageBuildRunner(
                fixture.RemoteRoot,
                fixture.RemoteUrl,
                mutateDuringBuild: true);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleDeviceDeploymentService(runner).DeployAsync(
                    new AppleAppDeviceDeploymentRequest
                    {
                        ProjectPath = fixture.ProjectPath,
                        Scheme = "CasaRay",
                        ProductName = "CasaRay",
                        DerivedDataPath = fixture.DerivedDataPath,
                        DeviceIdentifier = "device-1",
                        BundleIdentifier = "com.evotecit.casaray",
                        Launch = false,
                        XcodeBuildExecutable = "/usr/bin/xcodebuild",
                        XcrunExecutable = "/usr/bin/xcrun"
                    }));

            Assert.Contains("materialized Swift package root changed", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(2, runner.Requests.Count);
            Assert.False(Directory.Exists(runner.SourcePackagesRoot));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task DeployAsync_ignores_an_unrelated_repository_root_package_lock()
    {
        var fixture = CreateLocalPackageBuildFixture(
            bindPackageToSelectedProject: false);
        try
        {
            var runner = new LocalPackageBuildRunner(
                fixture.RemoteRoot,
                fixture.RemoteUrl,
                mutateDuringBuild: false);

            var result = await new AppleDeviceDeploymentService(runner).DeployAsync(
                new AppleAppDeviceDeploymentRequest
                {
                    ProjectPath = fixture.ProjectPath,
                    Scheme = "CasaRay",
                    ProductName = "CasaRay",
                    DerivedDataPath = fixture.DerivedDataPath,
                    DeviceIdentifier = "device-1",
                    BundleIdentifier = "com.evotecit.casaray",
                    Launch = false,
                    XcodeBuildExecutable = "/usr/bin/xcodebuild",
                    XcrunExecutable = "/usr/bin/xcrun"
                });

            Assert.True(result.Succeeded);
            Assert.Equal(2, runner.Requests.Count);
            Assert.DoesNotContain(
                runner.Requests,
                request => request.Arguments.Contains(
                    "-resolvePackageDependencies"));
            Assert.DoesNotContain(
                "-clonedSourcePackagesDirPath",
                runner.Requests[0].Arguments);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public void Package_snapshot_includes_a_root_lock_referenced_by_the_selected_local_package()
    {
        var fixture = CreateLocalPackageBuildFixture(
            bindPackageToSelectedProject: false);
        try
        {
            var root = Path.GetDirectoryName(fixture.ProjectPath)!;
            var revision = ReadGit(
                fixture.RemoteRoot,
                "rev-parse",
                "HEAD").Trim();
            File.WriteAllText(
                Path.Combine(fixture.ProjectPath, "project.pbxproj"),
                """
                {
                    objects = {
                        AA0000000000000000000001 = {
                            isa = XCLocalSwiftPackageReference;
                            relativePath = .;
                        };
                    };
                }
                """);
            File.WriteAllText(
                Path.Combine(root, "Package.swift"),
                $$"""
                // swift-tools-version: 6.0
                import PackageDescription
                let package = Package(
                    name: "App",
                    dependencies: [
                        .package(url: "{{fixture.RemoteUrl}}", revision: "{{revision}}")
                    ]
                )
                """);
            RunGit(root, "add", ".");
            RunGit(root, "commit", "-m", "bind local package graph");

            var approved = AppleSwiftPackageBuildSnapshot
                .ReadApprovedRemotePackages(fixture.ProjectPath);

            Assert.Equal(revision, Assert.Single(approved).Value);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    private static LocalPackageBuildFixture CreateLocalPackageBuildFixture(
        bool bindPackageToSelectedProject = true)
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests.LocalPackageBuild",
            Guid.NewGuid().ToString("N")));
        var remote = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests.RemotePackage",
            Guid.NewGuid().ToString("N")));
        try
        {
            RunGit(remote.FullName, "init");
            RunGit(remote.FullName, "config", "user.name", "PowerForge Tests");
            RunGit(remote.FullName, "config", "user.email", "powerforge-tests@example.invalid");
            File.WriteAllText(
                Path.Combine(remote.FullName, "Package.swift"),
                "// swift-tools-version: 6.0\nimport PackageDescription\nlet package = Package(name: \"Shared\")\n");
            RunGit(remote.FullName, "add", ".");
            RunGit(remote.FullName, "commit", "-m", "package fixture");
            var revision = ReadGit(remote.FullName, "rev-parse", "HEAD").Trim();
            const string remoteUrl = "https://example.invalid/Shared.git";

            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "CasaRay.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            var lockDirectory = bindPackageToSelectedProject
                ? Directory.CreateDirectory(Path.Combine(
                    project.FullName,
                    "project.xcworkspace",
                    "xcshareddata",
                    "swiftpm")).FullName
                : root.FullName;
            File.WriteAllText(
                Path.Combine(lockDirectory, "Package.resolved"),
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    pins = new[]
                    {
                        new
                        {
                            identity = "shared",
                            kind = "remoteSourceControl",
                            location = remoteUrl,
                            state = new { revision, version = "1.0.0" }
                        }
                    },
                    version = 3
                }));
            InitializeGitRepository(root.FullName);
            return new LocalPackageBuildFixture(
                root,
                remote,
                project.FullName,
                ExternalOutputPath(root, "DerivedData"),
                remoteUrl);
        }
        catch
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
            try { remote.Delete(recursive: true); } catch { /* best effort */ }
            throw;
        }
    }

    private static string ReadGit(string workingDirectory, params string[] arguments)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
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
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(error);
        return output;
    }

    private static string ReadArgumentValue(ProcessRunRequest request, string name)
    {
        var arguments = request.Arguments.ToArray();
        var index = Array.IndexOf(arguments, name);
        return index >= 0 && index + 1 < arguments.Length
            ? arguments[index + 1]
            : throw new InvalidOperationException($"Missing process argument: {name}");
    }

    private sealed class LocalPackageBuildRunner : IProcessRunner
    {
        private readonly string _remoteRoot;
        private readonly string _remoteUrl;
        private readonly bool _mutateDuringBuild;

        internal LocalPackageBuildRunner(
            string remoteRoot,
            string remoteUrl,
            bool mutateDuringBuild)
        {
            _remoteRoot = remoteRoot;
            _remoteUrl = remoteUrl;
            _mutateDuringBuild = mutateDuringBuild;
        }

        internal List<ProcessRunRequest> Requests { get; } = new();

        internal string SourcePackagesRoot { get; private set; } = string.Empty;

        public Task<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            request.InvokeStartBoundary();
            if (request.Arguments.Contains("-resolvePackageDependencies"))
            {
                SourcePackagesRoot = ReadArgumentValue(request, "-clonedSourcePackagesDirPath");
                var checkouts = Directory.CreateDirectory(Path.Combine(SourcePackagesRoot, "checkouts"));
                var checkout = Path.Combine(checkouts.FullName, "Shared");
                RunGit(checkouts.FullName, "clone", "--quiet", "--no-hardlinks", _remoteRoot, checkout);
                RunGit(checkout, "remote", "set-url", "origin", _remoteUrl);
            }
            else if (request.FileName.Equals("/usr/bin/xcodebuild", StringComparison.Ordinal))
            {
                if (_mutateDuringBuild)
                {
                    File.AppendAllText(
                        Path.Combine(SourcePackagesRoot, "checkouts", "Shared", "Package.swift"),
                        "// unapproved mutation\n");
                }
                AppleDeploymentTestFixture.MaterializeConfiguredBuildProduct(request);
            }

            var result = request.Arguments.Contains("install")
                ? Success("App installed:\n• bundleID: com.evotecit.casaray\n")
                : Success("ok");
            request.InvokeCompletionBoundary(result);
            return Task.FromResult(result);
        }
    }

    private sealed class LocalPackageBuildFixture : IDisposable
    {
        private readonly DirectoryInfo _root;
        private readonly DirectoryInfo _remote;

        internal LocalPackageBuildFixture(
            DirectoryInfo root,
            DirectoryInfo remote,
            string projectPath,
            string derivedDataPath,
            string remoteUrl)
        {
            _root = root;
            _remote = remote;
            ProjectPath = projectPath;
            DerivedDataPath = derivedDataPath;
            RemoteUrl = remoteUrl;
        }

        internal string ProjectPath { get; }

        internal string DerivedDataPath { get; }

        internal string RemoteRoot => _remote.FullName;

        internal string RemoteUrl { get; }

        public void Dispose()
        {
            DeleteExternalOutputs(_root);
            try { _root.Delete(recursive: true); } catch { /* best effort */ }
            try { _remote.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}
