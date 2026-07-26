using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineUnifiedReleaseTests
{
    [Theory]
    [InlineData(ConfigurationGateMode.Manifest)]
    [InlineData(ConfigurationGateMode.Documentation)]
    public void Plan_NonReleaseGateIgnoresInvalidSynchronizationConfiguration(ConfigurationGateMode gateMode)
    {
        var plan = PlanWithInvalidSynchronizationConfiguration(gateMode, refreshPsd1Only: false);

        Assert.Null(plan.Release);
        Assert.Equal("1.0.0", plan.ResolvedVersion);
    }

    [Fact]
    public void Plan_RefreshPsd1OnlyIgnoresInvalidSynchronizationConfiguration()
    {
        var plan = PlanWithInvalidSynchronizationConfiguration(gateMode: null, refreshPsd1Only: true);

        Assert.Null(plan.Release);
        Assert.Equal("1.0.0", plan.ResolvedVersion);
    }

    private static ModulePipelinePlan PlanWithInvalidSynchronizationConfiguration(
        ConfigurationGateMode? gateMode,
        bool refreshPsd1Only)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var segments = new List<IConfigurationSegment>();
            if (gateMode.HasValue)
            {
                segments.Add(new ConfigurationGateSegment
                {
                    Configuration = new GateConfiguration { Mode = gateMode.Value }
                });
            }

            if (refreshPsd1Only)
            {
                segments.Add(new ConfigurationBuildSegment
                {
                    BuildModule = new BuildModuleConfiguration { RefreshPSD1Only = true }
                });
            }

            segments.Add(new ConfigurationReleaseSegment
            {
                Configuration = new ReleaseConfiguration
                {
                    VersionSource = ReleaseVersionSource.Module,
                    SynchronizeModuleVersion = true
                }
            });

            return new ModulePipelineRunner(new NullLogger()).Plan(new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0"
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = segments.ToArray()
            });
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }
}
