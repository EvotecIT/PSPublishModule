namespace PowerForge.Tests;

public sealed partial class AppleDeviceDeploymentServiceTests
{
    [Theory]
    [InlineData(null, null, null)]
    [InlineData("generic/platform=iOS", null, null)]
    [InlineData("platform=iOS,id=device-1", "device-2", null)]
    [InlineData("generic/platform=iOS", "device-1", null)]
    public async Task DeployAsync_rejects_missing_or_conflicting_device_before_build(
        string? destination, string? identifier, string? device)
    {
        var runner = new CapturingProcessRunner(_ => Success("unexpected"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new AppleDeviceDeploymentService(runner).DeployAsync(new AppleAppDeviceDeploymentRequest
            {
                Destination = destination,
                DeviceIdentifier = identifier,
                Device = device
            }));
        Assert.Empty(runner.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeployAsync_binds_named_selector_or_rejects_conflicting_destination(bool conflictingDestination)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "Tactra.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            InitializeGitRepository(root.FullName);
            const string first = "00008150-00011C2A3640401C";
            var discoveries = 0;
            var runner = new CapturingProcessRunner(request =>
            {
                if (request.Arguments.Contains("list"))
                {
                    discoveries++;
                    var identifier = discoveries == 1 ? first : "00008150-00011C2A3640402C";
                    return Success($"EvoPhone   phone.coredevice.local   {identifier}   connected   iPhone 17 Pro Max");
                }
                return Success("ok");
            });

            var request = new AppleAppDeviceDeploymentRequest
            {
                ProjectPath = project.FullName,
                Scheme = "Tactra",
                DerivedDataPath = ExternalOutputPath(root, "DerivedData"),
                Device = "EvoPhone",
                Destination = conflictingDestination ? "platform=iOS,id=00008150-00011C2A3640402C" : null,
                BundleIdentifier = "com.example.tactra",
                Launch = true
            };
            if (conflictingDestination)
            {
                var error = await Assert.ThrowsAsync<ArgumentException>(() =>
                    new AppleDeviceDeploymentService(runner).DeployAsync(request));
                Assert.Contains("select different devices", error.Message);
                Assert.Single(runner.Requests);
                return;
            }
            var result = await new AppleDeviceDeploymentService(runner).DeployAsync(request);

            Assert.True(result.Succeeded);
            Assert.Equal(1, discoveries);
            Assert.Equal("id=" + first, result.Build.Destination);
            Assert.Equal(first, result.Install!.DeviceIdentifier);
            Assert.Equal(first, result.Launch!.DeviceIdentifier);
            Assert.All(runner.Requests.Where(request => request.Arguments.Contains("--device")),
                request => Assert.Equal(first, ReadArgumentValue(request, "--device")));
        }
        finally
        {
            DeleteExternalOutputs(root);
            root.Delete(recursive: true);
        }
    }
}
