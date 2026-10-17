namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void AppleConfigurationVersion_UpdatesOnlySharedIdentityAndPreservesComments()
    {
        var root = CreateSandbox();
        try
        {
            var path = Path.Combine(root, "Version.xcconfig");
            const string content = "// Shared by both targets\r\nMARKETING_VERSION = 0.1 // public train\r\nCURRENT_PROJECT_VERSION = 1\r\nPRODUCT_NAME = Studio\r\n";
            File.WriteAllText(path, content);
            var service = new AppleReleaseVersionSourceService();
            var before = service.Read(path);
            Assert.Equal("0.1", before.MarketingVersion);
            Assert.Equal("1", before.BuildNumber);

            service.Update(path, content, "1.0", "2", 1, whatIf: false);
            var updated = File.ReadAllText(path);
            Assert.Equal(content.Replace("= 0.1", "= \"1.0\"").Replace("= 1\r", "= \"2\"\r"), updated);
            Assert.Equal("2", service.Read(path).BuildNumber);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void Execute_AppleVersion_UsesSharedConfigurationWithoutGeneratingOrEditingProject()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "0.1", "1");
            var project = Path.Combine(root, "CasaRay.xcodeproj", "project.pbxproj");
            var originalProject = File.ReadAllText(project);
            var sourcePath = Path.Combine(root, "Version.xcconfig");
            File.WriteAllText(sourcePath, "MARKETING_VERSION = 0.1\nCURRENT_PROJECT_VERSION = 1\n");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Automation.VersionSourcePath = "Version.xcconfig";
            var service = CreateAppleAutomationService(
                _ => throw new InvalidOperationException("Version must not query release status."),
                generateAppleProject: _ => throw new InvalidOperationException("Checked-in projects must not run XcodeGen."),
                getHighestAppleBuildNumber: (_, _, _) => 1);

            var result = service.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"),
                AppleAction = PowerForgeAppleReleaseAction.Version,
                AppleMarketingVersion = "1.0",
                AppleActionConfirmed = true
            });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal("1.0", Assert.Single(result.AppleReceipt!.Targets).Version);
            Assert.Equal("2", Assert.Single(result.AppleReceipt.Targets).Build);
            Assert.Equal("2", new AppleReleaseVersionSourceService().Read(sourcePath).BuildNumber);
            Assert.Equal(originalProject, File.ReadAllText(project));
        }
        finally { TryDelete(root); }
    }
}
