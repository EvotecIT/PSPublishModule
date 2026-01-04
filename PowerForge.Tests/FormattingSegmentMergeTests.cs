using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class FormattingSegmentMergeTests
{
    [Fact]
    public void Plan_MergesMultipleFormattingSegments()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var seg1 = new ConfigurationFormattingSegment
            {
                Options = new FormattingOptions
                {
                    Merge = new FormattingTargetOptions
                    {
                        FormatCodePSD1 = new FormatCodeOptions { Enabled = true }
                    }
                }
            };

            var seg2 = new ConfigurationFormattingSegment
            {
                Options = new FormattingOptions
                {
                    UpdateProjectRoot = true,
                    Standard = new FormattingTargetOptions
                    {
                        FormatCodePSD1 = new FormatCodeOptions { Enabled = true }
                    }
                }
            };

            var seg3 = new ConfigurationFormattingSegment
            {
                Options = new FormattingOptions
                {
                    Standard = new FormattingTargetOptions
                    {
                        Style = new FormattingStyleOptions { PSD1 = "Minimal" }
                    }
                }
            };

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
                Segments = new IConfigurationSegment[] { seg1, seg2, seg3 }
            };

            var plan = new ModulePipelineRunner(new NullLogger()).Plan(spec);

            Assert.NotNull(plan.Formatting);
            Assert.True(plan.Formatting!.Options.UpdateProjectRoot);
            Assert.NotNull(plan.Formatting.Options.Merge.FormatCodePSD1);
            Assert.True(plan.Formatting.Options.Merge.FormatCodePSD1!.Enabled);
            Assert.NotNull(plan.Formatting.Options.Standard.FormatCodePSD1);
            Assert.True(plan.Formatting.Options.Standard.FormatCodePSD1!.Enabled);
            Assert.Equal("Minimal", plan.Formatting.Options.Standard.Style?.PSD1);
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

