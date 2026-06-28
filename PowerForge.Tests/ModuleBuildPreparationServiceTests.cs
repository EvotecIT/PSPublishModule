using System.Collections;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text.Json;
using System.Text.Json.Serialization;
using PowerForge;

namespace PowerForge.Tests;

public sealed class ModuleBuildPreparationServiceTests
{
    [Fact]
    public void Prepare_from_modern_request_builds_project_paths_and_spec()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-modulebuild-modern-" + Guid.NewGuid().ToString("N")));

        try
        {
            var request = new ModuleBuildPreparationRequest
            {
                ParameterSetName = "Modern",
                ModuleName = "SampleModule",
                InputPath = root.FullName,
                CurrentPath = root.FullName,
                ResolvePath = path => path,
                DotNetFramework = new[] { "net8.0" },
                DotNetFrameworkWasBound = false,
                Legacy = true,
                ExcludeDirectories = new[] { ".git", "bin" },
                ExcludeFiles = new[] { ".gitignore" },
                JsonOnly = true
            };

            var prepared = new ModuleBuildPreparationService().Prepare(request);

            Assert.Equal("SampleModule", prepared.ModuleName);
            Assert.Equal(Path.Combine(root.FullName, "SampleModule"), prepared.ProjectRoot);
            Assert.Equal(root.FullName, prepared.BasePathForScaffold);
            Assert.True(prepared.UseLegacy);
            Assert.Empty(prepared.PipelineSpec.Build.Frameworks);
            Assert.Contains(".gitignore", prepared.PipelineSpec.Build.ExcludeFiles);
            Assert.Contains("SampleModule.Tests.ps1", prepared.PipelineSpec.Build.ExcludeFiles);
            Assert.Equal(Path.Combine(root.FullName, "SampleModule", "powerforge.json"), prepared.JsonOutputPath);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Prepare_from_module_build_script_resolves_repo_level_paths_from_workspace_root()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-modulebuild-workspace-" + Guid.NewGuid().ToString("N")));

        try
        {
            var moduleRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "Module"));
            var scriptRoot = Directory.CreateDirectory(Path.Combine(moduleRoot.FullName, "Build"));
            File.WriteAllText(Path.Combine(moduleRoot.FullName, "DbaClientX.psd1"), "@{ ModuleVersion = '1.0.0' }");

            var settings = ScriptBlock.Create("""
[PowerForge.ConfigurationBuildLibrariesSegment]@{
    BuildLibraries = [PowerForge.BuildLibrariesConfiguration]@{
        NETProjectPath = 'DbaClientX.PowerShell/DbaClientX.PowerShell.csproj'
        NETDevelopmentBinariesPath = 'DbaClientX.PowerShell/bin'
    }
}
[PowerForge.ConfigurationProjectBuildSegment]@{
    Configuration = [PowerForge.ProjectBuildConfigurationReference]@{
        ConfigPath = 'Build/project.build.json'
        BuildBeforeModule = $true
    }
}
[PowerForge.ConfigurationArtefactSegment]@{
    ArtefactType = [PowerForge.ArtefactType]::Packed
    Configuration = [PowerForge.ArtefactConfiguration]@{
        Path = 'Module/Artefacts/Packed'
        RequiredModules = [PowerForge.ArtefactRequiredModulesConfiguration]@{
            Path = 'Module/Artefacts/Packed/RequiredModules'
            ModulesPath = 'Module/Artefacts/Packed/Modules'
        }
        DirectoryOutput = [PowerForge.ArtefactCopyMapping[]]@(
            [PowerForge.ArtefactCopyMapping]@{
                Source = 'Build/LICENSE'
                Destination = 'LICENSE'
            }
        )
        FilesOutput = [PowerForge.ArtefactCopyMapping[]]@(
            [PowerForge.ArtefactCopyMapping]@{
                Source = 'Build/NOTICE.txt'
                Destination = 'NOTICE.txt'
            }
        )
    }
}
[PowerForge.ConfigurationArtefactSegment]@{
    ArtefactType = [PowerForge.ArtefactType]::Unpacked
    Configuration = [PowerForge.ArtefactConfiguration]@{
        Path = 'Module/Artefacts/Unpacked'
        RequiredModules = [PowerForge.ArtefactRequiredModulesConfiguration]@{
            Path = 'Modules'
            ModulesPath = 'Payload/MainModules'
        }
    }
}
[PowerForge.ConfigurationPackageBuildSegment]@{
    Configuration = [PowerForge.PackageBuildConfiguration]@{
        RootPath = 'Sources'
        OutputPath = 'Artefacts/ProjectBuild/packages'
        PublishApiKeyFilePath = 'Build/nuget.key'
        NugetCredentialSecretFilePath = 'Build/nuget.secret'
        GitHubAccessTokenFilePath = 'Build/github.token'
    }
}
[PowerForge.ConfigurationPublishSegment]@{
    Configuration = [PowerForge.PublishConfiguration]@{
        Destination = [PowerForge.PublishDestination]::PowerShellGallery
        ApiKeyFilePath = 'Build/psgallery.key'
    }
}
[PowerForge.ConfigurationActionSegment]@{
    Configuration = [PowerForge.ModulePipelineActionConfiguration]@{
        FilePath = 'Build/Test-ReleaseReady.ps1'
        WorkingDirectory = 'Build'
    }
}
[PowerForge.ConfigurationReleaseSegment]@{
    Configuration = [PowerForge.ReleaseConfiguration]@{
        StageRoot = 'Module/Artefacts/UploadReady'
        VersionSource = [PowerForge.ReleaseVersionSource]::ProjectBuild
    }
}
""");

            using var runspace = RunspaceFactory.CreateRunspace();
            runspace.Open();
            var previousRunspace = Runspace.DefaultRunspace;
            Runspace.DefaultRunspace = runspace;
            try
            {
                var prepared = new ModuleBuildPreparationService().Prepare(new ModuleBuildPreparationRequest
                {
                    ParameterSetName = "Modern",
                    ModuleName = "DbaClientX",
                    Settings = settings,
                    CurrentPath = root.FullName,
                    ScriptRoot = scriptRoot.FullName,
                    ResolvePath = path => Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(root.FullName, path)),
                    DotNetFramework = Array.Empty<string>(),
                    ExcludeDirectories = Array.Empty<string>(),
                    ExcludeFiles = Array.Empty<string>()
                });

                Assert.Equal(moduleRoot.FullName, prepared.ProjectRoot);
                Assert.Null(prepared.BasePathForScaffold);
                Assert.Equal(moduleRoot.FullName, prepared.PipelineSpec.Build.SourcePath);

                var buildLibraries = Assert.IsType<ConfigurationBuildLibrariesSegment>(prepared.PipelineSpec.Segments[0]);
                Assert.Equal(Path.Combine(root.FullName, "DbaClientX.PowerShell", "DbaClientX.PowerShell.csproj"), buildLibraries.BuildLibraries.NETProjectPath);
                Assert.Equal(Path.Combine(root.FullName, "DbaClientX.PowerShell", "bin"), buildLibraries.BuildLibraries.NETDevelopmentBinariesPath);

                var projectBuild = Assert.IsType<ConfigurationProjectBuildSegment>(prepared.PipelineSpec.Segments[1]);
                Assert.Equal(Path.Combine(root.FullName, "Build", "project.build.json"), projectBuild.Configuration.ConfigPath);

                var artefact = Assert.IsType<ConfigurationArtefactSegment>(prepared.PipelineSpec.Segments[2]);
                Assert.Equal(Path.Combine(root.FullName, "Module", "Artefacts", "Packed"), artefact.Configuration.Path);
                Assert.Equal(Path.Combine(root.FullName, "Build", "LICENSE"), artefact.Configuration.DirectoryOutput![0].Source);
                Assert.Equal("LICENSE", artefact.Configuration.DirectoryOutput[0].Destination);
                Assert.Equal(Path.Combine(root.FullName, "Build", "NOTICE.txt"), artefact.Configuration.FilesOutput![0].Source);
                Assert.Equal("NOTICE.txt", artefact.Configuration.FilesOutput[0].Destination);

                var artefactWithRelativeLayout = Assert.IsType<ConfigurationArtefactSegment>(prepared.PipelineSpec.Segments[3]);
                Assert.Equal(Path.Combine(root.FullName, "Module", "Artefacts", "Unpacked"), artefactWithRelativeLayout.Configuration.Path);
                Assert.Equal("Modules", artefactWithRelativeLayout.Configuration.RequiredModules.Path);
                Assert.Equal("Payload/MainModules", artefactWithRelativeLayout.Configuration.RequiredModules.ModulesPath);

                var packageBuild = Assert.IsType<ConfigurationPackageBuildSegment>(prepared.PipelineSpec.Segments[4]);
                Assert.Equal(Path.Combine(root.FullName, "Sources"), packageBuild.Configuration.RootPath);
                Assert.Equal(Path.Combine(root.FullName, "Artefacts", "ProjectBuild", "packages"), packageBuild.Configuration.OutputPath);
                Assert.Equal(Path.Combine(root.FullName, "Build", "nuget.key"), packageBuild.Configuration.PublishApiKeyFilePath);
                Assert.Equal(Path.Combine(root.FullName, "Build", "nuget.secret"), packageBuild.Configuration.NugetCredentialSecretFilePath);
                Assert.Equal(Path.Combine(root.FullName, "Build", "github.token"), packageBuild.Configuration.GitHubAccessTokenFilePath);

                Assert.Equal(Path.Combine(root.FullName, "Module", "Artefacts", "Packed", "RequiredModules"), artefact.Configuration.RequiredModules.Path);
                Assert.Equal(Path.Combine(root.FullName, "Module", "Artefacts", "Packed", "Modules"), artefact.Configuration.RequiredModules.ModulesPath);

                var publish = Assert.IsType<ConfigurationPublishSegment>(prepared.PipelineSpec.Segments[5]);
                Assert.Equal(Path.Combine(root.FullName, "Build", "psgallery.key"), publish.Configuration.ApiKeyFilePath);

                var action = Assert.IsType<ConfigurationActionSegment>(prepared.PipelineSpec.Segments[6]);
                Assert.Equal(Path.Combine(root.FullName, "Build", "Test-ReleaseReady.ps1"), action.Configuration.FilePath);
                Assert.Equal(Path.Combine(root.FullName, "Build"), action.Configuration.WorkingDirectory);

                var release = Assert.IsType<ConfigurationReleaseSegment>(prepared.PipelineSpec.Segments[7]);
                Assert.Equal(Path.Combine(root.FullName, "Module", "Artefacts", "UploadReady"), release.Configuration.StageRoot);
            }
            finally
            {
                Runspace.DefaultRunspace = previousRunspace;
            }
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Prepare_from_modern_path_keeps_segment_paths_relative_to_module_root()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-modulebuild-path-root-" + Guid.NewGuid().ToString("N")));

        try
        {
            var moduleRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "SampleModule"));
            File.WriteAllText(Path.Combine(moduleRoot.FullName, "SampleModule.psd1"), "@{ ModuleVersion = '1.0.0' }");

            var settings = ScriptBlock.Create("""
[PowerForge.ConfigurationBuildLibrariesSegment]@{
    BuildLibraries = [PowerForge.BuildLibrariesConfiguration]@{
        NETProjectPath = 'Sources/Demo/Demo.csproj'
    }
}
[PowerForge.ConfigurationArtefactSegment]@{
    ArtefactType = [PowerForge.ArtefactType]::Packed
    Configuration = [PowerForge.ArtefactConfiguration]@{
        Path = 'Artefacts/Packed'
    }
}
""");

            using var runspace = RunspaceFactory.CreateRunspace();
            runspace.Open();
            var previousRunspace = Runspace.DefaultRunspace;
            Runspace.DefaultRunspace = runspace;
            try
            {
                var prepared = new ModuleBuildPreparationService().Prepare(new ModuleBuildPreparationRequest
                {
                    ParameterSetName = "Modern",
                    ModuleName = "SampleModule",
                    InputPath = root.FullName,
                    Settings = settings,
                    CurrentPath = root.FullName,
                    ResolvePath = path => Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(root.FullName, path)),
                    DotNetFramework = Array.Empty<string>(),
                    ExcludeDirectories = Array.Empty<string>(),
                    ExcludeFiles = Array.Empty<string>()
                });

                var buildLibraries = Assert.IsType<ConfigurationBuildLibrariesSegment>(prepared.PipelineSpec.Segments[0]);
                Assert.Equal("Sources/Demo/Demo.csproj", buildLibraries.BuildLibraries.NETProjectPath);

                var artefact = Assert.IsType<ConfigurationArtefactSegment>(prepared.PipelineSpec.Segments[1]);
                Assert.Equal("Artefacts/Packed", artefact.Configuration.Path);
            }
            finally
            {
                Runspace.DefaultRunspace = previousRunspace;
            }
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Prepare_from_configuration_uses_legacy_module_name_and_manifest_version()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-modulebuild-config-" + Guid.NewGuid().ToString("N")));

        try
        {
            File.WriteAllText(
                Path.Combine(root.FullName, "SampleModule.psd1"),
                "@{ ModuleVersion = '2.4.6' }");

            var configuration = new Hashtable
            {
                ["Information"] = new Hashtable
                {
                    ["ModuleName"] = "SampleModule"
                }
            };

            var prepared = new ModuleBuildPreparationService().Prepare(new ModuleBuildPreparationRequest
            {
                ParameterSetName = "Configuration",
                Configuration = configuration,
                CurrentPath = root.FullName,
                ResolvePath = path => path,
                SkipInstall = true,
                DiagnosticsBaselinePath = Path.Combine(root.FullName, ".powerforge", "baseline.json"),
                FailOnNewDiagnostics = true
            });

            Assert.Equal("SampleModule", prepared.ModuleName);
            Assert.Equal(root.FullName, prepared.ProjectRoot);
            Assert.Null(prepared.BasePathForScaffold);
            Assert.True(prepared.UseLegacy);
            Assert.Equal("2.4.6", prepared.PipelineSpec.Build.Version);
            Assert.False(prepared.PipelineSpec.Install.Enabled);
            Assert.Equal(Path.Combine(root.FullName, ".powerforge", "baseline.json"), prepared.PipelineSpec.Diagnostics.BaselinePath);
            Assert.True(prepared.PipelineSpec.Diagnostics.FailOnNewDiagnostics);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Prepare_prefers_configured_manifest_version_over_source_manifest_version()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-modulebuild-configured-version-" + Guid.NewGuid().ToString("N")));

        try
        {
            File.WriteAllText(
                Path.Combine(root.FullName, "SampleModule.psd1"),
                "@{ ModuleVersion = '3.0.0' }");

            var configuration = new Hashtable
            {
                ["Information"] = new Hashtable
                {
                    ["ModuleName"] = "SampleModule",
                    ["Manifest"] = new Hashtable
                    {
                        ["ModuleVersion"] = "3.0.X",
                        ["CompatiblePSEditions"] = new[] { "Desktop", "Core" },
                        ["Author"] = "Przemyslaw Klys"
                    }
                }
            };

            var prepared = new ModuleBuildPreparationService().Prepare(new ModuleBuildPreparationRequest
            {
                ParameterSetName = "Configuration",
                Configuration = configuration,
                CurrentPath = root.FullName,
                ResolvePath = path => path
            });

            Assert.Equal("3.0.X", prepared.PipelineSpec.Build.Version);
            var manifestSegment = Assert.IsType<ConfigurationManifestSegment>(Assert.Single(prepared.PipelineSpec.Segments));
            Assert.Equal("3.0.X", manifestSegment.Configuration.ModuleVersion);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void WritePipelineSpecJson_rewrites_paths_relative_to_output()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-modulebuild-json-" + Guid.NewGuid().ToString("N")));

        try
        {
            var jsonPath = Path.Combine(root.FullName, ".powerforge", "powerforge.json");
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = "SampleModule",
                    SourcePath = root.FullName,
                    StagingPath = Path.Combine(root.FullName, "staging"),
                    CsprojPath = Path.Combine(root.FullName, "src", "SampleModule.csproj")
                },
                Diagnostics = new ModulePipelineDiagnosticsOptions
                {
                    BaselinePath = Path.Combine(root.FullName, ".powerforge", "baseline.json")
                }
            };

            new ModuleBuildPreparationService().WritePipelineSpecJson(spec, jsonPath);

            var json = File.ReadAllText(jsonPath);
            Assert.Contains("\"SourcePath\": \"..\"", json, StringComparison.Ordinal);
            Assert.Contains("\"StagingPath\": \"../staging\"", json, StringComparison.Ordinal);
            Assert.Contains("\"CsprojPath\": \"../src/SampleModule.csproj\"", json, StringComparison.Ordinal);
            Assert.Contains("\"BaselinePath\": \"baseline.json\"", json, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void WritePipelineSpecJson_keeps_apple_and_xcode_paths_relative_to_project_root()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-modulebuild-json-apple-" + Guid.NewGuid().ToString("N")));

        try
        {
            var jsonPath = Path.Combine(root.FullName, ".powerforge", "powerforge.json");
            var appProject = Path.Combine(root.FullName, "Tactra.xcodeproj");
            var macProject = Path.Combine(root.FullName, "Mac", "TactraMac.xcodeproj");
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = "SampleModule",
                    SourcePath = root.FullName
                },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationAppleAppSegment
                    {
                        Configuration = new AppleAppConfiguration
                        {
                            ProjectPath = "Tactra.xcodeproj",
                            UseResolvedVersion = true
                        }
                    },
                    new ConfigurationXcodeProjectVersionSegment
                    {
                        Configuration = new XcodeProjectVersionConfiguration
                        {
                            Path = Path.Combine("Mac", "TactraMac.xcodeproj"),
                            UseResolvedVersion = true
                        }
                    }
                }
            };

            var service = new ModuleBuildPreparationService();
            service.WritePipelineSpecJson(spec, jsonPath);

            var json = File.ReadAllText(jsonPath);
            Assert.Contains("\"SourcePath\": \"..\"", json, StringComparison.Ordinal);
            Assert.Contains("\"ProjectPath\": \"Tactra.xcodeproj\"", json, StringComparison.Ordinal);
            Assert.Contains("\"Path\": \"Mac/TactraMac.xcodeproj\"", json, StringComparison.Ordinal);
            Assert.DoesNotContain("\"ProjectPath\": \"../Tactra.xcodeproj\"", json, StringComparison.Ordinal);

            var jsonSpec = JsonSerializer.Deserialize<ModulePipelineSpec>(json, CreateJsonOptions());
            Assert.NotNull(jsonSpec);

            service.ResolvePipelineSpecPaths(jsonSpec!, jsonPath);
            var apple = Assert.IsType<ConfigurationAppleAppSegment>(jsonSpec!.Segments[0]);
            var xcode = Assert.IsType<ConfigurationXcodeProjectVersionSegment>(jsonSpec.Segments[1]);
            Assert.Equal(appProject, apple.Configuration.ProjectPath);
            Assert.Equal(macProject, xcode.Configuration.Path);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void WritePipelineSpecJson_keeps_package_build_paths_relative_to_project_root()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-modulebuild-json-packages-" + Guid.NewGuid().ToString("N")));

        try
        {
            var jsonPath = Path.Combine(root.FullName, ".powerforge", "powerforge.json");
            var projectConfig = Path.Combine(root.FullName, "Build", "project.build.json");
            var netProject = Path.Combine(root.FullName, "Sources", "SampleModule.PowerShell", "SampleModule.PowerShell.csproj");
            var developmentBinaries = Path.Combine(root.FullName, "Sources", "SampleModule.PowerShell", "bin");
            var packageRoot = Path.Combine(root.FullName, "Sources");
            var stagingRoot = Path.Combine(root.FullName, "Artifacts", "Packages", "Staging");
            var releaseRoot = Path.Combine(root.FullName, "Artifacts", "Release");
            var artefactRoot = Path.Combine(root.FullName, "Module", "Artefacts", "Packed");
            var artefactRequiredModules = Path.Combine(root.FullName, "Module", "Artefacts", "Packed", "RequiredModules");
            var artefactModules = Path.Combine(root.FullName, "Module", "Artefacts", "Packed", "Modules");
            var publishKey = Path.Combine(root.FullName, "Build", "psgallery.key");
            var actionFile = Path.Combine(root.FullName, "Build", "Test-ReleaseReady.ps1");
            var actionWorkingDirectory = Path.Combine(root.FullName, "Build");
            var copyDirectorySource = Path.Combine(root.FullName, "Build", "Templates");
            var copyFileSource = Path.Combine(root.FullName, "Build", "NOTICE.txt");
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = "SampleModule",
                    SourcePath = root.FullName
                },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationProjectBuildSegment
                    {
                        Configuration = new ProjectBuildConfigurationReference
                        {
                            ConfigPath = projectConfig,
                            BuildBeforeModule = true
                        }
                    },
                    new ConfigurationBuildLibrariesSegment
                    {
                        BuildLibraries = new BuildLibrariesConfiguration
                        {
                            NETProjectPath = netProject,
                            NETDevelopmentBinariesPath = developmentBinaries
                        }
                    },
                    new ConfigurationPackageBuildSegment
                    {
                        Configuration = new PackageBuildConfiguration
                        {
                            RootPath = packageRoot,
                            StagingPath = stagingRoot,
                            OutputPath = Path.Combine(root.FullName, "Artifacts", "Packages", "NuGet"),
                            PlanOutputPath = Path.Combine(root.FullName, "Artifacts", "Packages", "plan.json"),
                            PublishApiKeyFilePath = Path.Combine(root.FullName, "Build", "nuget.key"),
                            NugetCredentialSecretFilePath = Path.Combine(root.FullName, "Build", "nuget.secret"),
                            GitHubAccessTokenFilePath = Path.Combine(root.FullName, "Build", "github.token")
                        }
                    },
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Packed,
                        Configuration = new ArtefactConfiguration
                        {
                            Path = artefactRoot,
                            RequiredModules = new ArtefactRequiredModulesConfiguration
                            {
                                Path = artefactRequiredModules,
                                ModulesPath = artefactModules
                            },
                            DirectoryOutput = new[]
                            {
                                new ArtefactCopyMapping
                                {
                                    Source = copyDirectorySource,
                                    Destination = "Templates"
                                }
                            },
                            FilesOutput = new[]
                            {
                                new ArtefactCopyMapping
                                {
                                    Source = copyFileSource,
                                    Destination = "NOTICE.txt"
                                }
                            }
                        }
                    },
                    new ConfigurationPublishSegment
                    {
                        Configuration = new PublishConfiguration
                        {
                            Destination = PublishDestination.PowerShellGallery,
                            ApiKeyFilePath = publishKey
                        }
                    },
                    new ConfigurationActionSegment
                    {
                        Configuration = new ModulePipelineActionConfiguration
                        {
                            FilePath = actionFile,
                            WorkingDirectory = actionWorkingDirectory
                        }
                    },
                    new ConfigurationReleaseSegment
                    {
                        Configuration = new ReleaseConfiguration
                        {
                            StageRoot = releaseRoot,
                            VersionSource = ReleaseVersionSource.PackageBuild
                        }
                    }
                }
            };

            var service = new ModuleBuildPreparationService();
            service.WritePipelineSpecJson(spec, jsonPath);

            var json = File.ReadAllText(jsonPath);
            Assert.Contains("\"ConfigPath\": \"Build/project.build.json\"", json, StringComparison.Ordinal);
            Assert.Contains("\"NETProjectPath\": \"Sources/SampleModule.PowerShell/SampleModule.PowerShell.csproj\"", json, StringComparison.Ordinal);
            Assert.Contains("\"NETDevelopmentBinariesPath\": \"Sources/SampleModule.PowerShell/bin\"", json, StringComparison.Ordinal);
            Assert.Contains("\"RootPath\": \"Sources\"", json, StringComparison.Ordinal);
            Assert.Contains("\"StagingPath\": \"Artifacts/Packages/Staging\"", json, StringComparison.Ordinal);
            Assert.Contains("\"PlanOutputPath\": \"Artifacts/Packages/plan.json\"", json, StringComparison.Ordinal);
            Assert.Contains("\"PublishApiKeyFilePath\": \"Build/nuget.key\"", json, StringComparison.Ordinal);
            Assert.Contains("\"NugetCredentialSecretFilePath\": \"Build/nuget.secret\"", json, StringComparison.Ordinal);
            Assert.Contains("\"GitHubAccessTokenFilePath\": \"Build/github.token\"", json, StringComparison.Ordinal);
            Assert.Contains("\"Path\": \"Module/Artefacts/Packed\"", json, StringComparison.Ordinal);
            Assert.Contains("\"Path\": \"Module/Artefacts/Packed/RequiredModules\"", json, StringComparison.Ordinal);
            Assert.Contains("\"ModulesPath\": \"Module/Artefacts/Packed/Modules\"", json, StringComparison.Ordinal);
            Assert.Contains("\"Source\": \"Build/Templates\"", json, StringComparison.Ordinal);
            Assert.Contains("\"Source\": \"Build/NOTICE.txt\"", json, StringComparison.Ordinal);
            Assert.Contains("\"ApiKeyFilePath\": \"Build/psgallery.key\"", json, StringComparison.Ordinal);
            Assert.Contains("\"FilePath\": \"Build/Test-ReleaseReady.ps1\"", json, StringComparison.Ordinal);
            Assert.Contains("\"WorkingDirectory\": \"Build\"", json, StringComparison.Ordinal);
            Assert.Contains("\"StageRoot\": \"Artifacts/Release\"", json, StringComparison.Ordinal);

            var jsonSpec = JsonSerializer.Deserialize<ModulePipelineSpec>(json, CreateJsonOptions());
            Assert.NotNull(jsonSpec);

            service.ResolvePipelineSpecPaths(jsonSpec!, jsonPath);
            var projectBuild = Assert.IsType<ConfigurationProjectBuildSegment>(jsonSpec!.Segments[0]);
            var buildLibraries = Assert.IsType<ConfigurationBuildLibrariesSegment>(jsonSpec.Segments[1]);
            var packageBuild = Assert.IsType<ConfigurationPackageBuildSegment>(jsonSpec.Segments[2]);
            var artefact = Assert.IsType<ConfigurationArtefactSegment>(jsonSpec.Segments[3]);
            var publish = Assert.IsType<ConfigurationPublishSegment>(jsonSpec.Segments[4]);
            var action = Assert.IsType<ConfigurationActionSegment>(jsonSpec.Segments[5]);
            var release = Assert.IsType<ConfigurationReleaseSegment>(jsonSpec.Segments[6]);
            Assert.Equal(projectConfig, projectBuild.Configuration.ConfigPath);
            Assert.Equal(netProject, buildLibraries.BuildLibraries.NETProjectPath);
            Assert.Equal(developmentBinaries, buildLibraries.BuildLibraries.NETDevelopmentBinariesPath);
            Assert.Equal(packageRoot, packageBuild.Configuration.RootPath);
            Assert.Equal(stagingRoot, packageBuild.Configuration.StagingPath);
            Assert.Equal(Path.Combine(root.FullName, "Build", "nuget.key"), packageBuild.Configuration.PublishApiKeyFilePath);
            Assert.Equal(Path.Combine(root.FullName, "Build", "nuget.secret"), packageBuild.Configuration.NugetCredentialSecretFilePath);
            Assert.Equal(Path.Combine(root.FullName, "Build", "github.token"), packageBuild.Configuration.GitHubAccessTokenFilePath);
            Assert.Equal(artefactRoot, artefact.Configuration.Path);
            Assert.Equal(artefactRequiredModules, artefact.Configuration.RequiredModules.Path);
            Assert.Equal(artefactModules, artefact.Configuration.RequiredModules.ModulesPath);
            Assert.Equal(copyDirectorySource, artefact.Configuration.DirectoryOutput![0].Source);
            Assert.Equal("Templates", artefact.Configuration.DirectoryOutput[0].Destination);
            Assert.Equal(copyFileSource, artefact.Configuration.FilesOutput![0].Source);
            Assert.Equal("NOTICE.txt", artefact.Configuration.FilesOutput[0].Destination);
            Assert.Equal(publishKey, publish.Configuration.ApiKeyFilePath);
            Assert.Equal(actionFile, action.Configuration.FilePath);
            Assert.Equal(actionWorkingDirectory, action.Configuration.WorkingDirectory);
            Assert.Equal(releaseRoot, release.Configuration.StageRoot);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void ResolvePipelineSpecPaths_preserves_artefact_relative_required_module_paths()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-modulebuild-artefact-layout-" + Guid.NewGuid().ToString("N")));

        try
        {
            var jsonPath = Path.Combine(root.FullName, ".powerforge", "powerforge.json");
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = "SampleModule",
                    SourcePath = root.FullName
                },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Unpacked,
                        Configuration = new ArtefactConfiguration
                        {
                            Path = "Artefacts/Unpacked",
                            RequiredModules = new ArtefactRequiredModulesConfiguration
                            {
                                Path = "Modules",
                                ModulesPath = "Payload/MainModules"
                            }
                        }
                    },
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Packed,
                        Configuration = new ArtefactConfiguration
                        {
                            Path = "Artefacts/Packed",
                            RequiredModules = new ArtefactRequiredModulesConfiguration
                            {
                                Path = "Artefacts/Packed/RequiredModules",
                                ModulesPath = "Artefacts/Packed/Modules"
                            }
                        }
                    }
                }
            };

            new ModuleBuildPreparationService().ResolvePipelineSpecPaths(spec, jsonPath);

            var relativeLayout = Assert.IsType<ConfigurationArtefactSegment>(spec.Segments[0]);
            Assert.Equal(Path.Combine(root.FullName, "Artefacts", "Unpacked"), relativeLayout.Configuration.Path);
            Assert.Equal("Modules", relativeLayout.Configuration.RequiredModules.Path);
            Assert.Equal("Payload/MainModules", relativeLayout.Configuration.RequiredModules.ModulesPath);

            var workspaceQualifiedLayout = Assert.IsType<ConfigurationArtefactSegment>(spec.Segments[1]);
            Assert.Equal(Path.Combine(root.FullName, "Artefacts", "Packed"), workspaceQualifiedLayout.Configuration.Path);
            Assert.Equal(Path.Combine(root.FullName, "Artefacts", "Packed", "RequiredModules"), workspaceQualifiedLayout.Configuration.RequiredModules.Path);
            Assert.Equal(Path.Combine(root.FullName, "Artefacts", "Packed", "Modules"), workspaceQualifiedLayout.Configuration.RequiredModules.ModulesPath);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void ResolvePipelineSpecPaths_accepts_powershell_authored_absolute_paths()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-modulebuild-pspaths-" + Guid.NewGuid().ToString("N")));

        try
        {
            var buildRoot = Path.Combine(root.FullName, "Build");
            var jsonPath = Path.Combine(root.FullName, ".powerforge", "powerforge.json");
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = "SampleModule",
                    SourcePath = root.FullName
                },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationProjectBuildSegment
                    {
                        Configuration = new ProjectBuildConfigurationReference
                        {
                            ConfigPath = buildRoot + "\\project.build.json",
                            BuildBeforeModule = true
                        }
                    },
                    new ConfigurationReleaseSegment
                    {
                        Configuration = new ReleaseConfiguration
                        {
                            StageRoot = buildRoot + "\\..\\Artefacts\\UploadReady",
                            VersionSource = ReleaseVersionSource.ProjectBuild
                        }
                    }
                }
            };

            new ModuleBuildPreparationService().ResolvePipelineSpecPaths(spec, jsonPath);

            var projectBuild = Assert.IsType<ConfigurationProjectBuildSegment>(spec.Segments[0]);
            var release = Assert.IsType<ConfigurationReleaseSegment>(spec.Segments[1]);
            Assert.Equal(Path.Combine(buildRoot, "project.build.json"), projectBuild.Configuration.ConfigPath);
            Assert.Equal(Path.Combine(root.FullName, "Artefacts", "UploadReady"), release.Configuration.StageRoot);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Plan_keeps_package_build_segments()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-modulebuild-plan-packages-" + Guid.NewGuid().ToString("N")));

        try
        {
            const string moduleName = "SampleModule";
            File.WriteAllText(Path.Combine(root.FullName, $"{moduleName}.psd1"), "@{ ModuleVersion = '1.2.3' }");

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.2.X"
                },
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
                            RootPath = "Sources",
                            Enabled = true
                        }
                    },
                    new ConfigurationReleaseSegment
                    {
                        Configuration = new ReleaseConfiguration
                        {
                            VersionSource = ReleaseVersionSource.PackageBuild,
                            PrimaryProject = "HtmlTinkerX"
                        }
                    }
                }
            };

            var plan = new ModulePipelineRunner(new NullLogger()).Plan(spec);

            Assert.Single(plan.ProjectBuilds);
            Assert.Single(plan.PackageBuilds);
            Assert.NotNull(plan.Release);
            Assert.True(plan.ProjectBuilds[0].Configuration.BuildBeforeModule);
            Assert.Equal("Sources", plan.PackageBuilds[0].Configuration.RootPath);
            Assert.Equal(ReleaseVersionSource.PackageBuild, plan.Release!.Configuration.VersionSource);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }


    [Fact]
    public void WritePipelineSpecJson_preserves_configured_manifest_version_in_build_spec()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-modulebuild-json-version-" + Guid.NewGuid().ToString("N")));

        try
        {
            var jsonPath = Path.Combine(root.FullName, ".powerforge", "powerforge.json");
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = "SampleModule",
                    SourcePath = root.FullName,
                    Version = "3.0.X"
                },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationManifestSegment
                    {
                        Configuration = new ManifestConfiguration
                        {
                            ModuleVersion = "3.0.X",
                            Author = "Przemyslaw Klys"
                        }
                    }
                }
            };

            new ModuleBuildPreparationService().WritePipelineSpecJson(spec, jsonPath);

            var json = File.ReadAllText(jsonPath);
            Assert.Contains("\"Version\": \"3.0.X\"", json, StringComparison.Ordinal);
            Assert.Contains("\"ModuleVersion\": \"3.0.X\"", json, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void WritePipelineSpecJson_round_trips_pipeline_plan_without_losing_publish_or_version_data()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-modulebuild-json-parity-" + Guid.NewGuid().ToString("N")));

        try
        {
            const string moduleName = "SampleModule";
            File.WriteAllText(Path.Combine(root.FullName, $"{moduleName}.psd1"), "@{ ModuleVersion = '3.0.0' }");
            File.WriteAllText(Path.Combine(root.FullName, $"{moduleName}.psm1"), string.Empty);

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "3.0.X",
                    Configuration = "Release",
                    Frameworks = new[] { "net8.0", "net472" }
                },
                Install = new ModulePipelineInstallOptions
                {
                    Enabled = true,
                    Strategy = InstallationStrategy.AutoRevision,
                    KeepVersions = 3
                },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationManifestSegment
                    {
                        Configuration = new ManifestConfiguration
                        {
                            ModuleVersion = "3.0.X",
                            CompatiblePSEditions = new[] { "Desktop", "Core" },
                            Guid = "eb76426a-1992-40a5-82cd-6480f883ef4d",
                            Author = "Przemyslaw Klys"
                        }
                    },
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Unpacked,
                        Configuration = new ArtefactConfiguration
                        {
                            Enabled = true,
                            Path = "Artefacts/Unpacked/<TagModuleVersionWithPreRelease>"
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
                            ID = "ToGitHub",
                            UserName = "EvotecIT",
                            OverwriteTagName = "<TagModuleVersionWithPreRelease>"
                        }
                    }
                }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var directPlan = runner.Plan(spec);

            var jsonPath = Path.Combine(root.FullName, ".powerforge", "powerforge.json");
            new ModuleBuildPreparationService().WritePipelineSpecJson(spec, jsonPath);

            var json = File.ReadAllText(jsonPath);
            var jsonSpec = JsonSerializer.Deserialize<ModulePipelineSpec>(json, CreateJsonOptions());
            Assert.NotNull(jsonSpec);

            ResolvePipelineSpecPathsLikeCli(jsonSpec!, jsonPath);
            var roundTrippedPlan = runner.Plan(jsonSpec!);

            Assert.Equal(directPlan.ExpectedVersion, roundTrippedPlan.ExpectedVersion);
            Assert.Equal(directPlan.ResolvedVersion, roundTrippedPlan.ResolvedVersion);
            Assert.Equal(directPlan.BuildSpec.Version, roundTrippedPlan.BuildSpec.Version);
            Assert.Equal(directPlan.Publishes.Length, roundTrippedPlan.Publishes.Length);
            Assert.Equal(directPlan.Artefacts.Length, roundTrippedPlan.Artefacts.Length);
            Assert.Equal(directPlan.InstallEnabled, roundTrippedPlan.InstallEnabled);
            Assert.Equal(directPlan.InstallStrategy, roundTrippedPlan.InstallStrategy);
            Assert.Equal(directPlan.InstallKeepVersions, roundTrippedPlan.InstallKeepVersions);
            Assert.Equal(
                directPlan.Publishes.Select(p => p.Configuration.Destination).ToArray(),
                roundTrippedPlan.Publishes.Select(p => p.Configuration.Destination).ToArray());
            Assert.Equal(
                directPlan.Publishes.Select(p => p.Configuration.ID ?? string.Empty).ToArray(),
                roundTrippedPlan.Publishes.Select(p => p.Configuration.ID ?? string.Empty).ToArray());
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Prepare_from_config_loads_pipeline_json_and_resolves_paths()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-modulebuild-config-json-" + Guid.NewGuid().ToString("N")));

        try
        {
            var moduleRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "Module"));
            File.WriteAllText(Path.Combine(moduleRoot.FullName, "PowerTierBridge.psd1"), "@{ ModuleVersion = '4.0.2' }");

            var configDir = Directory.CreateDirectory(Path.Combine(root.FullName, "Build"));
            var configPath = Path.Combine(configDir.FullName, "module.build.json");
            File.WriteAllText(configPath, """
{
  "Build": {
    "Name": "PowerTierBridge",
    "SourcePath": "../Module",
    "StagingPath": "../Build/Artifacts/Module/Staging",
    "CsprojPath": "../TierBridge.PowerShell/TierBridge.PowerShell.csproj",
    "DevelopmentBinariesPath": "../Sources/Demo/bin",
    "Version": "4.0.X"
  },
  "Diagnostics": {
    "BaselinePath": ".powerforge/module-baseline.json"
  }
}
""");

            var prepared = new ModuleBuildPreparationService().Prepare(new ModuleBuildPreparationRequest
            {
                ParameterSetName = "Config",
                ConfigPath = configPath,
                CurrentPath = root.FullName,
                ResolvePath = path => Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(root.FullName, path))
            });

            Assert.Equal("PowerTierBridge", prepared.ModuleName);
            Assert.Equal(moduleRoot.FullName, prepared.ProjectRoot);
            Assert.False(prepared.UseLegacy);
            Assert.Null(prepared.BasePathForScaffold);
            Assert.Equal("json", prepared.ConfigLabel);
            Assert.Equal(configPath, prepared.ConfigFilePath);
            Assert.Equal(Path.Combine(root.FullName, "Build", "Artifacts", "Module", "Staging"), prepared.PipelineSpec.Build.StagingPath);
            Assert.Equal(Path.Combine(root.FullName, "TierBridge.PowerShell", "TierBridge.PowerShell.csproj"), prepared.PipelineSpec.Build.CsprojPath);
            Assert.Equal(Path.Combine(root.FullName, "Sources", "Demo", "bin"), prepared.PipelineSpec.Build.DevelopmentBinariesPath);
            Assert.Equal(Path.Combine(configDir.FullName, ".powerforge", "module-baseline.json"), prepared.PipelineSpec.Diagnostics.BaselinePath);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new ConfigurationSegmentJsonConverter());
        return options;
    }

    private static void ResolvePipelineSpecPathsLikeCli(ModulePipelineSpec spec, string configFullPath)
    {
        var baseDir = Path.GetDirectoryName(configFullPath) ?? Directory.GetCurrentDirectory();

        if (!string.IsNullOrWhiteSpace(spec.Build.SourcePath))
            spec.Build.SourcePath = Path.GetFullPath(Path.IsPathRooted(spec.Build.SourcePath) ? spec.Build.SourcePath : Path.Combine(baseDir, spec.Build.SourcePath));

        if (!string.IsNullOrWhiteSpace(spec.Build.StagingPath))
            spec.Build.StagingPath = Path.GetFullPath(Path.IsPathRooted(spec.Build.StagingPath) ? spec.Build.StagingPath! : Path.Combine(baseDir, spec.Build.StagingPath!));

        if (!string.IsNullOrWhiteSpace(spec.Build.CsprojPath))
            spec.Build.CsprojPath = Path.GetFullPath(Path.IsPathRooted(spec.Build.CsprojPath) ? spec.Build.CsprojPath! : Path.Combine(baseDir, spec.Build.CsprojPath!));

        if (!string.IsNullOrWhiteSpace(spec.Build.DevelopmentBinariesPath))
            spec.Build.DevelopmentBinariesPath = Path.GetFullPath(Path.IsPathRooted(spec.Build.DevelopmentBinariesPath) ? spec.Build.DevelopmentBinariesPath! : Path.Combine(baseDir, spec.Build.DevelopmentBinariesPath!));
    }
}
