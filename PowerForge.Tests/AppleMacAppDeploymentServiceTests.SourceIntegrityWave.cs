using PowerForge;

namespace PowerForge.Tests;

public sealed partial class AppleMacAppDeploymentServiceTests
{
    [Fact]
    public async Task DeployAsync_rejects_an_install_root_symlink_alias_into_source()
    {
        if (Path.DirectorySeparatorChar == '\\')
            return;

        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            Guid.NewGuid().ToString("N")));
        var alias = Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests.InstallAlias",
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
                new AppleMacAppDeploymentService(runner).DeployAsync(
                    new AppleMacAppDeploymentRequest
                    {
                        ProjectPath = project.FullName,
                        Scheme = "CasaRay",
                        Platform = ApplePlatform.macOS,
                        DerivedDataPath = ExternalOutputPath(root, "DerivedData"),
                        InstallRoot = Path.Combine(alias, "Applications"),
                        Launch = false
                    }));

            Assert.Contains(nameof(AppleMacAppDeploymentRequest.InstallRoot), error.Message, StringComparison.Ordinal);
            Assert.Empty(runner.Requests);
        }
        finally
        {
            try { Directory.Delete(alias); } catch { /* best effort */ }
            DeleteExternalOutputs(root);
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task DeployAsync_rejects_a_product_mutation_before_mac_replacement()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "CasaRay.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            var derived = Directory.CreateDirectory(ExternalOutputPath(root, "DerivedData"));
            var source = Directory.CreateDirectory(Path.Combine(
                derived.FullName,
                "Build",
                "Products",
                "Debug-maccatalyst",
                "CasaRay.app"));
            var payload = Path.Combine(source.FullName, "version.txt");
            File.WriteAllText(payload, "approved");
            var installRoot = Directory.CreateDirectory(ExternalOutputPath(root, "Applications"));
            var existing = Directory.CreateDirectory(Path.Combine(installRoot.FullName, "CasaRay.app"));
            File.WriteAllText(Path.Combine(existing.FullName, "version.txt"), "existing");
            InitializeGitRepository(root.FullName);
            var runner = new CapturingProcessRunner(request =>
            {
                if (request.FileName == "ditto-test")
                {
                    CopyDirectory(request.Arguments[0], request.Arguments[1]);
                    File.WriteAllText(
                        Path.Combine(request.Arguments[0], "version.txt"),
                        "replacement");
                }
                return Success("ok");
            });

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleMacAppDeploymentService(runner).DeployAsync(
                    new AppleMacAppDeploymentRequest
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
                    }));

            Assert.Contains("private built Apple app snapshot changed", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(
                "existing",
                File.ReadAllText(Path.Combine(existing.FullName, "version.txt")));
        }
        finally
        {
            DeleteExternalOutputs(root);
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}
