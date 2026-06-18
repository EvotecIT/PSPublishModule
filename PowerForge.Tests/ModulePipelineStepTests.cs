using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePipelineStepTests
{
    [Fact]
    public void Create_IncludesBuildSubsteps()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

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
                Segments = Array.Empty<IConfigurationSegment>()
            };

            var plan = new ModulePipelineRunner(new NullLogger()).Plan(spec);
            var steps = ModulePipelineStep.Create(plan);

            Assert.Contains(steps, s => s.Key == "build:stage");
            Assert.Contains(steps, s => s.Key == "build:build");
            Assert.Contains(steps, s => s.Key == "build:manifest");
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Create_IncludesLifecycleActionStepsAtConfiguredStages()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

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
                    new ConfigurationActionSegment
                    {
                        Configuration = new ModulePipelineActionConfiguration
                        {
                            Name = "Inspect staged module",
                            At = ModulePipelineActionStage.AfterStaging,
                            InlineScript = "$ctx = Get-Content $env:POWERFORGE_CONTEXT | ConvertFrom-Json"
                        }
                    },
                    new ConfigurationActionSegment
                    {
                        Configuration = new ModulePipelineActionConfiguration
                        {
                            Name = "Release guard",
                            At = ModulePipelineActionStage.BeforePublish,
                            FilePath = ".\\Build\\Test-ReleaseReady.ps1"
                        }
                    }
                }
            };

            var plan = new ModulePipelineRunner(new NullLogger()).Plan(spec);
            var steps = ModulePipelineStep.Create(plan);

            var idxStage = Array.FindIndex(steps, s => s.Key == "build:stage");
            var idxAfterStageAction = Array.FindIndex(steps, s => s.Key == "action:AfterStaging:01");
            var idxBuild = Array.FindIndex(steps, s => s.Key == "build:build");
            var idxBeforePublishAction = Array.FindIndex(steps, s => s.Key == "action:BeforePublish:01");

            Assert.True(idxStage >= 0);
            Assert.True(idxAfterStageAction >= 0);
            Assert.True(idxBuild >= 0);
            Assert.True(idxBeforePublishAction >= 0);
            Assert.True(idxStage < idxAfterStageAction);
            Assert.True(idxAfterStageAction < idxBuild);
            Assert.Equal(ModulePipelineStepKind.Action, steps[idxAfterStageAction].Kind);
            Assert.Equal("Inspect staged module", steps[idxAfterStageAction].ActionSegment?.Configuration.Name);
            Assert.True(idxBuild < idxBeforePublishAction);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Create_IncludesXcodeProjectVersionStep_WhenConfigured()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

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
                            MarketingVersion = "1.0.0"
                        }
                    }
                }
            };

            var plan = new ModulePipelineRunner(new NullLogger()).Plan(spec);
            var steps = ModulePipelineStep.Create(plan);

            var idxXcode = Array.FindIndex(steps, s => s.Key == "version:xcode:01");
            var idxStage = Array.FindIndex(steps, s => s.Key == "build:stage");

            Assert.True(idxXcode >= 0);
            Assert.True(idxStage >= 0);
            Assert.True(idxXcode < idxStage);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Create_IncludesAppleAppStep_WhenConfigured()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

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
                    new ConfigurationAppleAppSegment
                    {
                        Configuration = new AppleAppConfiguration
                        {
                            Name = "Tactra",
                            ProjectPath = "Tactra.xcodeproj",
                            UseResolvedVersion = true
                        }
                    }
                }
            };

            var plan = new ModulePipelineRunner(new NullLogger()).Plan(spec);
            var steps = ModulePipelineStep.Create(plan);

            var idxApple = Array.FindIndex(steps, s => s.Key == "version:apple:01");
            var idxStage = Array.FindIndex(steps, s => s.Key == "build:stage");

            Assert.True(idxApple >= 0);
            Assert.True(idxStage >= 0);
            Assert.True(idxApple < idxStage);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Create_IncludesPackageBuildStepsBeforeModuleBuildSteps()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

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
                    new ConfigurationProjectBuildSegment
                    {
                        Configuration = new ProjectBuildConfigurationReference
                        {
                            ConfigPath = "Build/project.build.json",
                            BuildBeforeModule = true
                        }
                    },
                    new ConfigurationPackageBuildSegment
                    {
                        Configuration = new PackageBuildConfiguration
                        {
                            Name = "Inline",
                            BuildBeforeModule = true
                        }
                    }
                }
            };

            var plan = new ModulePipelineRunner(new NullLogger()).Plan(spec);
            var steps = ModulePipelineStep.Create(plan);

            var idxProject = Array.FindIndex(steps, s => s.Key == "package:project:01");
            var idxInline = Array.FindIndex(steps, s => s.Key == "package:inline:01");
            var idxStage = Array.FindIndex(steps, s => s.Key == "build:stage");

            Assert.True(idxProject >= 0);
            Assert.True(idxInline >= 0);
            Assert.True(idxStage >= 0);
            Assert.True(idxProject < idxStage);
            Assert.True(idxInline < idxStage);
            Assert.Equal(ModulePipelineStepKind.PackageBuild, steps[idxProject].Kind);
            Assert.Equal(ModulePipelineStepKind.PackageBuild, steps[idxInline].Kind);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Create_SkipsDisabledAppleAndXcodeVersioningSteps()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

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
                    new ConfigurationAppleAppSegment
                    {
                        Configuration = new AppleAppConfiguration
                        {
                            Enabled = false,
                            ProjectPath = "Tactra.xcodeproj",
                            UseResolvedVersion = true
                        }
                    },
                    new ConfigurationXcodeProjectVersionSegment
                    {
                        Configuration = new XcodeProjectVersionConfiguration
                        {
                            Enabled = false,
                            Path = "Tactra.xcodeproj",
                            UseResolvedVersion = true
                        }
                    }
                }
            };

            var plan = new ModulePipelineRunner(new NullLogger()).Plan(spec);
            var steps = ModulePipelineStep.Create(plan);

            Assert.Empty(plan.AppleApps);
            Assert.Empty(plan.XcodeProjectVersions);
            Assert.DoesNotContain(steps, s => s.Key.StartsWith("version:apple:", StringComparison.Ordinal));
            Assert.DoesNotContain(steps, s => s.Key.StartsWith("version:xcode:", StringComparison.Ordinal));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Create_IncludesDocsSubsteps_WhenDocsEnabled()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

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
                    }
                }
            };

            var plan = new ModulePipelineRunner(new NullLogger()).Plan(spec);
            var steps = ModulePipelineStep.Create(plan);

            Assert.Contains(steps, s => s.Key == "docs:extract");
            Assert.Contains(steps, s => s.Key == "docs:write");
            Assert.Contains(steps, s => s.Key == "docs:maml");
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Create_OmitsExternalHelpStep_WhenExternalHelpDisabled()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

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
                            GenerateExternalHelp = false
                        }
                    }
                }
            };

            var plan = new ModulePipelineRunner(new NullLogger()).Plan(spec);
            var steps = ModulePipelineStep.Create(plan);

            Assert.Contains(steps, s => s.Key == "docs:extract");
            Assert.Contains(steps, s => s.Key == "docs:write");
            Assert.DoesNotContain(steps, s => s.Key == "docs:maml");
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Create_IncludesValidationSteps_WhenEnabled()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

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
                    new ConfigurationFileConsistencySegment
                    {
                        Settings = new FileConsistencySettings { Enable = true }
                    },
                    new ConfigurationCompatibilitySegment
                    {
                        Settings = new CompatibilitySettings { Enable = true }
                    }
                }
            };

            var plan = new ModulePipelineRunner(new NullLogger()).Plan(spec);
            var steps = ModulePipelineStep.Create(plan);

            var idxConsistency = Array.FindIndex(steps, s => s.Key == "validate:fileconsistency");
            var idxCompatibility = Array.FindIndex(steps, s => s.Key == "validate:compatibility");

            Assert.True(idxConsistency >= 0);
            Assert.True(idxCompatibility >= 0);
            Assert.True(idxConsistency < idxCompatibility);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Create_IncludesBinaryDependencyPreflightStep_WhenImportingSelf()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

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
                    new ConfigurationImportModulesSegment
                    {
                        ImportModules = new ImportModulesConfiguration
                        {
                            Self = true
                        }
                    }
                }
            };

            var plan = new ModulePipelineRunner(new NullLogger()).Plan(spec);
            var steps = ModulePipelineStep.Create(plan);

            Assert.Contains(steps, s => s.Key == "tests:binary-dependencies");
            Assert.Contains(steps, s => s.Key == "tests:import-modules");
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Create_OmitsBinaryDependencyPreflightStep_WhenSkipped()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

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
                    new ConfigurationImportModulesSegment
                    {
                        ImportModules = new ImportModulesConfiguration
                        {
                            Self = true,
                            SkipBinaryDependencyCheck = true
                        }
                    }
                }
            };

            var plan = new ModulePipelineRunner(new NullLogger()).Plan(spec);
            var steps = ModulePipelineStep.Create(plan);

            Assert.DoesNotContain(steps, s => s.Key == "tests:binary-dependencies");
            Assert.Contains(steps, s => s.Key == "tests:import-modules");
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Create_IncludesBinaryConflictAnalysisStep_WhenImportingRequiredModules()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

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
                    new ConfigurationImportModulesSegment
                    {
                        ImportModules = new ImportModulesConfiguration
                        {
                            RequiredModules = true
                        }
                    }
                }
            };

            var plan = new ModulePipelineRunner(new NullLogger()).Plan(spec);
            var steps = ModulePipelineStep.Create(plan);

            var idxAnalysis = Array.FindIndex(steps, s => s.Key == "validate:binary-conflicts");
            var idxImport = Array.FindIndex(steps, s => s.Key == "tests:import-modules");

            Assert.True(idxAnalysis >= 0);
            Assert.True(idxImport >= 0);
            Assert.True(idxAnalysis < idxImport);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Create_OmitsBinaryConflictAnalysisStep_WhenSkipped()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

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
                    new ConfigurationImportModulesSegment
                    {
                        ImportModules = new ImportModulesConfiguration
                        {
                            RequiredModules = true,
                            AnalyzeBinaryConflicts = false
                        }
                    }
                }
            };

            var plan = new ModulePipelineRunner(new NullLogger()).Plan(spec);
            var steps = ModulePipelineStep.Create(plan);

            Assert.DoesNotContain(steps, s => s.Key == "validate:binary-conflicts");
            Assert.Contains(steps, s => s.Key == "tests:import-modules");
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
