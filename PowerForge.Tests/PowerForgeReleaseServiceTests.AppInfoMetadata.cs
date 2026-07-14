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

    [Fact]
    public void Execute_AppleApps_SyncsAppInfoWithoutResolvingVersionOrBuild()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "Tactra.xcodeproj");
            var keyPath = Path.Combine(root, "AuthKey_ABC123DEFG.p8");
            File.WriteAllText(keyPath, "private-key");
            WriteAppInfoConfig(root, includeAppId: true);
            var requests = new List<AppStoreConnectReleasePreparationRequest>();
            var service = CreateAppInfoReleaseService(requests);

            var result = service.Execute(
                CreateAppInfoReleaseSpec(keyPath),
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json")
                });

            Assert.True(result.Success);
            var request = Assert.Single(requests);
            Assert.Equal(string.Empty, request.VersionString);
            Assert.Equal(string.Empty, request.BuildNumber);
            Assert.False(request.CreateVersion);
            Assert.False(request.SelectBuild);
            Assert.NotNull(request.AppInfoMetadataSpec);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleApps_RejectsAppInfoConfigWithoutAppId()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "Tactra.xcodeproj");
            var keyPath = Path.Combine(root, "AuthKey_ABC123DEFG.p8");
            File.WriteAllText(keyPath, "private-key");
            WriteAppInfoConfig(root, includeAppId: false);
            var service = CreateAppInfoReleaseService(new List<AppStoreConnectReleasePreparationRequest>());

            var exception = Assert.Throws<InvalidOperationException>(() => service.Execute(
                CreateAppInfoReleaseSpec(keyPath),
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json")
                }));

            Assert.Contains("must declare AppId", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static PowerForgeReleaseService CreateAppInfoReleaseService(
        List<AppStoreConnectReleasePreparationRequest> requests)
        => new(
            new NullLogger(),
            executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
            planTools: (_, _, _) => throw new InvalidOperationException("Legacy tools should not run."),
            runTools: _ => throw new InvalidOperationException("Legacy tools should not run."),
            loadDotNetToolsSpec: (_, _) => throw new InvalidOperationException("DotNet tools should not run."),
            planDotNetTools: (_, _, _, _) => throw new InvalidOperationException("DotNet tools should not run."),
            runDotNetTools: _ => throw new InvalidOperationException("DotNet tools should not run."),
            publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."),
            archiveAppleApp: _ => throw new InvalidOperationException("Archive should not run."),
            uploadAppleApp: _ => throw new InvalidOperationException("Upload should not run."),
            prepareAppleDistribution: request =>
            {
                requests.Add(request);
                return new AppStoreConnectReleasePreparationResult
                {
                    AppId = request.AppId,
                    Platform = request.Platform,
                    AppInfoMetadata = new AppStoreConnectAppInfoMetadataSyncResult
                    {
                        UpdatedFields = new[] { "privacyPolicyUrl" }
                    }
                };
            });

    private static PowerForgeReleaseSpec CreateAppInfoReleaseSpec(string keyPath)
        => new()
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
        };

    private static void WriteAppInfoConfig(string root, bool includeAppId)
    {
        var json = includeAppId
            ? """
              {
                "appId": "app-1",
                "locale": "en-US",
                "metadata": {
                  "privacyPolicyUrl": "https://tactra.dev/privacy/"
                }
              }
              """
            : """
              {
                "locale": "en-US",
                "metadata": {
                  "privacyPolicyUrl": "https://tactra.dev/privacy/"
                }
              }
              """;
        File.WriteAllText(Path.Combine(root, "app-info.json"), json);
    }
}
