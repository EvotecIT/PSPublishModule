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
    public void Plan_RefreshPSD1Only_PreservesResolvedCsprojPath_ForVersionSync()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var csprojPath = Path.Combine(root.FullName, "Sources", moduleName, moduleName + ".csproj");
            WriteMinimalCsproj(csprojPath, "1.0.0");

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "2.0.0",
                    CsprojPath = csprojPath
                },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildSegment
                    {
                        BuildModule = new BuildModuleConfiguration
                        {
                            RefreshPSD1Only = true,
                            SyncNETProjectVersion = true
                        }
                    }
                }
            };

            var plan = new ModulePipelineRunner(new NullLogger()).Plan(spec);

            Assert.True(plan.BuildSpec.RefreshManifestOnly);
            Assert.True(string.IsNullOrWhiteSpace(plan.BuildSpec.CsprojPath));
            Assert.Equal(csprojPath, plan.ResolvedCsprojPath);
            Assert.True(plan.SyncNETProjectVersion);
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
    public void Run_RefreshPSD1Only_SyncsSourceCsprojVersion_WhenEnabled()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var csprojPath = Path.Combine(root.FullName, "Sources", moduleName, moduleName + ".csproj");
            WriteMinimalCsproj(csprojPath, "1.0.0");

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "2.0.0",
                    KeepStaging = true,
                    CsprojPath = csprojPath
                },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildSegment
                    {
                        BuildModule = new BuildModuleConfiguration
                        {
                            RefreshPSD1Only = true,
                            SyncNETProjectVersion = true
                        }
                    }
                }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);
            var result = runner.Run(spec, plan);

            Assert.NotNull(result.BuildResult);
            Assert.True(CsprojVersionEditor.TryGetVersion(csprojPath, out var syncedVersion));
            Assert.Equal("2.0.0", syncedVersion);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Run_RefreshPSD1Only_WithSyncEnabledAndNoCsproj_Throws()
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
                    Version = "2.0.0",
                    KeepStaging = true
                },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildSegment
                    {
                        BuildModule = new BuildModuleConfiguration
                        {
                            RefreshPSD1Only = true,
                            SyncNETProjectVersion = true
                        }
                    }
                }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);

            var ex = Assert.Throws<InvalidOperationException>(() => runner.Run(spec, plan));
            Assert.Contains("SyncNETProjectVersion", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Run_RefreshPSD1Only_SyncEnabled_DoesNotRewriteCsprojWhenAlreadyInSync()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var csprojPath = Path.Combine(root.FullName, "Sources", moduleName, moduleName + ".csproj");
            WriteMinimalCsproj(csprojPath, "2.0.0");
            var expectedTimestamp = new DateTime(2024, 06, 01, 12, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(csprojPath, expectedTimestamp);

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "2.0.0",
                    KeepStaging = true,
                    CsprojPath = csprojPath
                },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildSegment
                    {
                        BuildModule = new BuildModuleConfiguration
                        {
                            RefreshPSD1Only = true,
                            SyncNETProjectVersion = true
                        }
                    }
                }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);
            var result = runner.Run(spec, plan);

            Assert.NotNull(result.BuildResult);
            Assert.Equal(expectedTimestamp, File.GetLastWriteTimeUtc(csprojPath));
            Assert.True(CsprojVersionEditor.TryGetVersion(csprojPath, out var syncedVersion));
            Assert.Equal("2.0.0", syncedVersion);
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
            Assert.True(ManifestEditor.TrySetRequiredModules(projectManifest, new[]
            {
                new RequiredModuleReference("PSSharedGoods", moduleVersion: "0.0.312", guid: "e272aa8-baaa-4edf-9f45-b6d6f7d844fe")
            }));

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
            Assert.True(ManifestEditor.TryGetRequiredModules(projectManifest, out RequiredModuleReference[]? projectRequiredModules));
            var required = Assert.Single(projectRequiredModules!);
            Assert.Equal("PSSharedGoods", required.ModuleName);
            Assert.Equal("0.0.312", required.ModuleVersion);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Run_RefreshPSD1Only_ClearsStalePrereleaseWhenManifestSegmentOmitsIt()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var projectManifest = Path.Combine(root.FullName, moduleName + ".psd1");
            Assert.True(ManifestEditor.TrySetTopLevelString(projectManifest, "Prerelease", "Preview1"));

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0",
                    KeepStaging = true
                },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationManifestSegment
                    {
                        Configuration = new ManifestConfiguration
                        {
                            ModuleVersion = "1.0.0",
                            Author = "Tests"
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
            Assert.Null(plan.PreRelease);

            var result = runner.Run(spec, plan);

            Assert.NotNull(result.BuildResult);
            Assert.False(ManifestEditor.TryGetTopLevelString(projectManifest, "Prerelease", out _));
            Assert.False(ManifestEditor.TryGetPsDataStringArray(projectManifest, "Prerelease", out _));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Run_NormalBuild_UpdatesProjectRootManifestOnly()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var libDefault = Directory.CreateDirectory(Path.Combine(root.FullName, "Lib", "Default"));
            File.WriteAllText(Path.Combine(libDefault.FullName, moduleName + ".dll"), "placeholder");

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
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationManifestSegment
                    {
                        Configuration = new ManifestConfiguration
                        {
                            ModuleVersion = "2.0.0",
                            Author = "NewAuthor"
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

            var projectPsm1 = Path.Combine(root.FullName, moduleName + ".psm1");
            var projectPsm1Content = File.ReadAllText(projectPsm1);
            Assert.DoesNotContain(moduleName + ".Libraries.ps1", projectPsm1Content, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(root.FullName, moduleName + ".Libraries.ps1")));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void SyncPublishedManifestToProjectRoot_RefreshesProjectRootManifestWithoutCopyingStagedFile()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var projectManifest = Path.Combine(root.FullName, moduleName + ".psd1");
            Assert.True(ManifestEditor.TrySetTopLevelString(projectManifest, "Author", "OldAuthor"));
            Assert.True(ManifestEditor.TrySetRequiredModules(projectManifest, new[]
            {
                new RequiredModuleReference("PSSharedGoods", moduleVersion: "0.0.312", guid: "e272aa8-baaa-4edf-9f45-b6d6f7d844fe")
            }));

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "2.0.0",
                    KeepStaging = true
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationManifestSegment
                    {
                        Configuration = new ManifestConfiguration
                        {
                            ModuleVersion = "2.0.0",
                            Author = "PublishedAuthor",
                            Prerelease = "Preview9"
                        }
                    }
                }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);
            var result = runner.Run(spec, plan);

            File.WriteAllText(result.BuildResult.ManifestPath, "@{ ModuleVersion = '9.9.9'; RootModule = 'Broken.psm1' }");

            runner.SyncPublishedManifestToProjectRoot(
                plan,
                new[]
                {
                    new ModulePublishResult(
                        destination: PublishDestination.PowerShellGallery,
                        repositoryName: "PSGallery",
                        userName: null,
                        tagName: null,
                        versionText: "2.0.0-Preview9",
                        isPreRelease: true,
                        assetPaths: Array.Empty<string>(),
                        releaseUrl: null,
                        succeeded: true,
                        errorMessage: null)
                });

            Assert.True(ManifestEditor.TryGetTopLevelString(projectManifest, "ModuleVersion", out var projectVersion));
            Assert.Equal("2.0.0", projectVersion);
            Assert.True(ManifestEditor.TryGetTopLevelString(projectManifest, "Author", out var projectAuthor));
            Assert.Equal("PublishedAuthor", projectAuthor);
            Assert.True(ManifestEditor.TryGetTopLevelString(projectManifest, "RootModule", out var rootModule));
            Assert.Equal("TestModule.psm1", rootModule);
            var projectManifestContent = File.ReadAllText(projectManifest);
            Assert.Contains("Prerelease", projectManifestContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Preview9", projectManifestContent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Broken.psm1", projectManifestContent, StringComparison.OrdinalIgnoreCase);
            Assert.True(ManifestEditor.TryGetRequiredModules(projectManifest, out RequiredModuleReference[]? projectRequiredModules));
            var required = Assert.Single(projectRequiredModules!);
            Assert.Equal("PSSharedGoods", required.ModuleName);
            Assert.Equal("0.0.312", required.ModuleVersion);
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

    private static void WriteMinimalCsproj(string csprojPath, string version)
    {
        var directory = Path.GetDirectoryName(csprojPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(
            csprojPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\">" + Environment.NewLine +
            "  <PropertyGroup>" + Environment.NewLine +
            "    <TargetFramework>net8.0</TargetFramework>" + Environment.NewLine +
            "    <VersionPrefix>" + version + "</VersionPrefix>" + Environment.NewLine +
            "  </PropertyGroup>" + Environment.NewLine +
            "</Project>");
    }
}
