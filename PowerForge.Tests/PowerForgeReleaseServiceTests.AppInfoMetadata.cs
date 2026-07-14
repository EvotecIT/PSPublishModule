using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void Execute_AppleApps_AcceptsAppInfoMetadataInUnifiedPlan()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "Tactra.xcodeproj");
            var keyPath = Path.Combine(root, "AuthKey_ABC123DEFG.p8");
            File.WriteAllText(keyPath, "private-key");
            File.WriteAllText(Path.Combine(root, "app-info.json"),
                """
                {
                  "appId": "app-1",
                  "locale": "en-US",
                  "metadata": {
                    "name": "Tactra Remote",
                    "subtitle": "Premium Home Assistant remote",
                    "privacyPolicyUrl": "https://tactra.dev/privacy/"
                  }
                }
                """);

            var service = new PowerForgeReleaseService(new NullLogger());
            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Archive = false,
                        SyncAppInfo = true,
                        AppInfoConfigPath = "app-info.json",
                        AppStoreConnectApiKeyPath = keyPath,
                        AppStoreConnectApiKeyId = "ABC123DEFG",
                        AppStoreConnectApiIssuerId = "issuer-id",
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "Tactra",
                                ProjectPath = "Tactra.xcodeproj",
                                Scheme = "Tactra",
                                Platform = ApplePlatform.iOS,
                                AppStoreConnectAppId = "app-1"
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true
                });

            Assert.True(result.Success);
            Assert.NotNull(result.AppleAppPlan);
            Assert.True(result.AppleAppPlan!.SyncAppInfo);
            Assert.EndsWith("app-info.json", result.AppleAppPlan.AppInfoConfigPath, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }
}
