using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineUnifiedReleaseTests
{
    [Fact]
    public void Run_RejectsPackedLayoutModuleRootThatEscapesTemporaryPayloadRoot()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var artefactsRoot = Path.Combine(root.FullName, "Artifacts");
            var packageOutput = Path.Combine(artefactsRoot, "ProjectBuild");
            var packagePath = Path.Combine(packageOutput, "Mailozaurr.1.0.0.nupkg");

            var runner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: null,
                manifestMutator: null,
                missingFunctionAnalysisService: null,
                scriptFunctionExportDetector: null,
                packageBuildExecutor: (request, configuration, configPath) =>
                {
                    Directory.CreateDirectory(packageOutput);
                    File.WriteAllText(packagePath, "package");

                    var release = new DotNetRepositoryReleaseResult { Success = true };
                    var project = new DotNetRepositoryProjectResult
                    {
                        ProjectName = "Mailozaurr",
                        PackageId = "Mailozaurr",
                        IsPackable = true,
                        NewVersion = "1.0.0"
                    };
                    project.Packages.Add(packagePath);
                    release.Projects.Add(project);

                    return new ProjectBuildHostExecutionResult
                    {
                        Success = true,
                        ConfigPath = configPath ?? request.ConfigPath,
                        RootPath = root.FullName,
                        OutputPath = packageOutput,
                        Result = new ProjectBuildResult
                        {
                            Success = true,
                            Release = release
                        }
                    };
                });

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0"
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationPackageBuildSegment
                    {
                        Configuration = new PackageBuildConfiguration
                        {
                            Name = "Packages",
                            RootPath = "Sources",
                            BuildBeforeModule = true
                        }
                    },
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Packed,
                        Configuration = new ArtefactConfiguration
                        {
                            Enabled = true,
                            Path = Path.Combine(artefactsRoot, "Packed"),
                            RequiredModules = new ArtefactRequiredModulesConfiguration
                            {
                                ModulesPath = Path.Combine("..", "ProjectBuild")
                            }
                        }
                    }
                }
            };

            var ex = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));

            Assert.Contains("resolves outside the temporary packed artefact root", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("package", File.ReadAllText(packagePath));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_RejectsUnpackedLayoutChildInsideProtectedProjectBuildStaging()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var artefactsRoot = Path.Combine(root.FullName, "Artifacts");
            var protectedStagingPath = Path.Combine(artefactsRoot, "ProjectBuild");
            var protectedChildPath = Path.Combine(protectedStagingPath, moduleName);
            Directory.CreateDirectory(protectedChildPath);
            File.WriteAllText(Path.Combine(protectedChildPath, "project-build.txt"), "project-build");

            var runner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: null,
                manifestMutator: null,
                missingFunctionAnalysisService: null,
                scriptFunctionExportDetector: null,
                packageBuildExecutor: (request, configuration, configPath) =>
                {
                    return new ProjectBuildHostExecutionResult
                    {
                        Success = true,
                        ConfigPath = configPath ?? request.ConfigPath,
                        RootPath = root.FullName,
                        StagingPath = protectedStagingPath,
                        Result = new ProjectBuildResult
                        {
                            Success = true,
                            Release = new DotNetRepositoryReleaseResult { Success = true }
                        }
                    };
                });

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0"
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationPackageBuildSegment
                    {
                        Configuration = new PackageBuildConfiguration
                        {
                            Name = "Packages",
                            RootPath = "Sources",
                            BuildBeforeModule = true
                        }
                    },
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Unpacked,
                        Configuration = new ArtefactConfiguration
                        {
                            Enabled = true,
                            Path = Path.Combine(artefactsRoot, "Unpacked"),
                            RequiredModules = new ArtefactRequiredModulesConfiguration
                            {
                                ModulesPath = protectedStagingPath
                            }
                        }
                    }
                }
            };

            var ex = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));

            Assert.Contains("main module copy root", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("project build staging path", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("project-build", File.ReadAllText(Path.Combine(protectedChildPath, "project-build.txt")));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_AllowsPackedModuleZipBesideProjectReleaseZipWithDifferentName()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var releaseOutput = Path.Combine(root.FullName, "Artifacts", "Releases");
            var releaseZipPath = Path.Combine(releaseOutput, "Mailozaurr.1.0.0.zip");

            var runner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: null,
                manifestMutator: null,
                missingFunctionAnalysisService: null,
                scriptFunctionExportDetector: null,
                packageBuildExecutor: (request, configuration, configPath) =>
                {
                    Directory.CreateDirectory(releaseOutput);
                    File.WriteAllText(releaseZipPath, "project-release-zip");

                    var release = new DotNetRepositoryReleaseResult { Success = true };
                    var project = new DotNetRepositoryProjectResult
                    {
                        ProjectName = "Mailozaurr",
                        PackageId = "Mailozaurr",
                        IsPackable = true,
                        NewVersion = "1.0.0",
                        ReleaseZipPath = releaseZipPath
                    };
                    release.Projects.Add(project);

                    return new ProjectBuildHostExecutionResult
                    {
                        Success = true,
                        ConfigPath = configPath ?? request.ConfigPath,
                        RootPath = root.FullName,
                        ReleaseZipOutputPath = releaseOutput,
                        Result = new ProjectBuildResult
                        {
                            Success = true,
                            Release = release
                        }
                    };
                });

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0"
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationPackageBuildSegment
                    {
                        Configuration = new PackageBuildConfiguration
                        {
                            Name = "Packages",
                            RootPath = "Sources",
                            BuildBeforeModule = true
                        }
                    },
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Packed,
                        Configuration = new ArtefactConfiguration
                        {
                            Enabled = true,
                            Path = releaseOutput,
                            ArtefactName = "TestModule.1.0.0.zip"
                        }
                    }
                }
            };

            var result = runner.Run(spec);

            Assert.Equal("project-release-zip", File.ReadAllText(releaseZipPath));
            Assert.Contains(result.ArtefactResults, item => string.Equals(item.OutputPath, Path.Combine(releaseOutput, "TestModule.1.0.0.zip"), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }
}
