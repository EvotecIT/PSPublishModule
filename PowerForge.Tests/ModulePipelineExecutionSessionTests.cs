using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge.Tests;

public sealed class ModulePipelineExecutionSessionTests
{
    [Fact]
    public void Create_ResolvesNamedStepsAndSegmentLookups()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(CreateSpec(root.FullName, moduleName));
            var session = ModulePipelineExecutionSession.Create(plan);

            Assert.NotNull(session.StageStep);
            Assert.NotNull(session.BuildStep);
            Assert.NotNull(session.ManifestStep);
            Assert.NotNull(session.DocsExtractStep);
            Assert.NotNull(session.DocsWriteStep);
            Assert.NotNull(session.DocsMamlStep);
            Assert.NotNull(session.FormatStagingStep);
            Assert.NotNull(session.FormatProjectStep);
            Assert.NotNull(session.FileConsistencyStep);
            Assert.NotNull(session.ProjectFileConsistencyStep);
            Assert.NotNull(session.CompatibilityStep);
            Assert.NotNull(session.ModuleValidationStep);
            Assert.NotNull(session.BinaryConflictAnalysisStep);
            Assert.NotNull(session.BinaryDependenciesStep);
            Assert.NotNull(session.ImportModulesStep);
            Assert.NotNull(session.SignStep);
            Assert.NotNull(session.InstallStep);
            Assert.NotNull(session.CleanupStep);
            Assert.Single(session.TestSteps);

            var artefactStep = session.GetArtefactStep(plan.Artefacts[0]);
            Assert.NotNull(artefactStep);
            Assert.Equal(ModulePipelineStepKind.Artefact, artefactStep!.Kind);

            var firstPublishStep = session.GetPublishStep(plan.Publishes[0]);
            var secondPublishStep = session.GetPublishStep(plan.Publishes[1]);
            Assert.NotNull(firstPublishStep);
            Assert.NotNull(secondPublishStep);
            Assert.Equal(ModulePipelineStepKind.Publish, firstPublishStep!.Kind);
            Assert.Equal(ModulePipelineStepKind.Publish, secondPublishStep!.Kind);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void NotifySkippedOnFailure_SkipsOnlyStepsThatNeverStarted()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var reporter = new RecordingProgressReporter();
            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(CreateSpec(root.FullName, moduleName));
            var session = ModulePipelineExecutionSession.Create(plan, reporter);

            session.Start(session.StageStep);
            session.Done(session.StageStep);
            session.Start(session.BuildStep);
            session.Fail(session.BuildStep, new InvalidOperationException("boom"));
            session.NotifySkippedOnFailure();

            Assert.Contains("build:stage", reporter.Started);
            Assert.Contains("build:stage", reporter.Completed);
            Assert.Contains("build:build", reporter.Started);
            Assert.Contains("build:build", reporter.Failed);
            Assert.DoesNotContain("build:stage", reporter.Skipped);
            Assert.DoesNotContain("build:build", reporter.Skipped);
            Assert.Contains("build:manifest", reporter.Skipped);
            Assert.Contains("docs:extract", reporter.Skipped);
            Assert.Contains("cleanup", reporter.Skipped);
            Assert.Equal(
                session.Steps.Select(static step => step.Key).Where(static key => key != "build:stage" && key != "build:build").OrderBy(static key => key).ToArray(),
                reporter.Skipped.OrderBy(static key => key).ToArray());
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    private static ModulePipelineSpec CreateSpec(string rootPath, string moduleName)
    {
        return new ModulePipelineSpec
        {
            Build = new ModuleBuildSpec
            {
                Name = moduleName,
                SourcePath = rootPath,
                Version = "1.0.0",
                CsprojPath = null
            },
            Install = new ModulePipelineInstallOptions
            {
                Enabled = true,
                Strategy = InstallationStrategy.AutoRevision,
                KeepVersions = 2
            },
            Segments = new IConfigurationSegment[]
            {
                new ConfigurationDocumentationSegment
                {
                    Configuration = new DocumentationConfiguration
                    {
                        Path = "Docs",
                        PathReadme = "Docs\\Readme.md"
                    }
                },
                new ConfigurationBuildDocumentationSegment
                {
                    Configuration = new BuildDocumentationConfiguration
                    {
                        Enable = true,
                        GenerateExternalHelp = true
                    }
                },
                new ConfigurationFormattingSegment
                {
                    Options = new FormattingOptions
                    {
                        UpdateProjectRoot = true
                    }
                },
                new ConfigurationFileConsistencySegment
                {
                    Settings = new FileConsistencySettings
                    {
                        Enable = true,
                        UpdateProjectRoot = true
                    }
                },
                new ConfigurationCompatibilitySegment
                {
                    Settings = new CompatibilitySettings
                    {
                        Enable = true
                    }
                },
                new ConfigurationValidationSegment
                {
                    Settings = new ModuleValidationSettings
                    {
                        Enable = true
                    }
                },
                new ConfigurationImportModulesSegment
                {
                    ImportModules = new ImportModulesConfiguration
                    {
                        Self = true,
                        RequiredModules = true
                    }
                },
                new ConfigurationBuildSegment
                {
                    BuildModule = new BuildModuleConfiguration
                    {
                        SignMerged = true
                    }
                },
                new ConfigurationTestSegment
                {
                    Configuration = new TestConfiguration
                    {
                        TestsPath = "Tests"
                    }
                },
                new ConfigurationArtefactSegment
                {
                    ArtefactType = ArtefactType.Unpacked,
                    Configuration = new ArtefactConfiguration
                    {
                        Enabled = true,
                        Path = "Artefacts\\Unpacked"
                    }
                },
                new ConfigurationPublishSegment
                {
                    Configuration = new PublishConfiguration
                    {
                        Destination = PublishDestination.PowerShellGallery,
                        Enabled = true,
                        RepositoryName = "PSGallery"
                    }
                },
                new ConfigurationPublishSegment
                {
                    Configuration = new PublishConfiguration
                    {
                        Destination = PublishDestination.GitHub,
                        Enabled = true,
                        ID = "GitHubRelease",
                        UserName = "EvotecIT"
                    }
                }
            }
        };
    }

    private static void WriteMinimalModule(string moduleRoot, string moduleName, string version)
    {
        Directory.CreateDirectory(moduleRoot);
        Directory.CreateDirectory(Path.Combine(moduleRoot, "Tests"));
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

    private sealed class RecordingProgressReporter : IModulePipelineProgressReporterV2
    {
        public List<string> Started { get; } = new();
        public List<string> Completed { get; } = new();
        public List<string> Failed { get; } = new();
        public List<string> Skipped { get; } = new();

        public void StepStarting(ModulePipelineStep step)
        {
            Started.Add(step.Key);
        }

        public void StepCompleted(ModulePipelineStep step)
        {
            Completed.Add(step.Key);
        }

        public void StepFailed(ModulePipelineStep step, Exception error)
        {
            Failed.Add(step.Key);
        }

        public void StepSkipped(ModulePipelineStep step)
        {
            Skipped.Add(step.Key);
        }
    }
}
