using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePipelineRefreshManifestOnlyTests
{
    [Fact]
    public void Plan_RefreshPSD1Only_DisablesNonManifestPhases()
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
                    CsprojPath = Path.Combine(root.FullName, "Sources", moduleName, moduleName + ".csproj")
                },
                Install = new ModulePipelineInstallOptions { Enabled = true },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildSegment
                    {
                        BuildModule = new BuildModuleConfiguration
                        {
                            RefreshPSD1Only = true,
                            Merge = true,
                            SignMerged = true,
                            InstallMissingModules = true
                        }
                    }
                }
            };

            var plan = new ModulePipelineRunner(new NullLogger()).Plan(spec);

            Assert.True(plan.BuildSpec.RefreshManifestOnly);
            Assert.True(string.IsNullOrWhiteSpace(plan.BuildSpec.CsprojPath));
            Assert.False(plan.MergeModule);
            Assert.False(plan.MergeMissing);
            Assert.False(plan.SignModule);
            Assert.False(plan.InstallEnabled);
            Assert.False(plan.InstallMissingModules);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Run_RefreshPSD1Only_SkipsInstallAndPublishing()
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
                    KeepStaging = true
                },
                Install = new ModulePipelineInstallOptions { Enabled = true },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildSegment
                    {
                        BuildModule = new BuildModuleConfiguration
                        {
                            RefreshPSD1Only = true,
                            Merge = true,
                            SignMerged = true
                        }
                    }
                }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);
            var result = runner.Run(spec, plan);

            Assert.NotNull(result.BuildResult);
            Assert.NotNull(result.BuildResult.ManifestPath);
            Assert.True(File.Exists(result.BuildResult.ManifestPath));
            Assert.Null(result.InstallResult);
            Assert.Empty(result.PublishResults);
            Assert.Empty(result.ArtefactResults);
            Assert.Null(result.SigningResult);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Run_RefreshPSD1Only_UpdatesProjectRootManifest()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var projectManifest = Path.Combine(root.FullName, moduleName + ".psd1");
            Assert.True(ManifestEditor.TrySetTopLevelString(projectManifest, "Author", "OldAuthor"));

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "2.0.0",
                    KeepStaging = true
                },
                Install = new ModulePipelineInstallOptions { Enabled = true },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationManifestSegment
                    {
                        Configuration = new ManifestConfiguration
                        {
                            ModuleVersion = "2.0.0",
                            Guid = "22222222-2222-2222-2222-222222222222",
                            Author = "NewAuthor",
                            CompatiblePSEditions = new[] { "Desktop", "Core" }
                        }
                    },
                    new ConfigurationBuildSegment
                    {
                        BuildModule = new BuildModuleConfiguration
                        {
                            RefreshPSD1Only = true
                        }
                    }
                }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);
            var result = runner.Run(spec, plan);

            Assert.NotNull(result.BuildResult);
            Assert.True(File.Exists(projectManifest));
            Assert.True(ManifestEditor.TryGetTopLevelString(projectManifest, "ModuleVersion", out var projectVersion));
            Assert.Equal("2.0.0", projectVersion);
            Assert.True(ManifestEditor.TryGetTopLevelString(projectManifest, "Author", out var projectAuthor));
            Assert.Equal("NewAuthor", projectAuthor);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static void WriteMinimalModule(string rootPath, string moduleName, string moduleVersion)
    {
        File.WriteAllText(Path.Combine(rootPath, moduleName + ".psm1"), "function Test-Example { 'ok' }");
        File.WriteAllText(Path.Combine(rootPath, moduleName + ".psd1"),
            "@{" + Environment.NewLine +
            "    RootModule = '" + moduleName + ".psm1'" + Environment.NewLine +
            "    ModuleVersion = '" + moduleVersion + "'" + Environment.NewLine +
            "    GUID = '11111111-1111-1111-1111-111111111111'" + Environment.NewLine +
            "    Author = 'Tests'" + Environment.NewLine +
            "    CompanyName = 'Tests'" + Environment.NewLine +
            "    Description = 'Test module'" + Environment.NewLine +
            "    FunctionsToExport = @('*')" + Environment.NewLine +
            "    CmdletsToExport = @()" + Environment.NewLine +
            "    VariablesToExport = @('*')" + Environment.NewLine +
            "    AliasesToExport = @('*')" + Environment.NewLine +
            "}");
    }
}
