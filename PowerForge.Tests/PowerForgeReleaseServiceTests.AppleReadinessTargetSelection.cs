using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Theory]
    [InlineData("app", true, false)]
    [InlineData("platform", true, false)]
    [InlineData("version", true, false)]
    [InlineData("app", false, false)]
    [InlineData("platform", false, false)]
    [InlineData("version", false, false)]
    [InlineData("app", true, true)]
    [InlineData("platform", true, true)]
    [InlineData("version", true, true)]
    [InlineData("app", false, true)]
    [InlineData("platform", false, true)]
    [InlineData("version", false, true)]
    public void Execute_AppleReadinessRejectsConfiguredMetadataForAnotherTarget(string mismatch, bool planOnly, bool submit)
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "Sample.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            var app = spec.AppleApps!.Apps[0];
            app.ProjectPath = "Sample.xcodeproj";
            app.Scheme = "Sample";
            app.Name = "Sample";
            app.BundleId = "com.example.sample";
            app.AppStoreConnectAppId = "1234567890";
            spec.AppleApps.Archive = false;
            spec.AppleApps.Upload = false;
            spec.AppleApps.PrepareDistribution = true;
            spec.AppleApps.CheckReleaseReadiness = true;
            spec.AppleApps.SyncMetadata = false;
            ConfigureMetadataReadinessFiles(spec, root, ["en-US", "pl"]);
            foreach (var path in spec.AppleApps.MetadataConfigPaths)
            {
                var absolutePath = Path.Combine(root, path);
                var metadata = JsonSerializer.Deserialize<AppStoreConnectVersionMetadataSpec>(File.ReadAllText(absolutePath))!;
                if (mismatch == "app") metadata.AppId = "other-app";
                if (mismatch == "platform") metadata.Platform = ApplePlatform.macOS;
                if (mismatch == "version")
                {
                    metadata.UseReleaseVersion = false;
                    metadata.VersionString = "9.9.9";
                }
                File.WriteAllText(absolutePath, JsonSerializer.Serialize(metadata));
            }
            var readinessCalls = 0;
            var mutationCalls = 0;
            var service = CreateAppleAutomationService(request => CreateReleaseState(request, "VALID"),
                checkAppleReleaseReadiness: (_, request) => { readinessCalls++; return CreateReadyReleaseReadiness(request); },
                prepareAppleDistribution: request => { mutationCalls++; return CreateSuccessfulPreparation(request); },
                submitAppleReview: request => { mutationCalls++; return new(); });
            var request = new PowerForgeReleaseRequest()
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"), PlanOnly = planOnly,
                AppleAction = submit ? PowerForgeAppleReleaseAction.SubmitAppReview : PowerForgeAppleReleaseAction.Configured,
                AppleActionConfirmed = !planOnly
            };
            if (planOnly)
            {
                var error = Assert.Throws<InvalidOperationException>(() => service.Execute(spec, request));
                Assert.Contains("No App Store metadata config matches", error.Message, StringComparison.Ordinal);
            }
            else
            {
                var result = service.Execute(spec, request);
                Assert.False(result.Success);
                var errors = new[] { result.ErrorMessage }.Concat(result.AppleApps.Select(value => value.ErrorMessage));
                Assert.Contains(errors, error => error?.Contains("No App Store metadata config matches", StringComparison.Ordinal) == true);
            }
            Assert.Equal(0, readinessCalls);
            Assert.Equal(0, mutationCalls);
        }
        finally { TryDelete(root); }
    }
}
