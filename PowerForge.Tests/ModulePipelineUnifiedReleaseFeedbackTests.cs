using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineUnifiedReleaseTests
{
    [Fact]
    public void Run_ResumesExactCoordinatedVersionAfterPartialPublishFailure()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var firstStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        var secondStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "TestModule";
            const string synchronizedVersion = "2.0.11";
            WriteMinimalModule(root.FullName, moduleName, "2.0.10");
            var configPath = Path.Combine(root.FullName, "Build", "project.build.json");
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.WriteAllText(
                configPath,
                string.Join(Environment.NewLine, new[]
                {
                    "{",
                    "  \"RootPath\": \"Sources\",",
                    "  \"ExpectedVersionMap\": { \"TestModule\": \"2.0.X\" },",
                    "  \"ExpectedVersionMapAsInclude\": true,",
                    "  \"UpdateVersions\": true,",
                    "  \"Build\": true,",
                    "  \"PublishNuget\": true,",
                    "  \"PublishGitHub\": false",
                    "}"
                }));

            var packagePublishCount = 0;
            var resumedDependencyBuildUsedExactVersion = false;
            var runNumber = 1;
            ProjectBuildHostExecutionResult ExecutePackageBuild(
                ProjectBuildHostRequest request,
                ProjectBuildConfiguration? configuration,
                string? loadedConfigPath)
            {
                Assert.NotNull(configuration);
                if (request.PublishNuget == true)
                    packagePublishCount++;
                if (runNumber == 2 && request.PublishNuget != true)
                {
                    resumedDependencyBuildUsedExactVersion =
                        configuration!.ExpectedVersionMap is not null &&
                        configuration.ExpectedVersionMap.TryGetValue(moduleName, out var expected) &&
                        string.Equals(expected, synchronizedVersion, StringComparison.OrdinalIgnoreCase);
                }

                return CreateProjectBuildResult(
                    root.FullName,
                    moduleName,
                    synchronizedVersion,
                    Path.Combine(root.FullName, "Artifacts", "NuGet"),
                    request,
                    loadedConfigPath,
                    includePackage: false);
            }

            var firstHosted = new FakeHostedOperations(new List<string>())
            {
                ModulePublishAction = (_, _) => throw new InvalidOperationException("Simulated gallery outage.")
            };
            var firstRunner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: firstHosted,
                manifestMutator: null,
                missingFunctionAnalysisService: null,
                scriptFunctionExportDetector: null,
                packageBuildExecutor: ExecutePackageBuild);

            var firstException = Assert.Throws<InvalidOperationException>(() =>
                firstRunner.Run(CreateResumableReleaseSpec(root.FullName, firstStagingPath, moduleName)));

            Assert.Contains("gallery outage", firstException.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, packagePublishCount);
            var checkpointRoot = Path.Combine(root.FullName, "Artefacts", ".powerforge", "coordinated-release");
            Assert.Single(Directory.GetFiles(checkpointRoot, "*.json"));

            runNumber = 2;
            var secondHosted = new FakeHostedOperations(new List<string>());
            var secondRunner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: secondHosted,
                manifestMutator: null,
                missingFunctionAnalysisService: null,
                scriptFunctionExportDetector: null,
                packageBuildExecutor: ExecutePackageBuild);

            var result = secondRunner.Run(CreateResumableReleaseSpec(root.FullName, secondStagingPath, moduleName));

            Assert.True(resumedDependencyBuildUsedExactVersion);
            Assert.Equal(synchronizedVersion, result.Plan.ResolvedVersion);
            Assert.Equal(1, packagePublishCount);
            Assert.Equal(new[] { synchronizedVersion }, secondHosted.PublishedModuleVersions);
            Assert.False(Directory.Exists(checkpointRoot));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(firstStagingPath)) Directory.Delete(firstStagingPath, recursive: true); } catch { }
            try { if (Directory.Exists(secondStagingPath)) Directory.Delete(secondStagingPath, recursive: true); } catch { }
        }
    }

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

    private static ModulePipelineSpec CreateResumableReleaseSpec(
        string rootPath,
        string stagingPath,
        string moduleName)
        => new()
        {
            Build = new ModuleBuildSpec
            {
                Name = moduleName,
                SourcePath = rootPath,
                Version = "2.0.10",
                StagingPath = stagingPath
            },
            Install = new ModulePipelineInstallOptions { Enabled = false },
            Segments = new IConfigurationSegment[]
            {
                CreateProjectBuildSegment(moduleName, enabled: true, buildBeforeModule: true),
                new ConfigurationPublishSegment
                {
                    Configuration = new PublishConfiguration
                    {
                        Enabled = true,
                        Destination = PublishDestination.PowerShellGallery,
                        RepositoryName = "TestRepository"
                    }
                },
                new ConfigurationReleaseSegment
                {
                    Configuration = new ReleaseConfiguration
                    {
                        VersionSource = ReleaseVersionSource.ProjectBuild,
                        PrimaryProject = moduleName,
                        SynchronizeModuleVersion = true,
                        PublishOrder = new[] { "NuGet", "PowerShellGallery" }
                    }
                }
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
        string? configPath,
        bool includePackage = true)
    {
        Directory.CreateDirectory(packageOutput);
        var packagePath = Path.Combine(packageOutput, $"{projectName}.{version}.nupkg");
        if (includePackage)
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
        if (includePackage)
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
