using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineUnifiedReleaseTests
{
    [Fact]
    public void Run_RevalidatesTokenizedDeliveryPathsAfterModuleVersionSynchronization()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            const string synchronizedVersion = "2.0.0";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            WriteProjectBuildConfig(root.FullName, Path.Combine("Build", "project.build.json"));
            var packageOutput = Path.Combine(root.FullName, "Artifacts", "NuGet");
            var packagePath = Path.Combine(packageOutput, $"{moduleName}.{synchronizedVersion}.nupkg");
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: null,
                manifestMutator: null,
                missingFunctionAnalysisService: null,
                scriptFunctionExportDetector: null,
                packageBuildExecutor: (request, configuration, configPath) =>
                    CreateProjectBuildResult(
                        root.FullName,
                        moduleName,
                        synchronizedVersion,
                        packageOutput,
                        request,
                        configPath));
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
                    new ConfigurationOptionsSegment
                    {
                        Options = new ConfigurationOptions
                        {
                            Delivery = new DeliveryOptionsConfiguration
                            {
                                Enable = true,
                                InternalsPath = Path.Combine("Payload", synchronizedVersion)
                            }
                        }
                    },
                    CreateProjectBuildSegment(moduleName, enabled: true, buildBeforeModule: true),
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Unpacked,
                        Configuration = new ArtefactConfiguration
                        {
                            Enabled = true,
                            Path = Path.Combine("Payload", "<ModuleVersion>")
                        }
                    },
                    CreateSynchronizedReleaseSegment(moduleName)
                }
            };

            var exception = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));

            Assert.Contains("Delivery configuration is unsafe", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("overlaps artefact output root", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(packagePath), "The synchronized path guard should stop before artefact creation.");
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void SyncSourceProjectVersionIfRequested_PreservesPrereleaseVersion()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            const string numericVersion = "2.0.11";
            const string preRelease = "beta.2";
            WriteMinimalModule(root.FullName, moduleName, numericVersion);
            var sourceProjectPath = Path.Combine(root.FullName, $"{moduleName}.csproj");
            File.WriteAllText(
                sourceProjectPath,
                "<Project><PropertyGroup><Version>2.0.10</Version></PropertyGroup></Project>");
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = numericVersion,
                    CsprojPath = sourceProjectPath
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildSegment
                    {
                        BuildModule = new BuildModuleConfiguration
                        {
                            SyncNETProjectVersion = true
                        }
                    },
                    new ConfigurationManifestSegment
                    {
                        Configuration = new ManifestConfiguration
                        {
                            ModuleVersion = numericVersion,
                            Prerelease = preRelease
                        }
                    }
                }
            };
            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);

            runner.SyncSourceProjectVersionIfRequested(plan);

            Assert.True(CsprojVersionEditor.TryGetVersion(sourceProjectPath, out var synchronizedVersion));
            Assert.Equal($"{numericVersion}-{preRelease}", synchronizedVersion);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_IgnoresDisabledSelectedLaneWhenValidatingSynchronizedOrdering()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var stagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "TestModule";
            const string synchronizedVersion = "2.0.13";
            WriteMinimalModule(root.FullName, moduleName, "2.0.10");
            WriteProjectBuildConfig(root.FullName, Path.Combine("Build", "project.build.json"));
            var packageOutput = Path.Combine(root.FullName, "Artifacts", "NuGet");
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: null,
                manifestMutator: null,
                missingFunctionAnalysisService: null,
                scriptFunctionExportDetector: null,
                packageBuildExecutor: (request, configuration, configPath) =>
                    CreateProjectBuildResult(
                        root.FullName,
                        moduleName,
                        synchronizedVersion,
                        packageOutput,
                        request,
                        configPath));
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
                    CreateProjectBuildSegment(moduleName, enabled: true, buildBeforeModule: true),
                    CreateProjectBuildSegment("Disabled", enabled: false, buildBeforeModule: false),
                    CreateSynchronizedReleaseSegment(moduleName)
                }
            };

            var result = runner.Run(spec);

            Assert.Equal(synchronizedVersion, result.Plan.ResolvedVersion);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(stagingPath)) Directory.Delete(stagingPath, recursive: true); } catch { }
        }
    }

    private static ConfigurationProjectBuildSegment CreateProjectBuildSegment(
        string name,
        bool enabled,
        bool buildBeforeModule)
        => new()
        {
            Configuration = new ProjectBuildConfigurationReference
            {
                Enabled = enabled,
                Name = name,
                ConfigPath = Path.Combine("Build", "project.build.json"),
                BuildBeforeModule = buildBeforeModule,
                UseAsReleaseVersionSource = true
            }
        };

    private static ConfigurationReleaseSegment CreateSynchronizedReleaseSegment(string primaryProject)
        => new()
        {
            Configuration = new ReleaseConfiguration
            {
                VersionSource = ReleaseVersionSource.ProjectBuild,
                PrimaryProject = primaryProject,
                SynchronizeModuleVersion = true
            }
        };

    private static ProjectBuildHostExecutionResult CreateProjectBuildResult(
        string rootPath,
        string projectName,
        string version,
        string packageOutput,
        ProjectBuildHostRequest request,
        string? configPath)
    {
        Directory.CreateDirectory(packageOutput);
        var packagePath = Path.Combine(packageOutput, $"{projectName}.{version}.nupkg");
        File.WriteAllText(packagePath, "package");
        var release = new DotNetRepositoryReleaseResult
        {
            Success = true,
            ResolvedVersion = version
        };
        release.ResolvedVersionsByProject[projectName] = version;
        var project = new DotNetRepositoryProjectResult
        {
            ProjectName = projectName,
            PackageId = projectName,
            IsPackable = true,
            NewVersion = version
        };
        project.Packages.Add(packagePath);
        release.Projects.Add(project);
        return new ProjectBuildHostExecutionResult
        {
            Success = true,
            ConfigPath = configPath ?? request.ConfigPath,
            RootPath = rootPath,
            OutputPath = packageOutput,
            Result = new ProjectBuildResult
            {
                Success = true,
                Release = release
            }
        };
    }
}
