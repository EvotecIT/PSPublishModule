using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineUnifiedReleaseTests
{
    [Fact]
    public void Run_BuildGate_DoesNotRunPublishLifecycleActions()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var stagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "2.0.10");
            var events = new List<string>();
            var hosted = new FakeHostedOperations(events)
            {
                ModuleAction = (action, context) =>
                {
                    events.Add(context.Stage.ToString());
                    return CreateActionResult(action, context, succeeded: true);
                }
            };
            var runner = CreateRunner(
                hosted,
                (_, _, _) => throw new InvalidOperationException("Package builds should not run."));
            var progress = new PublishGateProgressReporter();
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "2.0.10",
                    StagingPath = stagingPath
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationActionSegment
                    {
                        Configuration = new ModulePipelineActionConfiguration
                        {
                            At = ModulePipelineActionStage.BeforePublish,
                            InlineScript = "Write-Output ignored"
                        }
                    },
                    new ConfigurationActionSegment
                    {
                        Configuration = new ModulePipelineActionConfiguration
                        {
                            At = ModulePipelineActionStage.AfterPublish,
                            InlineScript = "Write-Output ignored"
                        }
                    },
                    new ConfigurationGateSegment
                    {
                        Configuration = new GateConfiguration { Mode = ConfigurationGateMode.Build }
                    }
                }
            };

            ModulePipelinePlan plan = runner.Plan(spec);
            var result = runner.Run(spec, plan, progress);

            Assert.Empty(events);
            Assert.Empty(result.ActionResults);
            Assert.Contains("action:BeforePublish:01", progress.Skipped);
            Assert.Contains("action:AfterPublish:01", progress.Skipped);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(stagingPath)) Directory.Delete(stagingPath, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_PreservesDependencyActionOrderingWithoutVersionSynchronization()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var stagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "2.0.10");
            WriteSynchronizedProjectBuildConfig(root.FullName, "project.build.json", moduleName, publishNuGet: false);

            var events = new List<string>();
            var hosted = new FakeHostedOperations(events)
            {
                ModuleAction = (action, context) =>
                {
                    events.Add("action");
                    return CreateActionResult(action, context, succeeded: true);
                }
            };
            var runner = CreateRunner(
                hosted,
                (request, configuration, configPath) =>
                {
                    events.Add("package");
                    return CreateProjectBuildResult(
                        root.FullName,
                        moduleName,
                        "2.0.11",
                        Path.Combine(root.FullName, "Artifacts", "NuGet"),
                        request,
                        configPath,
                        includePackage: false);
                });
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "2.0.10",
                    StagingPath = stagingPath
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    CreateProjectBuildSegment(
                        moduleName,
                        enabled: true,
                        buildBeforeModule: true,
                        configPath: Path.Combine("Build", "project.build.json")),
                    new ConfigurationActionSegment
                    {
                        Configuration = new ModulePipelineActionConfiguration
                        {
                            Enabled = true,
                            Name = "BeforeDependencies",
                            At = ModulePipelineActionStage.BeforeDependencies,
                            InlineScript = "Write-Output ignored"
                        }
                    }
                }
            };

            runner.Run(spec);

            Assert.Equal(new[] { "action", "package" }, events);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(stagingPath)) Directory.Delete(stagingPath, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_ResolvesSynchronizedVersionBeforeDependencyActions()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var stagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "TestModule";
            const string synchronizedVersion = "2.0.11";
            WriteMinimalModule(root.FullName, moduleName, "2.0.10");
            WriteSynchronizedProjectBuildConfig(root.FullName, "project.build.json", moduleName, publishNuGet: false);

            string? actionVersion = null;
            var hosted = new FakeHostedOperations(new List<string>())
            {
                ModuleAction = (action, context) =>
                {
                    actionVersion = context.ResolvedVersion;
                    return CreateActionResult(action, context, succeeded: true);
                }
            };
            var runner = CreateRunner(
                hosted,
                (request, configuration, configPath) => CreateProjectBuildResult(
                    root.FullName,
                    moduleName,
                    synchronizedVersion,
                    Path.Combine(root.FullName, "Artifacts", "NuGet"),
                    request,
                    configPath,
                    includePackage: false));
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "2.0.10",
                    StagingPath = stagingPath
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    CreateProjectBuildSegment(
                        moduleName,
                        enabled: true,
                        buildBeforeModule: true,
                        configPath: Path.Combine("Build", "project.build.json")),
                    new ConfigurationActionSegment
                    {
                        Configuration = new ModulePipelineActionConfiguration
                        {
                            Enabled = true,
                            Name = "BeforeDependencies",
                            At = ModulePipelineActionStage.BeforeDependencies,
                            InlineScript = "Write-Output ignored"
                        }
                    },
                    new ConfigurationReleaseSegment
                    {
                        Configuration = new ReleaseConfiguration
                        {
                            VersionSource = ReleaseVersionSource.ProjectBuild,
                            PrimaryProject = moduleName,
                            SynchronizeModuleVersion = true
                        }
                    }
                }
            };

            var result = runner.Run(spec);

            Assert.Equal(synchronizedVersion, result.Plan.ResolvedVersion);
            Assert.Equal(synchronizedVersion, actionVersion);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(stagingPath)) Directory.Delete(stagingPath, recursive: true); } catch { }
        }
    }

    private sealed class PublishGateProgressReporter : IModulePipelineProgressReporterV2
    {
        internal List<string> Skipped { get; } = new();

        public void StepStarting(ModulePipelineStep step) { }

        public void StepCompleted(ModulePipelineStep step) { }

        public void StepFailed(ModulePipelineStep step, Exception error) { }

        public void StepSkipped(ModulePipelineStep step) => Skipped.Add(step.Key);
    }
}
