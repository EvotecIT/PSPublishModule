namespace PowerForge.Tests;

public partial class PowerForgeReleaseServiceTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Execute_AppleApps_ReadinessUsesScreenshotSpecWithoutRequestingScreenshotSync(int localeCount)
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "SampleMedia.xcodeproj", "1.0.5", "9");
            var keyPath = Path.Combine(root, "AuthKey_ABC123DEFG.p8");
            var screenshotConfigPath = Path.Combine(root, "screenshots.json");
            File.WriteAllText(keyPath, "private-key");
            File.WriteAllText(screenshotConfigPath,
                """
                {
                  "appId": "app-1",
                  "versionString": "1.0.5",
                  "platform": "iOS",
                  "locale": "en-US",
                  "screenshotSets": [
                    {
                      "screenshotDisplayType": "APP_IPHONE_65",
                      "path": "../missing-local-screenshots",
                      "filter": "*.png"
                    }
                  ]
                }
                """);
            var polishConfigPath = Path.Combine(root, "screenshots-pl.json");
            File.WriteAllText(polishConfigPath, File.ReadAllText(screenshotConfigPath).Replace("en-US", "pl"));
            var requests = new List<AppStoreConnectReleasePreparationRequest>();

            var service = new PowerForgeReleaseService(
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
                        VersionString = request.VersionString,
                        BuildNumber = request.BuildNumber,
                        Platform = request.Platform,
                        Version = new AppStoreConnectVersionInfo { Id = "version-1", VersionString = request.VersionString }
                    };
                });

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Archive = false,
                        CheckReleaseReadiness = true,
                        SyncScreenshots = false,
                        ScreenshotConfigPaths = localeCount == 1 ? new[] { screenshotConfigPath } : new[] { screenshotConfigPath, polishConfigPath },
                        AppStoreConnectApiKeyPath = keyPath,
                        AppStoreConnectApiKeyId = "ABC123DEFG",
                        AppStoreConnectApiIssuerId = "issuer-id",
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "SampleMedia",
                                ProjectPath = "SampleMedia.xcodeproj",
                                Scheme = "SampleMedia",
                                Platform = ApplePlatform.iOS,
                                AppStoreConnectAppId = "app-1"
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json")
                });

            Assert.True(result.Success);
            var request = Assert.Single(requests);
            Assert.True(request.CheckReadiness);
            Assert.Null(request.ScreenshotSpec);
            Assert.Empty(request.ScreenshotMappings);
            Assert.NotNull(request.ReadinessRequest);
            var specs = localeCount == 1 ? new[] { request.ReadinessRequest.ScreenshotSpec! } : request.ReadinessRequest.ScreenshotSpecs;
            Assert.Equal(localeCount, specs.Length);
            Assert.Equal(localeCount == 1 ? new[] { "en-US" } : new[] { "en-US", "pl" }, specs.Select(spec => spec.Locale));
            Assert.All(specs, spec => Assert.Equal("APP_IPHONE_65", Assert.Single(spec.ScreenshotSets).ScreenshotDisplayType));
        }
        finally
        {
            TryDelete(root);
        }
    }

}
