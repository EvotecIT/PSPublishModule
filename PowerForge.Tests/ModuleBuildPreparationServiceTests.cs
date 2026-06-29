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
                StagingPath = "Artefacts/Staging",
                CsprojPath = "Sources/SampleModule.PowerShell/SampleModule.PowerShell.csproj",
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
            Assert.Equal(Path.Combine(root.FullName, "Artefacts", "Staging"), prepared.PipelineSpec.Build.StagingPath);
            Assert.Equal(Path.Combine(root.FullName, "Sources", "SampleModule.PowerShell", "SampleModule.PowerShell.csproj"), prepared.PipelineSpec.Build.CsprojPath);
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
            MarkRepositoryRoot(root.FullName);
            File.WriteAllText(Path.Combine(scriptRoot.FullName, "Build-Module.ps1"), string.Empty);
            Directory.CreateDirectory(Path.Combine(root.FullName, "Build"));
            Directory.CreateDirectory(Path.Combine(root.FullName, "Build", "Templates"));
            Directory.CreateDirectory(Path.Combine(moduleRoot.FullName, "Examples"));
            var topLevelProject = Path.Combine(root.FullName, "Sources", "TopLevel", "TopLevel.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(topLevelProject)!);
            File.WriteAllText(topLevelProject, "<Project />");
            File.WriteAllText(Path.Combine(root.FullName, "Build", "header.ps1"), "Write-Output 'repo header'");
            File.WriteAllText(Path.Combine(scriptRoot.FullName, "header.ps1"), "Write-Output 'module header'");
            File.WriteAllText(Path.Combine(scriptRoot.FullName, "workspace-header.ps1"), "Write-Output 'workspace-qualified module header'");
            File.WriteAllText(Path.Combine(root.FullName, "Build", "NOTICE.txt"), "notice");
            File.WriteAllText(Path.Combine(moduleRoot.FullName, "Examples", "NOTICE.txt"), "module notice");
            File.WriteAllText(Path.Combine(moduleRoot.FullName, "DbaClientX.psd1"), "@{ ModuleVersion = '1.0.0' }");

            var settings = ScriptBlock.Create("""
$projectOptions = [System.Collections.Generic.Dictionary[string,object]]::new()
$projectOptions['OutputPath'] = 'Artefacts/ProjectBuild/options'
$projectOptions['PlanOutputPath'] = 'Build/project-options-plan.json'
$packageOptions = [System.Collections.Generic.Dictionary[string,object]]::new()
$packageOptions['outputPath'] = 'Module/Artefacts/PackageBuild/options'
$packageOptions['PlanOutputPath'] = 'Build/package-options-plan.json'
[PowerForge.ConfigurationBuildLibrariesSegment]@{
    BuildLibraries = [PowerForge.BuildLibrariesConfiguration]@{
        NETProjectPath = 'Module/Sources/Foo/Foo.csproj'
        DevelopmentBinariesPath = 'Sources/Local/bin'
        NETDevelopmentBinariesPath = '../DbaClientX.PowerShell/bin'
    }
}
[PowerForge.ConfigurationProjectBuildSegment]@{
    Configuration = [PowerForge.ProjectBuildConfigurationReference]@{
        ConfigPath = 'Module/Build/project.build.json'
        BuildBeforeModule = $true
        Options = $projectOptions
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
                Source = 'Module/Build/LICENSE'
                Destination = 'LICENSE'
            },
            [PowerForge.ArtefactCopyMapping]@{
                Source = 'Build/Templates'
                Destination = 'Templates'
            },
            [PowerForge.ArtefactCopyMapping]@{
                Source = 'Docs/Help'
                Destination = 'Help'
            }
        )
        FilesOutput = [PowerForge.ArtefactCopyMapping[]]@(
            [PowerForge.ArtefactCopyMapping]@{
                Source = 'Examples/NOTICE.txt'
                Destination = 'NOTICE.txt'
            },
            [PowerForge.ArtefactCopyMapping]@{
                Source = 'Build/NOTICE.txt'
                Destination = 'RepoNotice.txt'
            },
            [PowerForge.ArtefactCopyMapping]@{
                Source = 'Artefacts/ProjectBuild/packages/Foo.nupkg'
                Destination = 'Packages/Foo.nupkg'
            },
            [PowerForge.ArtefactCopyMapping]@{
                Source = 'Assets/logo.png'
                Destination = 'Assets/logo.png'
            }
        )
    }
}
[PowerForge.ConfigurationArtefactSegment]@{
    ArtefactType = [PowerForge.ArtefactType]::Unpacked
    Configuration = [PowerForge.ArtefactConfiguration]@{
        Path = 'Artefacts/Unpacked'
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
        Options = $packageOptions
    }
}
[PowerForge.ConfigurationTestSegment]@{
    Configuration = [PowerForge.TestConfiguration]@{
        TestsPath = 'Tests'
    }
}
[PowerForge.ConfigurationOptionsSegment]@{
    Options = [PowerForge.ConfigurationOptions]@{
        Signing = [PowerForge.SigningOptionsConfiguration]@{
            CertificatePFXPath = 'Build/cert.pfx'
        }
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
        StageRoot = 'Artefacts/UploadReady/<ModuleVersion>'
        VersionSource = [PowerForge.ReleaseVersionSource]::ProjectBuild
    }
}
[PowerForge.ConfigurationValidationSegment]@{
    Settings = [PowerForge.ModuleValidationSettings]@{
        Enable = $true
        Tests = [PowerForge.TestSuiteValidationSettings]@{
            Enable = $true
            TestPath = 'Tests'
        }
    }
}
[PowerForge.ConfigurationDocumentationSegment]@{
    Configuration = [PowerForge.DocumentationConfiguration]@{
        Path = '.\Module\Docs'
        PathReadme = 'Docs/Readme.md'
    }
}
[PowerForge.ConfigurationBuildDocumentationSegment]@{
    Configuration = [PowerForge.BuildDocumentationConfiguration]@{
        Enable = $true
        AboutTopicsSourcePath = @('Help/About', 'Module/Help/Workspace')
    }
}
[PowerForge.ArtefactConfigurationFactory]::new([PowerForge.NullLogger]::new()).Create([PowerForge.ArtefactConfigurationRequest]@{
    Type = [PowerForge.ArtefactType]::Packed
    EnableSpecified = $true
    Enable = $true
    Path = 'Module/Artefacts/FileBacked/<TagModuleVersionWithPreRelease>'
    PreScriptMergePath = 'Build/header.ps1'
})
[PowerForge.ArtefactConfigurationFactory]::new([PowerForge.NullLogger]::new()).Create([PowerForge.ArtefactConfigurationRequest]@{
    Type = [PowerForge.ArtefactType]::Packed
    EnableSpecified = $true
    Enable = $true
    Path = 'Module/Artefacts/WorkspaceFileBacked/<TagModuleVersionWithPreRelease>'
    PreScriptMergePath = '.\Module\Build\workspace-header.ps1'
})
[PowerForge.ConfigurationAppleAppSegment]@{
    Configuration = [PowerForge.AppleAppConfiguration]@{
        ProjectPath = '.\Tactra.xcodeproj'
        UseResolvedVersion = $true
    }
}
[PowerForge.ConfigurationXcodeProjectVersionSegment]@{
    Configuration = [PowerForge.XcodeProjectVersionConfiguration]@{
        Path = 'Mac\TactraMac.xcodeproj'
        UseResolvedVersion = $true
    }
}
""");

            var outsideRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-modulebuild-outside-" + Guid.NewGuid().ToString("N")));
            var previousCurrentDirectory = Directory.GetCurrentDirectory();
            using var runspace = RunspaceFactory.CreateRunspace();
            runspace.Open();
            var previousRunspace = Runspace.DefaultRunspace;
            Runspace.DefaultRunspace = runspace;
            try
            {
                Directory.SetCurrentDirectory(outsideRoot.FullName);
                runspace.SessionStateProxy.Path.SetLocation(outsideRoot.FullName);

                var prepared = new ModuleBuildPreparationService().Prepare(new ModuleBuildPreparationRequest
                {
                    ParameterSetName = "Modern",
                    ModuleName = "DbaClientX",
                    Settings = settings,
                    CurrentPath = root.FullName,
                    ScriptRoot = scriptRoot.FullName,
                    ResolvePath = path => Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(root.FullName, path)),
                    StagingPath = "Artefacts/Staging",
                    CsprojPath = "Sources/TopLevel/TopLevel.csproj",
                    DiagnosticsBaselinePath = "Build/diagnostics.json",
                    DiagnosticsBinaryConflictSearchRoot = new[] { "Modules" },
                    InstallRootsWasBound = true,
                    InstallRoots = new[] { "Artefacts/Modules" },
                    JsonOnly = true,
                    JsonPath = "Build/powerforge.json",
                    DotNetFramework = Array.Empty<string>(),
                    ExcludeDirectories = Array.Empty<string>(),
                    ExcludeFiles = Array.Empty<string>()
                });

                Assert.Equal(moduleRoot.FullName, prepared.ProjectRoot);
                Assert.Null(prepared.BasePathForScaffold);
                Assert.Equal(moduleRoot.FullName, prepared.PipelineSpec.Build.SourcePath);
                Assert.Equal(Path.Combine(root.FullName, "Artefacts", "Staging"), prepared.PipelineSpec.Build.StagingPath);
                Assert.Equal(Path.Combine(root.FullName, "Sources", "TopLevel", "TopLevel.csproj"), prepared.PipelineSpec.Build.CsprojPath);
                Assert.Equal("Build/diagnostics.json", prepared.PipelineSpec.Diagnostics.BaselinePath);
                Assert.Equal(new[] { Path.Combine(root.FullName, "Modules") }, prepared.PipelineSpec.Build.BinaryConflictSearchRoots);
                Assert.Equal(new[] { Path.Combine(root.FullName, "Modules") }, prepared.PipelineSpec.Diagnostics.BinaryConflictSearchRoots);
                Assert.Equal(new[] { Path.Combine(root.FullName, "Artefacts", "Modules") }, prepared.PipelineSpec.Install.Roots);
                Assert.Equal(Path.Combine(root.FullName, "Build", "powerforge.json"), prepared.JsonOutputPath);

                var buildLibraries = Assert.IsType<ConfigurationBuildLibrariesSegment>(prepared.PipelineSpec.Segments[0]);
                Assert.Equal(Path.Combine(root.FullName, "Module", "Sources", "Foo", "Foo.csproj"), buildLibraries.BuildLibraries.NETProjectPath);
                Assert.Equal("Sources/Local/bin", buildLibraries.BuildLibraries.DevelopmentBinariesPath);
                Assert.Equal("../DbaClientX.PowerShell/bin", buildLibraries.BuildLibraries.NETDevelopmentBinariesPath);

                var projectBuild = Assert.IsType<ConfigurationProjectBuildSegment>(prepared.PipelineSpec.Segments[1]);
                Assert.Equal(Path.Combine(root.FullName, "Module", "Build", "project.build.json"), projectBuild.Configuration.ConfigPath);
                Assert.Equal("Artefacts/ProjectBuild/options", projectBuild.Configuration.Options!["OutputPath"]);
                Assert.Equal("Build/project-options-plan.json", projectBuild.Configuration.Options["PlanOutputPath"]);

                var artefact = Assert.IsType<ConfigurationArtefactSegment>(prepared.PipelineSpec.Segments[2]);
                Assert.Equal(Path.Combine(root.FullName, "Module", "Artefacts", "Packed"), artefact.Configuration.Path);
                Assert.Equal(Path.Combine(root.FullName, "Module", "Build", "LICENSE"), artefact.Configuration.DirectoryOutput![0].Source);
                Assert.Equal("LICENSE", artefact.Configuration.DirectoryOutput[0].Destination);
                Assert.Equal("Build/Templates", artefact.Configuration.DirectoryOutput[1].Source);
                Assert.Equal("Templates", artefact.Configuration.DirectoryOutput[1].Destination);
                Assert.Equal("Docs/Help", artefact.Configuration.DirectoryOutput[2].Source);
                Assert.Equal("Help", artefact.Configuration.DirectoryOutput[2].Destination);
                Assert.Equal("Examples/NOTICE.txt", artefact.Configuration.FilesOutput![0].Source);
                Assert.Equal("NOTICE.txt", artefact.Configuration.FilesOutput[0].Destination);
                Assert.Equal("Build/NOTICE.txt", artefact.Configuration.FilesOutput[1].Source);
                Assert.Equal("RepoNotice.txt", artefact.Configuration.FilesOutput[1].Destination);
                Assert.Equal("Artefacts/ProjectBuild/packages/Foo.nupkg", artefact.Configuration.FilesOutput[2].Source);
                Assert.Equal("Packages/Foo.nupkg", artefact.Configuration.FilesOutput[2].Destination);
                Assert.Equal("Assets/logo.png", artefact.Configuration.FilesOutput[3].Source);
                Assert.Equal("Assets/logo.png", artefact.Configuration.FilesOutput[3].Destination);

                var artefactWithRelativeLayout = Assert.IsType<ConfigurationArtefactSegment>(prepared.PipelineSpec.Segments[3]);
                Assert.Equal("Artefacts/Unpacked", artefactWithRelativeLayout.Configuration.Path);
                Assert.Equal("Modules", artefactWithRelativeLayout.Configuration.RequiredModules.Path);
                Assert.Equal("Payload/MainModules", artefactWithRelativeLayout.Configuration.RequiredModules.ModulesPath);

                var packageBuild = Assert.IsType<ConfigurationPackageBuildSegment>(prepared.PipelineSpec.Segments[4]);
                Assert.Equal("Sources", packageBuild.Configuration.RootPath);
                Assert.Equal("Artefacts/ProjectBuild/packages", packageBuild.Configuration.OutputPath);
                Assert.Equal("Build/nuget.key", packageBuild.Configuration.PublishApiKeyFilePath);
                Assert.Equal("Build/nuget.secret", packageBuild.Configuration.NugetCredentialSecretFilePath);
                Assert.Equal("Build/github.token", packageBuild.Configuration.GitHubAccessTokenFilePath);
                Assert.Equal(Path.Combine(root.FullName, "Module", "Artefacts", "PackageBuild", "options"), packageBuild.Configuration.Options!["outputPath"]);
                Assert.Equal("Build/package-options-plan.json", packageBuild.Configuration.Options["PlanOutputPath"]);

                Assert.Equal(Path.Combine(root.FullName, "Module", "Artefacts", "Packed", "RequiredModules"), artefact.Configuration.RequiredModules.Path);
                Assert.Equal(Path.Combine(root.FullName, "Module", "Artefacts", "Packed", "Modules"), artefact.Configuration.RequiredModules.ModulesPath);

                var test = Assert.IsType<ConfigurationTestSegment>(prepared.PipelineSpec.Segments[5]);
                Assert.Equal("Tests", test.Configuration.TestsPath);

                var options = Assert.IsType<ConfigurationOptionsSegment>(prepared.PipelineSpec.Segments[6]);
                Assert.Equal("Build/cert.pfx", options.Options.Signing!.CertificatePFXPath);

                var publish = Assert.IsType<ConfigurationPublishSegment>(prepared.PipelineSpec.Segments[7]);
                Assert.Equal("Build/psgallery.key", publish.Configuration.ApiKeyFilePath);

                var action = Assert.IsType<ConfigurationActionSegment>(prepared.PipelineSpec.Segments[8]);
                Assert.Equal("Build/Test-ReleaseReady.ps1", action.Configuration.FilePath);
                Assert.Equal("Build", action.Configuration.WorkingDirectory);

                var release = Assert.IsType<ConfigurationReleaseSegment>(prepared.PipelineSpec.Segments[9]);
                Assert.Equal("Artefacts/UploadReady/<ModuleVersion>", release.Configuration.StageRoot);

                var validation = Assert.IsType<ConfigurationValidationSegment>(prepared.PipelineSpec.Segments[10]);
                Assert.Equal("Tests", validation.Settings.Tests.TestPath);

                var documentation = Assert.IsType<ConfigurationDocumentationSegment>(prepared.PipelineSpec.Segments[11]);
                Assert.Equal(Path.Combine(root.FullName, "Module", "Docs"), documentation.Configuration.Path);
                Assert.Equal("Docs/Readme.md", documentation.Configuration.PathReadme);

                var buildDocumentation = Assert.IsType<ConfigurationBuildDocumentationSegment>(prepared.PipelineSpec.Segments[12]);
                Assert.Equal(new[] { "Help/About", "Help/Workspace" }, buildDocumentation.Configuration.AboutTopicsSourcePath);

                var fileBackedArtefact = Assert.IsType<ConfigurationArtefactSegment>(prepared.PipelineSpec.Segments[13]);
                Assert.Equal(Path.Combine(root.FullName, "Module", "Artefacts", "FileBacked", "<TagModuleVersionWithPreRelease>"), fileBackedArtefact.Configuration.Path);
                Assert.Contains("module header", fileBackedArtefact.Configuration.PreScriptMerge, StringComparison.Ordinal);
                Assert.DoesNotContain("repo header", fileBackedArtefact.Configuration.PreScriptMerge, StringComparison.Ordinal);

                var workspaceFileBackedArtefact = Assert.IsType<ConfigurationArtefactSegment>(prepared.PipelineSpec.Segments[14]);
                Assert.Equal(Path.Combine(root.FullName, "Module", "Artefacts", "WorkspaceFileBacked", "<TagModuleVersionWithPreRelease>"), workspaceFileBackedArtefact.Configuration.Path);
                Assert.Contains("workspace-qualified module header", workspaceFileBackedArtefact.Configuration.PreScriptMerge, StringComparison.Ordinal);

                var appleApp = Assert.IsType<ConfigurationAppleAppSegment>(prepared.PipelineSpec.Segments[15]);
                Assert.Equal(".\\Tactra.xcodeproj", appleApp.Configuration.ProjectPath);

                var xcodeProject = Assert.IsType<ConfigurationXcodeProjectVersionSegment>(prepared.PipelineSpec.Segments[16]);
                Assert.Equal("Mac\\TactraMac.xcodeproj", xcodeProject.Configuration.Path);
            }
            finally
            {
                try { Directory.SetCurrentDirectory(previousCurrentDirectory); } catch { }
                Runspace.DefaultRunspace = previousRunspace;
                try { outsideRoot.Delete(recursive: true); } catch { }
            }
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Prepare_from_module_build_script_prefers_module_local_csproj_when_it_exists()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-modulebuild-module-csproj-" + Guid.NewGuid().ToString("N")));

        try
        {
            var moduleRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "Module"));
            var scriptRoot = Directory.CreateDirectory(Path.Combine(moduleRoot.FullName, "Build"));
            MarkRepositoryRoot(root.FullName);
            File.WriteAllText(Path.Combine(scriptRoot.FullName, "Build-Module.ps1"), string.Empty);
            var moduleProject = Path.Combine(moduleRoot.FullName, "Sources", "Foo", "Foo.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(moduleProject)!);
            File.WriteAllText(moduleProject, "<Project />");
            File.WriteAllText(Path.Combine(moduleRoot.FullName, "SampleModule.psd1"), "@{ ModuleVersion = '1.0.0' }");

            var prepared = new ModuleBuildPreparationService().Prepare(new ModuleBuildPreparationRequest
            {
                ParameterSetName = "Modern",
                ModuleName = "SampleModule",
                CurrentPath = root.FullName,
                ScriptRoot = scriptRoot.FullName,
                ResolvePath = path => Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(root.FullName, path)),
                CsprojPath = "Sources/Foo/Foo.csproj",
                DotNetFramework = Array.Empty<string>(),
                ExcludeDirectories = Array.Empty<string>(),
                ExcludeFiles = Array.Empty<string>()
            });

            Assert.Equal(moduleProject, prepared.PipelineSpec.Build.CsprojPath);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Prepare_from_module_build_script_uses_powershell_resolver_for_json_path_syntax()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-modulebuild-jsonpath-" + Guid.NewGuid().ToString("N")));

        try
        {
            var moduleRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "Module"));
            var scriptRoot = Directory.CreateDirectory(Path.Combine(moduleRoot.FullName, "Build"));
            MarkRepositoryRoot(root.FullName);
            File.WriteAllText(Path.Combine(scriptRoot.FullName, "Build-Module.ps1"), string.Empty);
            File.WriteAllText(Path.Combine(moduleRoot.FullName, "SampleModule.psd1"), "@{ ModuleVersion = '1.0.0' }");
            var resolvedJsonPath = Path.Combine(root.FullName, "ResolvedFromPSDrive", "powerforge.json");

            var prepared = new ModuleBuildPreparationService().Prepare(new ModuleBuildPreparationRequest
            {
                ParameterSetName = "Modern",
                ModuleName = "SampleModule",
                CurrentPath = root.FullName,
                ScriptRoot = scriptRoot.FullName,
                ResolvePath = path => string.Equals(path, "Temp:\\powerforge.json", StringComparison.OrdinalIgnoreCase)
                    ? resolvedJsonPath
                    : Path.GetFullPath(Path.Combine(root.FullName, path)),
                JsonOnly = true,
                JsonPath = "Temp:\\powerforge.json",
                DotNetFramework = Array.Empty<string>(),
                ExcludeDirectories = Array.Empty<string>(),
                ExcludeFiles = Array.Empty<string>()
            });

            Assert.Equal(resolvedJsonPath, prepared.JsonOutputPath);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Prepare_from_standalone_module_folder_keeps_workspace_paths_under_module()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-modulebuild-standalone-module-" + Guid.NewGuid().ToString("N")));

        try
        {
            var moduleRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "Module"));
            var scriptRoot = Directory.CreateDirectory(Path.Combine(moduleRoot.FullName, "Build"));
            File.WriteAllText(Path.Combine(scriptRoot.FullName, "Build-Module.ps1"), string.Empty);
            File.WriteAllText(Path.Combine(moduleRoot.FullName, "SampleModule.psd1"), "@{ ModuleVersion = '1.0.0' }");

            var prepared = new ModuleBuildPreparationService().Prepare(new ModuleBuildPreparationRequest
            {
                ParameterSetName = "Modern",
                ModuleName = "SampleModule",
                CurrentPath = moduleRoot.FullName,
                ScriptRoot = scriptRoot.FullName,
                ResolvePath = path => Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(moduleRoot.FullName, path)),
                StagingPath = "Artefacts/Staging",
                DiagnosticsBinaryConflictSearchRoot = new[] { "Modules" },
                InstallRootsWasBound = true,
                InstallRoots = new[] { "Artefacts/Modules" },
                DotNetFramework = Array.Empty<string>(),
                ExcludeDirectories = Array.Empty<string>(),
                ExcludeFiles = Array.Empty<string>()
            });

            Assert.Equal(moduleRoot.FullName, prepared.ProjectRoot);
            Assert.Equal(Path.Combine(moduleRoot.FullName, "Artefacts", "Staging"), prepared.PipelineSpec.Build.StagingPath);
            Assert.Equal(new[] { Path.Combine(moduleRoot.FullName, "Modules") }, prepared.PipelineSpec.Build.BinaryConflictSearchRoots);
            Assert.Equal(new[] { Path.Combine(moduleRoot.FullName, "Artefacts", "Modules") }, prepared.PipelineSpec.Install.Roots);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Prepare_from_modern_path_uses_input_root_for_workspace_paths()
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
                Assert.Equal(Path.Combine(root.FullName, "Sources", "Demo", "Demo.csproj"), buildLibraries.BuildLibraries.NETProjectPath);

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
    public void WritePipelineSpecJson_keeps_workspace_paths_portable_from_module_root()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-modulebuild-json-workspace-" + Guid.NewGuid().ToString("N")));

        try
        {
            var moduleRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "Module"));
            var buildRoot = Directory.CreateDirectory(Path.Combine(moduleRoot.FullName, "Build"));
            MarkRepositoryRoot(root.FullName);
            File.WriteAllText(Path.Combine(buildRoot.FullName, "Build-Module.ps1"), string.Empty);
            var jsonPath = Path.Combine(root.FullName, "Build", "powerforge.json");
            var externalRoot = Path.Combine(Path.GetTempPath(), "PowerForge", "External-" + Guid.NewGuid().ToString("N"));
            var externalPublishKey = Path.Combine(externalRoot, "psgallery.key");
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = "SampleModule",
                    SourcePath = moduleRoot.FullName,
                    BinaryConflictSearchRoots = new[] { Path.Combine(root.FullName, "Modules") }
                },
                Install = new ModulePipelineInstallOptions
                {
                    Roots = new[] { Path.Combine(root.FullName, "Artefacts", "Modules") }
                },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationOptionsSegment
                    {
                        Options = new ConfigurationOptions
                        {
                            Signing = new SigningOptionsConfiguration
                            {
                                CertificatePFXPath = Path.Combine(root.FullName, "Build", "cert.pfx")
                            }
                        }
                    },
                    new ConfigurationPublishSegment
                    {
                        Configuration = new PublishConfiguration
                        {
                            Destination = PublishDestination.PowerShellGallery,
                            ApiKeyFilePath = Path.Combine(root.FullName, "Build", "psgallery.key")
                        }
                    },
                    new ConfigurationPublishSegment
                    {
                        Configuration = new PublishConfiguration
                        {
                            Destination = PublishDestination.PowerShellGallery,
                            ApiKeyFilePath = externalPublishKey
                        }
                    },
                    new ConfigurationActionSegment
                    {
                        Configuration = new ModulePipelineActionConfiguration
                        {
                            FilePath = Path.Combine(root.FullName, "Build", "Test-ReleaseReady.ps1"),
                            WorkingDirectory = Path.Combine(root.FullName, "Build")
                        }
                    },
                    new ConfigurationReleaseSegment
                    {
                        Configuration = new ReleaseConfiguration
                        {
                            StageRoot = Path.Combine(root.FullName, "Artefacts", "UploadReady", "<ModuleVersion>")
                        }
                    }
                }
            };

            new ModuleBuildPreparationService().WritePipelineSpecJson(spec, jsonPath);

            var json = File.ReadAllText(jsonPath);
            Assert.Contains("\"SourcePath\": \"../Module\"", json, StringComparison.Ordinal);
            Assert.Contains("\"../Modules\"", json, StringComparison.Ordinal);
            Assert.Contains("\"Roots\": [", json, StringComparison.Ordinal);
            Assert.Contains("\"../Artefacts/Modules\"", json, StringComparison.Ordinal);
            Assert.Contains("\"CertificatePFXPath\": \"../Build/cert.pfx\"", json, StringComparison.Ordinal);
            Assert.Contains("\"ApiKeyFilePath\": \"../Build/psgallery.key\"", json, StringComparison.Ordinal);
            Assert.Contains($"\"ApiKeyFilePath\": \"{externalPublishKey.Replace('\\', '/')}\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"FilePath\": \"../Build/Test-ReleaseReady.ps1\"", json, StringComparison.Ordinal);
            Assert.Contains("\"WorkingDirectory\": \"../Build\"", json, StringComparison.Ordinal);
            var jsonSpec = JsonSerializer.Deserialize<ModulePipelineSpec>(json, CreateJsonOptions());
            var release = Assert.IsType<ConfigurationReleaseSegment>(Assert.Single(jsonSpec!.Segments.OfType<ConfigurationReleaseSegment>()));
            Assert.Equal("../Artefacts/UploadReady/<ModuleVersion>", release.Configuration.StageRoot);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void WritePipelineSpecJson_preserves_external_sibling_paths_for_standalone_module_folder()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-modulebuild-json-standalone-module-" + Guid.NewGuid().ToString("N")));

        try
        {
            var moduleRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "Module"));
            var siblingSecretPath = Path.Combine(root.FullName, "Secrets", "psgallery.key");
            var jsonPath = Path.Combine(moduleRoot.FullName, ".powerforge", "powerforge.json");
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = "SampleModule",
                    SourcePath = moduleRoot.FullName
                },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationPublishSegment
                    {
                        Configuration = new PublishConfiguration
                        {
                            Destination = PublishDestination.PowerShellGallery,
                            ApiKeyFilePath = siblingSecretPath
                        }
                    }
                }
            };

            new ModuleBuildPreparationService().WritePipelineSpecJson(spec, jsonPath);

            var json = File.ReadAllText(jsonPath);
            Assert.Contains($"\"ApiKeyFilePath\": \"{siblingSecretPath.Replace('\\', '/')}\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"ApiKeyFilePath\": \"../Secrets/psgallery.key\"", json, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void WritePipelineSpecJson_preserves_external_documentation_and_portable_about_paths()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-modulebuild-json-docs-" + Guid.NewGuid().ToString("N")));

        try
        {
            var jsonPath = Path.Combine(root.FullName, ".powerforge", "powerforge.json");
            var externalDocs = Path.Combine(Path.GetTempPath(), "PowerForge", "ExternalDocs-" + Guid.NewGuid().ToString("N"));
            var externalReadme = Path.Combine(Path.GetTempPath(), "PowerForge", "ExternalReadme-" + Guid.NewGuid().ToString("N"), "README.md");
            var projectAboutTopics = Path.Combine(root.FullName, "Help", "About");
            var externalAboutTopics = Path.Combine(Path.GetTempPath(), "PowerForge", "ExternalAbout-" + Guid.NewGuid().ToString("N"));
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = "SampleModule",
                    SourcePath = root.FullName
                },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationDocumentationSegment
                    {
                        Configuration = new DocumentationConfiguration
                        {
                            Path = externalDocs,
                            PathReadme = externalReadme
                        }
                    },
                    new ConfigurationBuildDocumentationSegment
                    {
                        Configuration = new BuildDocumentationConfiguration
                        {
                            Enable = true,
                            AboutTopicsSourcePath = new[] { projectAboutTopics, externalAboutTopics }
                        }
                    }
                }
            };

            var service = new ModuleBuildPreparationService();
            service.WritePipelineSpecJson(spec, jsonPath);

            var json = File.ReadAllText(jsonPath);
            Assert.Contains($"\"Path\": \"{externalDocs.Replace('\\', '/')}\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"\"PathReadme\": \"{externalReadme.Replace('\\', '/')}\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"Help/About\"", json, StringComparison.Ordinal);
            Assert.Contains(externalAboutTopics.Replace('\\', '/'), json, StringComparison.OrdinalIgnoreCase);

            var jsonSpec = JsonSerializer.Deserialize<ModulePipelineSpec>(json, CreateJsonOptions());
            Assert.NotNull(jsonSpec);

            service.ResolvePipelineSpecPaths(jsonSpec!, jsonPath);
            var documentation = Assert.IsType<ConfigurationDocumentationSegment>(jsonSpec!.Segments[0]);
            var buildDocumentation = Assert.IsType<ConfigurationBuildDocumentationSegment>(jsonSpec.Segments[1]);
            Assert.Equal(externalDocs, documentation.Configuration.Path);
            Assert.Equal(externalReadme, documentation.Configuration.PathReadme);
            Assert.Equal("Help/About", buildDocumentation.Configuration.AboutTopicsSourcePath[0]);
            Assert.Equal(externalAboutTopics, buildDocumentation.Configuration.AboutTopicsSourcePath[1]);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void WritePipelineSpecJson_preserves_tokenized_artefact_paths()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-modulebuild-json-tokens-" + Guid.NewGuid().ToString("N")));

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
                        ArtefactType = ArtefactType.Packed,
                        Configuration = new ArtefactConfiguration
                        {
                            Path = Path.Combine(root.FullName, "Module", "Artefacts", "Packed", "<TagModuleVersionWithPreRelease>"),
                            RequiredModules = new ArtefactRequiredModulesConfiguration
                            {
                                Path = Path.Combine(root.FullName, "Module", "Artefacts", "Packed", "<TagModuleVersionWithPreRelease>", "RequiredModules"),
                                ModulesPath = Path.Combine(root.FullName, "Module", "Artefacts", "Packed", "<TagModuleVersionWithPreRelease>", "Modules")
                            }
                        }
                    },
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Packed,
                        Configuration = new ArtefactConfiguration
                        {
                            Path = Path.Combine(root.FullName, "Module", "Artefacts", "Packed-<TagModuleVersionWithPreRelease>")
                        }
                    },
                    new ConfigurationReleaseSegment
                    {
                        Configuration = new ReleaseConfiguration
                        {
                            StageRoot = Path.Combine(root.FullName, "Artefacts", "UploadReady-<ModuleVersion>")
                        }
                    }
                }
            };

            new ModuleBuildPreparationService().WritePipelineSpecJson(spec, jsonPath);

            var json = File.ReadAllText(jsonPath);
            var jsonSpec = JsonSerializer.Deserialize<ModulePipelineSpec>(json, CreateJsonOptions());
            Assert.NotNull(jsonSpec);

            var artefact = Assert.IsType<ConfigurationArtefactSegment>(jsonSpec!.Segments[0]);
            Assert.Equal("Module/Artefacts/Packed/<TagModuleVersionWithPreRelease>", artefact.Configuration.Path);
            Assert.Equal("Module/Artefacts/Packed/<TagModuleVersionWithPreRelease>/RequiredModules", artefact.Configuration.RequiredModules.Path);
            Assert.Equal("Module/Artefacts/Packed/<TagModuleVersionWithPreRelease>/Modules", artefact.Configuration.RequiredModules.ModulesPath);
            var embeddedTokenArtefact = Assert.IsType<ConfigurationArtefactSegment>(jsonSpec.Segments[1]);
            Assert.Equal("Module/Artefacts/Packed-<TagModuleVersionWithPreRelease>", embeddedTokenArtefact.Configuration.Path);
            var release = Assert.IsType<ConfigurationReleaseSegment>(jsonSpec.Segments[2]);
            Assert.Equal("Artefacts/UploadReady-<ModuleVersion>", release.Configuration.StageRoot);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void WritePipelineSpecJson_preserves_external_release_stage_root()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-modulebuild-json-release-external-" + Guid.NewGuid().ToString("N")));

        try
        {
            var jsonPath = Path.Combine(root.FullName, ".powerforge", "powerforge.json");
            var externalReleaseRoot = Path.Combine(Path.GetTempPath(), "PowerForge", "Release-" + Guid.NewGuid().ToString("N"));
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = "SampleModule",
                    SourcePath = root.FullName
                },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationReleaseSegment
                    {
                        Configuration = new ReleaseConfiguration
                        {
                            StageRoot = externalReleaseRoot
                        }
                    }
                }
            };

            var service = new ModuleBuildPreparationService();
            service.WritePipelineSpecJson(spec, jsonPath);

            var json = File.ReadAllText(jsonPath);
            Assert.Contains($"\"StageRoot\": \"{externalReleaseRoot.Replace('\\', '/')}\"", json, StringComparison.OrdinalIgnoreCase);

            var jsonSpec = JsonSerializer.Deserialize<ModulePipelineSpec>(json, CreateJsonOptions());
            Assert.NotNull(jsonSpec);
            service.ResolvePipelineSpecPaths(jsonSpec!, jsonPath);
            var release = Assert.IsType<ConfigurationReleaseSegment>(Assert.Single(jsonSpec!.Segments.OfType<ConfigurationReleaseSegment>()));
            Assert.Equal(Path.GetFullPath(externalReleaseRoot), release.Configuration.StageRoot);
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
            var buildStagingPath = Path.Combine(root.FullName, "Artifacts", "Module", "Staging");
            var buildCsprojPath = Path.Combine(root.FullName, "Sources", "TopLevel", "TopLevel.csproj");
            var binaryConflictSearchRoot = Path.Combine(root.FullName, "Modules");
            var externalBinaryConflictSearchRoot = Path.Combine(Path.GetTempPath(), "PowerForge", "BinaryScan-" + Guid.NewGuid().ToString("N"));
            var installRoot = Path.Combine(root.FullName, "Artifacts", "Modules");
            var externalInstallRoot = Path.Combine(Path.GetTempPath(), "PowerShell", "Modules");
            var diagnosticsBaselinePath = Path.Combine(root.FullName, "Build", "diagnostics.json");
            var developmentBinaries = Path.Combine(root.FullName, "Sources", "SampleModule.PowerShell", "bin");
            var externalNetProject = Path.Combine(Path.GetTempPath(), "PowerForge", "ExternalProject-" + Guid.NewGuid().ToString("N"), "External.PowerShell.csproj");
            var externalDevelopmentBinaries = Path.Combine(Path.GetTempPath(), "PowerForge", "ExternalBinaries-" + Guid.NewGuid().ToString("N"));
            var packageRoot = Path.Combine(root.FullName, "Sources");
            var externalPackageOutputPath = Path.Combine(Path.GetTempPath(), "PowerForge", "ExternalPackageOutput-" + Guid.NewGuid().ToString("N"));
            var stagingRoot = Path.Combine(root.FullName, "Artifacts", "Packages", "Staging");
            var packagePublishKey = Path.Combine(Path.GetTempPath(), "PowerForge", "Secrets-" + Guid.NewGuid().ToString("N"), "nuget.key");
            var packageCredentialSecret = Path.Combine(Path.GetTempPath(), "PowerForge", "Secrets-" + Guid.NewGuid().ToString("N"), "nuget.secret");
            var packageGitHubToken = Path.Combine(Path.GetTempPath(), "PowerForge", "Secrets-" + Guid.NewGuid().ToString("N"), "github.token");
            var releaseRoot = Path.Combine(root.FullName, "Artifacts", "Release");
            var artefactRoot = Path.Combine(root.FullName, "Module", "Artefacts", "Packed");
            var artefactRequiredModules = Path.Combine(root.FullName, "Module", "Artefacts", "Packed", "RequiredModules");
            var artefactModules = Path.Combine(root.FullName, "Module", "Artefacts", "Packed", "Modules");
            var externalArtefactRoot = Path.Combine(Path.GetTempPath(), "PowerForge", "ExternalArtefacts-" + Guid.NewGuid().ToString("N"));
            var publishKey = Path.Combine(Path.GetTempPath(), "PowerForge", "Secrets-" + Guid.NewGuid().ToString("N"), "psgallery.key");
            var actionFile = Path.Combine(Path.GetTempPath(), "PowerForge", "Actions-" + Guid.NewGuid().ToString("N"), "Test-ReleaseReady.ps1");
            var actionWorkingDirectory = Path.Combine(Path.GetTempPath(), "PowerForge", "Actions-" + Guid.NewGuid().ToString("N"));
            var externalTestsPath = Path.Combine(Path.GetTempPath(), "PowerForge", "ExternalTests-" + Guid.NewGuid().ToString("N"));
            var externalValidationTestsPath = Path.Combine(Path.GetTempPath(), "PowerForge", "ExternalValidationTests-" + Guid.NewGuid().ToString("N"));
            var signingPfxPath = Path.Combine(Path.GetTempPath(), "PowerForge", "Secrets-" + Guid.NewGuid().ToString("N"), "cert.pfx");
            var documentationPath = Path.Combine(root.FullName, "Module", "Docs");
            var documentationReadmePath = Path.Combine(root.FullName, "README.md");
            const string aboutTopicsPath = "Help/About";
            var copyDirectorySource = Path.Combine(root.FullName, "Build", "Templates");
            var copyFileSource = Path.Combine(root.FullName, "Build", "NOTICE.txt");
            var externalCopyFileSource = Path.Combine(Path.GetTempPath(), "PowerForge", "Shared-" + Guid.NewGuid().ToString("N"), "NOTICE.txt");
            var projectOptionsOutputPath = Path.Combine(root.FullName, "Artifacts", "ProjectBuild", "options");
            var projectOptionsPlanPath = Path.Combine(root.FullName, "Build", "project-options-plan.json");
            var packageOptionsOutputPath = Path.Combine(root.FullName, "Artifacts", "PackageBuild", "options");
            var packageOptionsCredentialPath = Path.Combine(Path.GetTempPath(), "PowerForge", "OptionSecrets-" + Guid.NewGuid().ToString("N"), "nuget-option.key");
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = "SampleModule",
                    SourcePath = root.FullName,
                    StagingPath = buildStagingPath,
                    CsprojPath = buildCsprojPath,
                    BinaryConflictSearchRoots = new[] { binaryConflictSearchRoot, externalBinaryConflictSearchRoot }
                },
                Install = new ModulePipelineInstallOptions
                {
                    Roots = new[] { installRoot, externalInstallRoot }
                },
                Diagnostics = new ModulePipelineDiagnosticsOptions
                {
                    BaselinePath = diagnosticsBaselinePath,
                    BinaryConflictSearchRoots = new[] { binaryConflictSearchRoot, externalBinaryConflictSearchRoot }
                },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationProjectBuildSegment
                    {
                        Configuration = new ProjectBuildConfigurationReference
                        {
                            ConfigPath = projectConfig,
                            BuildBeforeModule = true,
                            Options = new Dictionary<string, object?>
                            {
                                ["OutputPath"] = projectOptionsOutputPath,
                                ["PlanOutputPath"] = projectOptionsPlanPath
                            }
                        }
                    },
                    new ConfigurationBuildLibrariesSegment
                    {
                        BuildLibraries = new BuildLibrariesConfiguration
                        {
                            NETProjectPath = externalNetProject,
                            DevelopmentBinariesPath = externalDevelopmentBinaries,
                            NETDevelopmentBinariesPath = developmentBinaries
                        }
                    },
                    new ConfigurationPackageBuildSegment
                    {
                        Configuration = new PackageBuildConfiguration
                        {
                            RootPath = packageRoot,
                            StagingPath = stagingRoot,
                            OutputPath = externalPackageOutputPath,
                            PlanOutputPath = Path.Combine(root.FullName, "Artifacts", "Packages", "plan.json"),
                            PublishApiKeyFilePath = packagePublishKey,
                            NugetCredentialSecretFilePath = packageCredentialSecret,
                            GitHubAccessTokenFilePath = packageGitHubToken,
                            Options = new Dictionary<string, object?>
                            {
                                ["outputPath"] = packageOptionsOutputPath,
                                ["PlanOutputPath"] = externalPackageOutputPath,
                                ["PublishApiKeyFilePath"] = packageOptionsCredentialPath
                            }
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
                                },
                                new ArtefactCopyMapping
                                {
                                    Source = externalCopyFileSource,
                                    Destination = "ExternalNotice.txt"
                                }
                            }
                        }
                    },
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Packed,
                        Configuration = new ArtefactConfiguration
                        {
                            Path = externalArtefactRoot
                        }
                    },
                    new ConfigurationTestSegment
                    {
                        Configuration = new TestConfiguration
                        {
                            TestsPath = externalTestsPath
                        }
                    },
                    new ConfigurationOptionsSegment
                    {
                        Options = new ConfigurationOptions
                        {
                            Signing = new SigningOptionsConfiguration
                            {
                                CertificatePFXPath = signingPfxPath
                            }
                        }
                    },
                    new ConfigurationDocumentationSegment
                    {
                        Configuration = new DocumentationConfiguration
                        {
                            Path = documentationPath,
                            PathReadme = documentationReadmePath
                        }
                    },
                    new ConfigurationBuildDocumentationSegment
                    {
                        Configuration = new BuildDocumentationConfiguration
                        {
                            Enable = true,
                            AboutTopicsSourcePath = new[] { aboutTopicsPath }
                        }
                    },
                    new ConfigurationValidationSegment
                    {
                        Settings = new ModuleValidationSettings
                        {
                            Enable = true,
                            Tests = new TestSuiteValidationSettings
                            {
                                Enable = true,
                                TestPath = externalValidationTestsPath
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
            Assert.Contains("\"StagingPath\": \"../Artifacts/Module/Staging\"", json, StringComparison.Ordinal);
            Assert.Contains("\"CsprojPath\": \"../Sources/TopLevel/TopLevel.csproj\"", json, StringComparison.Ordinal);
            Assert.Contains("\"BinaryConflictSearchRoots\": [", json, StringComparison.Ordinal);
            Assert.Contains("\"Modules\"", json, StringComparison.Ordinal);
            Assert.Contains(externalBinaryConflictSearchRoot.Replace('\\', '/'), json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"Roots\": [", json, StringComparison.Ordinal);
            Assert.Contains("\"Artifacts/Modules\"", json, StringComparison.Ordinal);
            Assert.Contains("\"BaselinePath\": \"../Build/diagnostics.json\"", json, StringComparison.Ordinal);
            Assert.Contains("\"ConfigPath\": \"Build/project.build.json\"", json, StringComparison.Ordinal);
            Assert.Contains($"\"NETProjectPath\": \"{externalNetProject.Replace('\\', '/')}\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"\"DevelopmentBinariesPath\": \"{externalDevelopmentBinaries.Replace('\\', '/')}\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"NETDevelopmentBinariesPath\": \"Sources/SampleModule.PowerShell/bin\"", json, StringComparison.Ordinal);
            Assert.Contains("\"RootPath\": \"Sources\"", json, StringComparison.Ordinal);
            Assert.Contains("\"StagingPath\": \"Artifacts/Packages/Staging\"", json, StringComparison.Ordinal);
            Assert.Contains($"\"OutputPath\": \"{externalPackageOutputPath.Replace('\\', '/')}\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"PlanOutputPath\": \"Artifacts/Packages/plan.json\"", json, StringComparison.Ordinal);
            Assert.Contains("\"OutputPath\": \"../Artifacts/ProjectBuild/options\"", json, StringComparison.Ordinal);
            Assert.Contains("\"PlanOutputPath\": \"project-options-plan.json\"", json, StringComparison.Ordinal);
            Assert.Contains("\"outputPath\": \"Artifacts/PackageBuild/options\"", json, StringComparison.Ordinal);
            Assert.Contains($"\"PlanOutputPath\": \"{externalPackageOutputPath.Replace('\\', '/')}\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"\"PublishApiKeyFilePath\": \"{packagePublishKey.Replace('\\', '/')}\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"\"NugetCredentialSecretFilePath\": \"{packageCredentialSecret.Replace('\\', '/')}\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"\"GitHubAccessTokenFilePath\": \"{packageGitHubToken.Replace('\\', '/')}\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"\"PublishApiKeyFilePath\": \"{packageOptionsCredentialPath.Replace('\\', '/')}\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"Path\": \"Module/Artefacts/Packed/RequiredModules\"", json, StringComparison.Ordinal);
            Assert.Contains("\"ModulesPath\": \"Module/Artefacts/Packed/Modules\"", json, StringComparison.Ordinal);
            Assert.Contains($"\"Path\": \"{externalArtefactRoot.Replace('\\', '/')}\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"Source\": \"Build/Templates\"", json, StringComparison.Ordinal);
            Assert.Contains("\"Source\": \"Build/NOTICE.txt\"", json, StringComparison.Ordinal);
            Assert.Contains($"\"Source\": \"{externalCopyFileSource.Replace('\\', '/')}\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"\"TestsPath\": \"{externalTestsPath.Replace('\\', '/')}\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"\"CertificatePFXPath\": \"{signingPfxPath.Replace('\\', '/')}\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"Path\": \"Module/Docs\"", json, StringComparison.Ordinal);
            Assert.Contains("\"PathReadme\": \"README.md\"", json, StringComparison.Ordinal);
            Assert.Contains($"\"TestPath\": \"{externalValidationTestsPath.Replace('\\', '/')}\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"AboutTopicsSourcePath\": [", json, StringComparison.Ordinal);
            Assert.Contains("\"Help/About\"", json, StringComparison.Ordinal);
            Assert.Contains($"\"ApiKeyFilePath\": \"{publishKey.Replace('\\', '/')}\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"\"FilePath\": \"{actionFile.Replace('\\', '/')}\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"\"WorkingDirectory\": \"{actionWorkingDirectory.Replace('\\', '/')}\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"StageRoot\": \"Artifacts/Release\"", json, StringComparison.Ordinal);

            var jsonSpec = JsonSerializer.Deserialize<ModulePipelineSpec>(json, CreateJsonOptions());
            Assert.NotNull(jsonSpec);
            Assert.Contains(jsonSpec!.Install.Roots!, root => string.Equals(root, externalInstallRoot.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));

            service.ResolvePipelineSpecPaths(jsonSpec, jsonPath);
            Assert.Equal(buildStagingPath, jsonSpec!.Build.StagingPath);
            Assert.Equal(buildCsprojPath, jsonSpec.Build.CsprojPath);
            Assert.Equal(new[] { binaryConflictSearchRoot, externalBinaryConflictSearchRoot }, jsonSpec.Build.BinaryConflictSearchRoots);
            Assert.Equal(new[] { installRoot, externalInstallRoot }, jsonSpec.Install.Roots);
            Assert.Equal(diagnosticsBaselinePath, jsonSpec.Diagnostics!.BaselinePath);
            Assert.Equal(new[] { binaryConflictSearchRoot, externalBinaryConflictSearchRoot }, jsonSpec.Diagnostics.BinaryConflictSearchRoots);
            var projectBuild = Assert.IsType<ConfigurationProjectBuildSegment>(jsonSpec!.Segments[0]);
            var buildLibraries = Assert.IsType<ConfigurationBuildLibrariesSegment>(jsonSpec.Segments[1]);
            var packageBuild = Assert.IsType<ConfigurationPackageBuildSegment>(jsonSpec.Segments[2]);
            var artefact = Assert.IsType<ConfigurationArtefactSegment>(jsonSpec.Segments[3]);
            var externalArtefact = Assert.IsType<ConfigurationArtefactSegment>(jsonSpec.Segments[4]);
            var test = Assert.IsType<ConfigurationTestSegment>(jsonSpec.Segments[5]);
            var options = Assert.IsType<ConfigurationOptionsSegment>(jsonSpec.Segments[6]);
            var documentation = Assert.IsType<ConfigurationDocumentationSegment>(jsonSpec.Segments[7]);
            var buildDocumentation = Assert.IsType<ConfigurationBuildDocumentationSegment>(jsonSpec.Segments[8]);
            var validation = Assert.IsType<ConfigurationValidationSegment>(jsonSpec.Segments[9]);
            var publish = Assert.IsType<ConfigurationPublishSegment>(jsonSpec.Segments[10]);
            var action = Assert.IsType<ConfigurationActionSegment>(jsonSpec.Segments[11]);
            var release = Assert.IsType<ConfigurationReleaseSegment>(jsonSpec.Segments[12]);
            Assert.Equal(projectConfig, projectBuild.Configuration.ConfigPath);
            Assert.Equal(projectOptionsOutputPath, projectBuild.Configuration.Options!["OutputPath"]);
            Assert.Equal(projectOptionsPlanPath, projectBuild.Configuration.Options["PlanOutputPath"]);
            Assert.Equal(externalNetProject, buildLibraries.BuildLibraries.NETProjectPath);
            Assert.Equal(externalDevelopmentBinaries, buildLibraries.BuildLibraries.DevelopmentBinariesPath);
            Assert.Equal(developmentBinaries, buildLibraries.BuildLibraries.NETDevelopmentBinariesPath);
            Assert.Equal(packageRoot, packageBuild.Configuration.RootPath);
            Assert.Equal(stagingRoot, packageBuild.Configuration.StagingPath);
            Assert.Equal(externalPackageOutputPath, packageBuild.Configuration.OutputPath);
            Assert.Equal(packagePublishKey, packageBuild.Configuration.PublishApiKeyFilePath);
            Assert.Equal(packageCredentialSecret, packageBuild.Configuration.NugetCredentialSecretFilePath);
            Assert.Equal(packageGitHubToken, packageBuild.Configuration.GitHubAccessTokenFilePath);
            Assert.Equal(packageOptionsOutputPath, packageBuild.Configuration.Options!["outputPath"]);
            Assert.Equal(externalPackageOutputPath.Replace('\\', '/'), packageBuild.Configuration.Options["PlanOutputPath"]);
            Assert.Equal(packageOptionsCredentialPath.Replace('\\', '/'), packageBuild.Configuration.Options["PublishApiKeyFilePath"]);
            Assert.Equal(artefactRoot, artefact.Configuration.Path);
            Assert.Equal(artefactRequiredModules, artefact.Configuration.RequiredModules.Path);
            Assert.Equal(artefactModules, artefact.Configuration.RequiredModules.ModulesPath);
            Assert.Equal(copyDirectorySource, artefact.Configuration.DirectoryOutput![0].Source);
            Assert.Equal("Templates", artefact.Configuration.DirectoryOutput[0].Destination);
            Assert.Equal(copyFileSource, artefact.Configuration.FilesOutput![0].Source);
            Assert.Equal("NOTICE.txt", artefact.Configuration.FilesOutput[0].Destination);
            Assert.Equal(externalCopyFileSource.Replace('\\', '/'), artefact.Configuration.FilesOutput[1].Source);
            Assert.Equal("ExternalNotice.txt", artefact.Configuration.FilesOutput[1].Destination);
            Assert.Equal(externalArtefactRoot, externalArtefact.Configuration.Path);
            Assert.Equal(externalTestsPath, test.Configuration.TestsPath);
            Assert.Equal(signingPfxPath, options.Options.Signing!.CertificatePFXPath);
            Assert.Equal(documentationPath, documentation.Configuration.Path);
            Assert.Equal(documentationReadmePath, documentation.Configuration.PathReadme);
            Assert.Equal("Help/About", buildDocumentation.Configuration.AboutTopicsSourcePath[0]);
            Assert.Equal(externalValidationTestsPath, validation.Settings.Tests.TestPath);
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
                    },
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Packed,
                        Configuration = new ArtefactConfiguration
                        {
                            Path = "Artefacts/Packed/<TagModuleVersionWithPreRelease>",
                            RequiredModules = new ArtefactRequiredModulesConfiguration
                            {
                                Path = "Artefacts/Packed/<TagModuleVersionWithPreRelease>/RequiredModules",
                                ModulesPath = "Artefacts/Packed/<TagModuleVersionWithPreRelease>/Modules"
                            },
                            FilesOutput = new[]
                            {
                                new ArtefactCopyMapping
                                {
                                    Source = "Payload/<ModuleVersion>/NOTICE.txt",
                                    Destination = "NOTICE.txt"
                                }
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

            var tokenizedLayout = Assert.IsType<ConfigurationArtefactSegment>(spec.Segments[2]);
            Assert.Equal("Artefacts/Packed/<TagModuleVersionWithPreRelease>", tokenizedLayout.Configuration.Path);
            Assert.Equal("Artefacts/Packed/<TagModuleVersionWithPreRelease>/RequiredModules", tokenizedLayout.Configuration.RequiredModules.Path);
            Assert.Equal("Artefacts/Packed/<TagModuleVersionWithPreRelease>/Modules", tokenizedLayout.Configuration.RequiredModules.ModulesPath);
            Assert.Equal("Payload/<ModuleVersion>/NOTICE.txt", tokenizedLayout.Configuration.FilesOutput![0].Source);
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
                ResolvePath = path => Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(root.FullName, path)),
                JsonOnly = true,
                JsonPath = "out.json"
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
            Assert.Equal(Path.Combine(root.FullName, "out.json"), prepared.JsonOutputPath);
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

    private static void MarkRepositoryRoot(string rootPath)
        => Directory.CreateDirectory(Path.Combine(rootPath, ".git"));

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
