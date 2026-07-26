namespace PowerForge.Tests;

public sealed class ModulePipelinePublishVersionAvailabilityTests
{
    [Fact]
    public void Plan_PublishGateVerifiesRepositoryAvailabilityForLocalVersion()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            Guid.NewGuid().ToString("N")));

        try
        {
            const string moduleName = "AvailabilityModule";
            File.WriteAllText(
                Path.Combine(root.FullName, moduleName + ".psd1"),
                "@{ ModuleVersion = '1.0.0'; RootModule = 'AvailabilityModule.psm1' }");
            File.WriteAllText(
                Path.Combine(root.FullName, moduleName + ".psm1"),
                string.Empty);

            bool? capturedVerification = null;
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: null,
                manifestMutator: null,
                missingFunctionAnalysisService: null,
                scriptFunctionExportDetector: null,
                packageBuildExecutor: null,
                gitHubReleasePublisher: null,
                moduleVersionStepResolver: (expectedVersion, _, _, _, verifyRepositoryAvailability) =>
                {
                    capturedVerification = verifyRepositoryAvailability;
                    return new ModuleVersionStepResult(
                        expectedVersion,
                        "1.0.2",
                        "1.0.1",
                        ModuleVersionSource.Repository,
                        usedAutoVersioning: true);
                });

            var plan = runner.Plan(new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.X"
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationGateSegment
                    {
                        Configuration = new GateConfiguration
                        {
                            Mode = ConfigurationGateMode.Publish
                        }
                    },
                    new ConfigurationBuildSegment
                    {
                        BuildModule = new BuildModuleConfiguration
                        {
                            LocalVersion = true
                        }
                    }
                }
            });

            Assert.True(capturedVerification);
            Assert.Equal("1.0.2", plan.ResolvedVersion);
        }
        finally
        {
            try
            {
                root.Delete(recursive: true);
            }
            catch
            {
                // Best-effort cleanup for transient file handles.
            }
        }
    }

    [Fact]
    public void Plan_BuildGateKeepsLocalVersionResolutionOffline()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            Guid.NewGuid().ToString("N")));

        try
        {
            const string moduleName = "OfflineModule";
            File.WriteAllText(
                Path.Combine(root.FullName, moduleName + ".psd1"),
                "@{ ModuleVersion = '1.0.0'; RootModule = 'OfflineModule.psm1' }");
            File.WriteAllText(
                Path.Combine(root.FullName, moduleName + ".psm1"),
                string.Empty);

            bool? capturedVerification = null;
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: null,
                manifestMutator: null,
                missingFunctionAnalysisService: null,
                scriptFunctionExportDetector: null,
                packageBuildExecutor: null,
                gitHubReleasePublisher: null,
                moduleVersionStepResolver: (expectedVersion, _, _, _, verifyRepositoryAvailability) =>
                {
                    capturedVerification = verifyRepositoryAvailability;
                    return new ModuleVersionStepResult(
                        expectedVersion,
                        "1.0.1",
                        "1.0.0",
                        ModuleVersionSource.LocalPsd1,
                        usedAutoVersioning: true);
                });

            var plan = runner.Plan(new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.X"
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationGateSegment
                    {
                        Configuration = new GateConfiguration
                        {
                            Mode = ConfigurationGateMode.Build
                        }
                    },
                    new ConfigurationBuildSegment
                    {
                        BuildModule = new BuildModuleConfiguration
                        {
                            LocalVersion = true
                        }
                    }
                }
            });

            Assert.False(capturedVerification);
            Assert.Equal("1.0.1", plan.ResolvedVersion);
        }
        finally
        {
            try
            {
                root.Delete(recursive: true);
            }
            catch
            {
                // Best-effort cleanup for transient file handles.
            }
        }
    }
}
