using System.Text.Json;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Execute_AppleMetadata_AcceptsCopyrightOnlyWithoutInventingALocalization(bool includeLocalizedMetadata)
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "Sample.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Apps[0].ProjectPath = "Sample.xcodeproj";
            spec.AppleApps.Apps[0].Scheme = "Sample";
            spec.AppleApps.Apps[0].AppStoreConnectAppId = "1234567890";
            spec.AppleApps.Archive = false;
            spec.AppleApps.SyncMetadata = true;
            spec.AppleApps.CheckReleaseReadiness = true;
            File.WriteAllText(Path.Combine(root, "copyright.json"), JsonSerializer.Serialize(new AppStoreConnectVersionMetadataSpec
            {
                AppId = "1234567890", UseReleaseVersion = true, Copyright = "2022 Example", Locale = ""
            }));
            var paths = new List<string> { "copyright.json" };
            if (includeLocalizedMetadata)
            {
                File.WriteAllText(Path.Combine(root, "en.json"), JsonSerializer.Serialize(new AppStoreConnectVersionMetadataSpec
                {
                    AppId = "1234567890", UseReleaseVersion = true, Locale = "en-US", Metadata = new() { Description = "Documents" }
                }));
                paths.Add("en.json");
            }
            spec.AppleApps.MetadataConfigPaths = paths.ToArray();
            AppStoreConnectReleasePreparationRequest? captured = null;
            var service = CreateAppleAutomationService(request => CreateReleaseState(request, "VALID"),
                prepareAppleDistribution: request => { captured = request; return CreateSuccessfulPreparation(request); },
                checkAppleReleaseReadiness: (_, request) => CreateReadyReleaseReadiness(request));
            var result = service.Execute(spec, new PowerForgeReleaseRequest { ConfigPath = Path.Combine(root, "powerforge.release.json") });
            Assert.True(result.Success, result.ErrorMessage ?? result.AppleReceipt?.ErrorMessage);
            Assert.NotNull(captured);
            Assert.Equal("2022 Example", captured.MetadataSpecs[0].Copyright);
            Assert.Equal(includeLocalizedMetadata ? new[] { "en-US" } : Array.Empty<string>(), captured.ReadinessRequest!.MetadataLocales);
        }
        finally { TryDelete(root); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Execute_AppleMetadata_ResolvesEveryLocaleAndRejectsDuplicates(bool duplicate)
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "Sample.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var locales = new[] { "en-US", "pl", "de-DE", "es-ES", "fr-FR", "it", "nl-NL", "sv", "da", "no", "cs", "pt-BR", "fi", "pt-PT", "uk", "ja", "ko", "zh-Hans", "zh-Hant" };
            var paths = new List<string>();
            foreach (var platform in new[] { ApplePlatform.iOS, ApplePlatform.macOS })
            {
                foreach (var locale in locales)
                {
                    var filename = $"metadata-{platform}-{locale}.json";
                    paths.Add(filename);
                    File.WriteAllText(Path.Combine(root, filename), JsonSerializer.Serialize(new AppStoreConnectVersionMetadataSpec
                    {
                        AppId = "1234567890", Platform = platform, UseReleaseVersion = true,
                        Locale = duplicate && locale == "pl" ? " EN-us " : locale,
                        Metadata = new() { Description = $"Description: {platform}/{locale}" }
                    }));
                }
            }
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Apps[0].Name = "iOS";
            spec.AppleApps.Apps[0].ProjectPath = "Sample.xcodeproj";
            spec.AppleApps.Apps[0].Scheme = "Sample";
            spec.AppleApps.Apps[0].AppStoreConnectAppId = "1234567890";
            spec.AppleApps.Apps[0].BundleId = "com.example.smart-home";
            spec.AppleApps.Archive = false;
            spec.AppleApps.SyncMetadata = true;
            spec.AppleApps.MetadataConfigPaths = paths.ToArray();
            spec.AppleApps.Apps = new[]
            {
                spec.AppleApps.Apps[0],
                new AppleAppConfiguration
                {
                    Name = "Mac", BundleId = "com.example.smart-home", Platform = ApplePlatform.macOS,
                    ProjectPath = "Sample.xcodeproj", Scheme = "Sample", AppStoreConnectAppId = "1234567890"
                }
            };
            var requests = new List<AppStoreConnectReleasePreparationRequest>();
            var service = CreateAppleAutomationService(
                request => CreateReleaseState(request, "VALID"),
                prepareAppleDistribution: request =>
                {
                    requests.Add(request);
                    return CreateSuccessfulPreparation(request);
                });
            var result = service.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json")
            });

            if (duplicate)
            {
                Assert.False(result.Success);
                Assert.Empty(requests);
                Assert.Contains("Multiple App Store metadata configs", result.AppleReceipt!.ErrorMessage);
            }
            else
            {
                Assert.True(result.Success, result.ErrorMessage ?? result.AppleReceipt?.ErrorMessage);
                Assert.Equal(2, requests.Count);
                foreach (var request in requests)
                {
                    Assert.Equal(locales, request.MetadataSpecs.Select(value => value.Locale));
                    Assert.All(request.MetadataSpecs, value => Assert.Equal(request.Platform, value.Platform));
                    Assert.Equal("1.2.0", request.VersionString);
                }
            }
        }
        finally { TryDelete(root); }
    }
}
