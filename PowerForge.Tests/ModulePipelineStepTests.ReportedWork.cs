using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineStepTests
{
    [Theory]
    [InlineData(AssemblyTypeAcceleratorExportMode.None)]
    [InlineData(AssemblyTypeAcceleratorExportMode.Assembly)]
    public void Create_PlansDependencyAndTypeAcceleratorWorkAsSteps(AssemblyTypeAcceleratorExportMode typeAccelerators)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec { Name = moduleName, SourcePath = root.FullName, Version = "1.0.0", CsprojPath = null },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = Array.Empty<IConfigurationSegment>()
            };

            var plan = new ModulePipelineRunner(new NullLogger()).Plan(spec);
            plan.BuildSpec.AssemblyTypeAcceleratorMode = typeAccelerators;
            var keys = Array.ConvertAll(ModulePipelineStep.Create(plan), static step => step.Key);

            // Dependency preparation runs before staging; the report runs after the module is final and before docs.
            Assert.InRange(Array.IndexOf(keys, "deps:install"), 0, Array.IndexOf(keys, "build:stage") - 1);
            var report = Array.IndexOf(keys, "build:type-accelerators");
            if (typeAccelerators == AssemblyTypeAcceleratorExportMode.None)
                Assert.Equal(-1, report);
            else
                Assert.True(report > Array.IndexOf(keys, "build:manifest"));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData(ConfigurationGateMode.Build, "Build NuGet packages (Lane)")]
    [InlineData(ConfigurationGateMode.Publish, "Build and publish NuGet packages (Lane)")]
    public void Create_NamesPackageLaneByWhatTheRunDoes(ConfigurationGateMode mode, string expected)
    {
        var steps = CreateStepsWithPackageLane(mode, synchronizeModuleVersion: false);
        var lane = Assert.Single(steps, static step => step.Kind == ModulePipelineStepKind.PackageBuild);

        // A build-only run must not say it publishes.
        Assert.Equal(expected, lane.Title);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Create_ListsPackageLaneWhereTheRunExecutesIt(bool synchronizeModuleVersion)
    {
        var keys = Array.ConvertAll(
            CreateStepsWithPackageLane(ConfigurationGateMode.Build, synchronizeModuleVersion),
            static step => step.Key);

        // The runner builds packages before preparing dependencies only when they supply the module version.
        var lane = Array.IndexOf(keys, "package:project:01");
        var dependencies = Array.IndexOf(keys, "deps:install");
        Assert.Equal(synchronizeModuleVersion, lane < dependencies);
    }

    private static ModulePipelineStep[] CreateStepsWithPackageLane(ConfigurationGateMode mode, bool synchronizeModuleVersion)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var projectConfig = Path.Combine(root.FullName, "project.build.json");
            File.WriteAllText(projectConfig, "{}");

            var segments = new System.Collections.Generic.List<IConfigurationSegment>
            {
                new ConfigurationGateSegment { Configuration = new GateConfiguration { Mode = mode } },
                new ConfigurationProjectBuildSegment
                {
                    Configuration = new ProjectBuildConfigurationReference
                    {
                        Name = "Lane",
                        ConfigPath = projectConfig,
                        BuildBeforeModule = true,
                        PublishNuget = true,
                        UseAsReleaseVersionSource = synchronizeModuleVersion
                    }
                }
            };
            if (synchronizeModuleVersion)
            {
                segments.Add(new ConfigurationReleaseSegment
                {
                    Configuration = new ReleaseConfiguration
                    {
                        VersionSource = ReleaseVersionSource.ProjectBuild,
                        PrimaryProject = moduleName,
                        SynchronizeModuleVersion = true
                    }
                });
            }

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec { Name = moduleName, SourcePath = root.FullName, Version = "1.0.0", CsprojPath = null },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = segments.ToArray()
            };

            return ModulePipelineStep.Create(new ModulePipelineRunner(new NullLogger()).Plan(spec));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}
