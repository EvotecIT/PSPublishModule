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
