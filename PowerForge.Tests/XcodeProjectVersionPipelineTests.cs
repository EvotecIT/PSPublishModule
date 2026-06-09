using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class XcodeProjectVersionPipelineTests
{
    [Fact]
    public void Run_UpdatesXcodeProjectVersionSegmentBeforeStaging()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var xcodeproj = Directory.CreateDirectory(Path.Combine(root.FullName, "Tactra.xcodeproj"));
            var pbxproj = Path.Combine(xcodeproj.FullName, "project.pbxproj");
            File.WriteAllText(
                pbxproj,
                """
                    MARKETING_VERSION = 0.9;
                    CURRENT_PROJECT_VERSION = 2;
                    """);

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0",
                    CsprojPath = null
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationXcodeProjectVersionSegment
                    {
                        Configuration = new XcodeProjectVersionConfiguration
                        {
                            Path = "Tactra.xcodeproj",
                            UseResolvedVersion = true,
                            BuildNumber = "3"
                        }
                    }
                }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var result = runner.Run(spec);

            Assert.Single(result.XcodeProjectVersionResults);
            Assert.Equal("0.9", result.XcodeProjectVersionResults[0].Before.MarketingVersion);
            Assert.Equal("1.0.0", result.XcodeProjectVersionResults[0].After.MarketingVersion);
            Assert.Equal("3", result.XcodeProjectVersionResults[0].After.BuildNumber);

            var updated = File.ReadAllText(pbxproj);
            Assert.Contains("MARKETING_VERSION = 1.0.0;", updated, StringComparison.Ordinal);
            Assert.Contains("CURRENT_PROJECT_VERSION = 3;", updated, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Run_PreparesAppleAppSegmentAndIncrementsBuildNumber()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.2.0");

            var xcodeproj = Directory.CreateDirectory(Path.Combine(root.FullName, "Tactra.xcodeproj"));
            var pbxproj = Path.Combine(xcodeproj.FullName, "project.pbxproj");
            File.WriteAllText(
                pbxproj,
                """
                    MARKETING_VERSION = 1.1.0;
                    CURRENT_PROJECT_VERSION = 8;
                    """);

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.2.0",
                    CsprojPath = null
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationAppleAppSegment
                    {
                        Configuration = new AppleAppConfiguration
                        {
                            Name = "Tactra",
                            BundleId = "com.example.Tactra",
                            Platform = ApplePlatform.iOS,
                            ProjectPath = "Tactra.xcodeproj",
                            Scheme = "Tactra",
                            UseResolvedVersion = true,
                            BuildNumberPolicy = AppleBuildNumberPolicy.IncrementExisting
                        }
                    }
                }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var result = runner.Run(spec);

            var appResult = Assert.Single(result.AppleAppResults);
            Assert.Equal("Tactra", appResult.Name);
            Assert.Equal("com.example.Tactra", appResult.BundleId);
            Assert.Equal(ApplePlatform.iOS, appResult.Platform);
            Assert.Equal("1.2.0", appResult.MarketingVersion);
            Assert.Equal("9", appResult.BuildNumber);
            Assert.Single(result.XcodeProjectVersionResults);

            var updated = File.ReadAllText(pbxproj);
            Assert.Contains("MARKETING_VERSION = 1.2.0;", updated, StringComparison.Ordinal);
            Assert.Contains("CURRENT_PROJECT_VERSION = 9;", updated, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static void WriteMinimalModule(string moduleRoot, string moduleName, string version)
    {
        Directory.CreateDirectory(moduleRoot);
        File.WriteAllText(Path.Combine(moduleRoot, $"{moduleName}.psm1"), string.Empty);

        var psd1 = string.Join(Environment.NewLine, new[]
        {
            "@{",
            $"    RootModule = '{moduleName}.psm1'",
            $"    ModuleVersion = '{version}'",
            "    FunctionsToExport = @()",
            "    CmdletsToExport = @()",
            "    AliasesToExport = @()",
            "}"
        }) + Environment.NewLine;

        File.WriteAllText(Path.Combine(moduleRoot, $"{moduleName}.psd1"), psd1);
    }
}
