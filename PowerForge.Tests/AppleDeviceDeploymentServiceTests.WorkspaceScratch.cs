namespace PowerForge.Tests;

public sealed partial class AppleDeviceDeploymentServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BuildAsync_allows_xcode_workspace_containers_but_rejects_file_changes(bool writePackageLock)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "Tactra.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            var workspace = Directory.CreateDirectory(Path.Combine(project.FullName, "project.xcworkspace"));
            File.WriteAllText(Path.Combine(workspace.FullName, "contents.xcworkspacedata"), "<Workspace/>");
            InitializeGitRepository(root.FullName);
            var mirror = ExternalOutputPath(root, "Mirror");
            var runner = new CapturingProcessRunner(request =>
            {
                if (request.FileName == "/usr/bin/rsync")
                {
                    foreach (var source in Directory.EnumerateFiles(project.FullName, "*", SearchOption.AllDirectories))
                    {
                        var target = Path.Combine(mirror, "Tactra.xcodeproj", Path.GetRelativePath(project.FullName, source));
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        // Match rsync's read/write boundary; APFS File.Copy can
                        // clone the source and produce source metadata events.
                        File.WriteAllBytes(target, File.ReadAllBytes(source));
                    }
                }
                if (request.FileName == "/usr/bin/xcodebuild")
                {
                    var packages = Path.Combine(mirror, "Tactra.xcodeproj", "project.xcworkspace", "xcshareddata", "swiftpm");
                    Directory.CreateDirectory(Path.Combine(packages, "configuration"));
                    if (writePackageLock)
                        File.WriteAllText(Path.Combine(packages, "Package.resolved"), "unbound package lock");
                }
                return Success("ok");
            });
            var request = new AppleAppBuildRequest
            {
                ProjectPath = project.FullName,
                Scheme = "Tactra",
                DeviceIdentifier = "device-1",
                UseBuildMirror = true,
                BuildMirrorPath = mirror,
                DerivedDataPath = ExternalOutputPath(root, "DerivedData")
            };
            if (writePackageLock)
            {
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    new AppleDeviceDeploymentService(runner).BuildAsync(request));
                Assert.Contains("local Apple build mirror changed", error.Message);
            }
            else
            {
                var result = await new AppleDeviceDeploymentService(runner).BuildAsync(request);
                Assert.True(result.Succeeded);
            }
        }
        finally
        {
            DeleteExternalOutputs(root);
            root.Delete(recursive: true);
        }
    }
}
