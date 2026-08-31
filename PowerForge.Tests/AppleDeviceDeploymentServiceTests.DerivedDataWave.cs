namespace PowerForge.Tests;

public sealed partial class AppleDeviceDeploymentServiceTests
{
    [Fact]
    public async Task BuildAsync_does_not_reuse_preexisting_DerivedData_contents()
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
            var derived = ExternalOutputPath(root, "DerivedData");
            var staleObject = Path.Combine(
                derived,
                "Build",
                "Intermediates.noindex",
                "Injected.o");
            Directory.CreateDirectory(Path.GetDirectoryName(staleObject)!);
            File.WriteAllText(staleObject, "unattested object bytes");
            var runner = new CapturingProcessRunner(_ => Success("ok"));
            InitializeGitRepository(root.FullName);

            var result = await new AppleDeviceDeploymentService(runner).BuildAsync(
                new AppleAppBuildRequest
                {
                    ProjectPath = project.FullName,
                    Scheme = "CasaRay",
                    Destination = "id=device-1",
                    DerivedDataPath = derived,
                    XcodeBuildExecutable = "/usr/bin/xcodebuild"
                });

            var buildDerivedData = ReadArgumentValue(
                Assert.Single(runner.Requests),
                "-derivedDataPath");
            var physicalBuildDerivedData =
                AppleReleaseArtifactService.ResolvePhysicalPath(buildDerivedData);
            Assert.NotEqual(
                AppleReleaseArtifactService.ResolvePhysicalPath(derived),
                physicalBuildDerivedData);
            Assert.StartsWith(
                Path.Combine(
                    AppleReleaseArtifactService.ResolvePhysicalPath(derived),
                    "PowerForge",
                    "ExactSourceBuilds") + Path.DirectorySeparatorChar,
                physicalBuildDerivedData,
                StringComparison.Ordinal);
            Assert.Equal(buildDerivedData, result.DerivedDataPath);
            Assert.True(File.Exists(staleObject));
        }
        finally
        {
            DeleteExternalOutputs(root);
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task BuildAsync_rejects_a_fresh_DerivedData_injection_before_process_start()
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
            var derived = ExternalOutputPath(root, "DerivedData");
            var runner = new DerivedDataInjectionRunner();
            InitializeGitRepository(root.FullName);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleDeviceDeploymentService(runner).BuildAsync(
                    new AppleAppBuildRequest
                    {
                        ProjectPath = project.FullName,
                        Scheme = "CasaRay",
                        Destination = "id=device-1",
                        DerivedDataPath = derived,
                        XcodeBuildExecutable = "/usr/bin/xcodebuild"
                    }));

            Assert.Contains(
                "populated before xcodebuild started",
                error.Message,
                StringComparison.OrdinalIgnoreCase);
            Assert.False(runner.ProcessStarted);
            Assert.NotNull(runner.DerivedDataPath);
            Assert.False(Directory.Exists(runner.DerivedDataPath));
        }
        finally
        {
            DeleteExternalOutputs(root);
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private sealed class AlternativeProductRunner : IProcessRunner
    {
        private readonly string _productDirectory;
        private readonly string _bundleName;

        internal AlternativeProductRunner(
            string productDirectory,
            string bundleName)
        {
            _productDirectory = productDirectory;
            _bundleName = bundleName;
        }

        public Task<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken = default)
        {
            request.InvokePreStartBoundary();
            request.InvokeStartBoundary();
            var derivedDataPath = ReadArgumentValue(
                request,
                "-derivedDataPath");
            Directory.CreateDirectory(Path.Combine(
                derivedDataPath,
                "Build",
                "Products",
                _productDirectory,
                _bundleName));
            var result = Success("ok");
            request.InvokeCompletionBoundary(result);
            return Task.FromResult(result);
        }
    }

    private sealed class DerivedDataInjectionRunner : IProcessRunner
    {
        internal string? DerivedDataPath { get; private set; }

        internal bool ProcessStarted { get; private set; }

        public Task<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken = default)
        {
            DerivedDataPath = ReadArgumentValue(request, "-derivedDataPath");
            File.WriteAllText(
                Path.Combine(DerivedDataPath, "Injected.o"),
                "unattested object bytes");
            request.InvokePreStartBoundary();
            ProcessStarted = true;
            return Task.FromResult(Success("unexpected"));
        }
    }
}
