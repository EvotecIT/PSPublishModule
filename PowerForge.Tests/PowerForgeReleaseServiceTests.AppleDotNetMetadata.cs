namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Theory]
    [InlineData(PowerForgeAppleReleaseAction.Configured)]
    [InlineData(PowerForgeAppleReleaseAction.Status)]
    [InlineData(PowerForgeAppleReleaseAction.Prepare)]
    public void AppleDotNetNonArchivePlanDoesNotRequireLocalSigningProfile(PowerForgeAppleReleaseAction action)
    {
        string root = CreateSandbox();
        try
        {
            var spec = CreateDotNetMetadataSpec(root);
            var service = CreateAppleAutomationService(request => CreateReleaseState(request, "VALID"),
                checkAppleReleaseReadiness: (_, request) => CreateReadyReleaseReadiness(request));
            var result = service.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"),
                PlanOnly = true,
                AppleAction = action
            });

            Assert.True(result.Success, result.ErrorMessage);
            var plan = Assert.IsType<PowerForgeAppleReleasePlan>(result.AppleAppPlan);
            Assert.False(plan.Archive);
            Assert.Contains("publish.json", plan.ApprovedMutationInputFilesSha256.Keys);
            if (plan.SyncAppInfo)
                Assert.Contains("app-info.json", plan.ApprovedMutationInputFilesSha256.Keys);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void AppleDotNetArchivePlanRequiresLocalSigningProfile()
    {
        string root = CreateSandbox();
        try
        {
            var service = CreateAppleAutomationService(request => CreateReleaseState(request, "VALID"));
            var exception = Assert.Throws<FileNotFoundException>(() => service.Execute(CreateDotNetMetadataSpec(root), new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"),
                PlanOnly = true,
                AppleAction = PowerForgeAppleReleaseAction.Archive
            }));

            Assert.Contains("missing.provisionprofile", exception.Message, StringComparison.Ordinal);
        }
        finally { TryDelete(root); }
    }

    private static PowerForgeReleaseSpec CreateDotNetMetadataSpec(string root)
    {
        string key = Path.Combine(root, "AuthKey_TEST.p8");
        File.WriteAllText(key, "test-only-placeholder");
        File.WriteAllText(Path.Combine(root, "publish.json"), """
            {"SchemaVersion":1,"DotNet":{"ProjectRoot":"."},"Installers":[{
            "Id":"store","Kind":"MacApp","MacApp":{"AppStore":true,"TeamId":"ABCDE12345",
            "BundleIdentifier":"com.example.studio","BundleName":"Studio","Version":"1.0","BuildNumber":"1",
            "Executable":"Studio","CodesignIdentity":"Apple Distribution: Example (ABCDE12345)",
            "InstallerSigningIdentity":"3rd Party Mac Developer Installer: Example (ABCDE12345)",
            "ProvisioningProfilePath":"missing.provisionprofile"}}]}
            """);
        File.WriteAllText(Path.Combine(root, "app-info.json"), """
            {"appId":"6778025328","locale":"en-US","metadata":{"privacyPolicyUrl":"https://example.com/privacy/"}}
            """);
        var spec = CreateAppleAutomationSpec(root, key);
        spec.AppleApps!.TeamId = "ABCDE12345";
        spec.AppleApps.Archive = false;
        spec.AppleApps.SyncAppInfo = true;
        spec.AppleApps.AppInfoConfigPath = "app-info.json";
        var app = Assert.Single(spec.AppleApps.Apps);
        app.Platform = ApplePlatform.macOS;
        app.BundleId = "com.example.studio";
        app.ProjectPath = "publish.json";
        app.DotNetPublishInstallerId = "store";
        app.MarketingVersion = "1.0";
        app.BuildNumber = "1";
        return spec;
    }
}
