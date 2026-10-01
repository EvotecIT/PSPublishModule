using System.Text.Json.Nodes;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void AppleDotNetArchivePlanAllowsCredentialsAndBindsReferencedPublishSettings()
    {
        string root = CreateSandbox();
        try
        {
            string key = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(key, "test-only-placeholder");
            string publish = Path.Combine(root, "publish.json");
            File.WriteAllText(publish, """
                {"SchemaVersion":1,"DotNet":{"ProjectRoot":"."},"Installers":[{
                "Id":"store","Kind":"MacApp","MacApp":{"AppStore":true,"TeamId":"ABCDE12345",
                "BundleIdentifier":"com.example.studio","BundleName":"Studio","Version":"1.0","BuildNumber":"1",
                "Executable":"Studio","CodesignIdentity":"Apple Distribution: Example (ABCDE12345)",
                "InstallerSigningIdentity":"3rd Party Mac Developer Installer: Example (ABCDE12345)",
                "EntitlementsPath":"AppStore.entitlements"}}]}
                """);
            File.WriteAllText(Path.Combine(root, "tool.release.json"), """
                {"Tools":{"DotNetPublishConfigPath":"publish.json"}}
                """);
            File.WriteAllText(Path.Combine(root, "AppStore.entitlements"), "<plist><dict><key>com.apple.security.app-sandbox</key><true/></dict></plist>");
            var spec = CreateAppleAutomationSpec(root, key);
            spec.AppleApps!.TeamId = "ABCDE12345";
            spec.AppleApps.AllowProvisioningUpdates = false;
            var app = Assert.Single(spec.AppleApps.Apps);
            app.Platform = ApplePlatform.macOS;
            app.BundleId = "com.example.studio";
            app.ProjectPath = "tool.release.json";
            app.DotNetPublishInstallerId = "store";
            app.MarketingVersion = "1.0";
            app.BuildNumber = "1";
            var service = CreateAppleAutomationService(request => CreateReleaseState(request, "VALID"));
            var request = new PowerForgeReleaseRequest {
                ConfigPath = Path.Combine(root, "powerforge.release.json"), PlanOnly = true,
                AppleAction = PowerForgeAppleReleaseAction.Archive
            };
            var before = service.Execute(spec, request);
            Assert.True(before.Success, before.ErrorMessage);
            var document = JsonNode.Parse(File.ReadAllText(publish))!;
            document["DotNet"]!["MsBuildProperties"] = new JsonObject { ["StudioDistribution"] = "Store" };
            File.WriteAllText(publish, document.ToJsonString());
            var after = service.Execute(spec, request);
            Assert.True(after.Success, after.ErrorMessage);
            Assert.NotEqual(before.AppleReceipt!.PlanSha256, after.AppleReceipt!.PlanSha256);
        }
        finally { TryDelete(root); }
    }
}
