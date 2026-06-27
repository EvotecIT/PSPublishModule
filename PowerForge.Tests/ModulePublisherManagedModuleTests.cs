using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePublisherManagedModuleTests
{
    [Fact]
    public void Publish_ManagedModule_UsesManagedEngineWithoutPowerShellRunner()
    {
        var stagingRoot = Path.Combine(Path.GetTempPath(), "PowerForgeTests", Guid.NewGuid().ToString("N"));
        using var feed = new TemporaryDirectory();
        try
        {
            Directory.CreateDirectory(stagingRoot);
            var manifestPath = Path.Combine(stagingRoot, "PSPublishModule.psd1");
            File.WriteAllText(
                manifestPath,
                """
                @{
                    ModuleVersion = '3.0.13'
                    GUID = 'eb76426a-1992-40a5-82cd-6480f883ef4d'
                    RootModule = 'PSPublishModule.psm1'
                    Author = 'Evotec'
                    Description = 'PowerForge publish test module.'
                    PrivateData = @{ PSData = @{ Tags = @('powerforge') } }
                }
                """);
            File.WriteAllText(Path.Combine(stagingRoot, "PSPublishModule.psm1"), string.Empty);

            var publisher = new ModulePublisher(
                new NullLogger(),
                new StubPowerShellRunner(_ => throw new InvalidOperationException("PowerShell runner should not be used by managed publish.")));

            var publish = new PublishConfiguration
            {
                Destination = PublishDestination.PowerShellGallery,
                Enabled = true,
                Tool = PublishTool.ManagedModule,
                RepositoryName = "Local",
                Repository = new PublishRepositoryConfiguration
                {
                    Name = "Local",
                    Uri = feed.Path
                }
            };
            var buildResult = new ModuleBuildResult(
                stagingPath: stagingRoot,
                manifestPath: manifestPath,
                exports: new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));

            var result = publisher.Publish(publish, CreatePlan(), buildResult, Array.Empty<ArtefactBuildResult>());

            Assert.True(result.Succeeded);
            Assert.Equal("Local", result.RepositoryName);
            Assert.Equal("3.0.13", result.VersionText);
            Assert.True(File.Exists(Path.Combine(feed.Path, "PSPublishModule.3.0.13.nupkg")));
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
                Directory.Delete(stagingRoot, recursive: true);
        }
    }

    [Fact]
    public void Publish_ManagedModule_MirrorsRequiredModulesThroughManagedEngine()
    {
        var stagingRoot = Path.Combine(Path.GetTempPath(), "PowerForgeTests", Guid.NewGuid().ToString("N"));
        using var sourceFeed = new TemporaryDirectory();
        using var targetFeed = new TemporaryDirectory();
        try
        {
            TestPackageFactory.Create(
                Path.Combine(sourceFeed.Path, "DependencySupport.1.0.0.nupkg"),
                "DependencySupport",
                "1.0.0");
            TestPackageFactory.Create(
                Path.Combine(sourceFeed.Path, "DependencyModule.1.2.3.nupkg"),
                "DependencyModule",
                "1.2.3",
                new[] { new TestDependency("DependencySupport", "[1.0.0]", null) });

            Directory.CreateDirectory(stagingRoot);
            var manifestPath = Path.Combine(stagingRoot, "PSPublishModule.psd1");
            File.WriteAllText(
                manifestPath,
                """
                @{
                    ModuleVersion = '3.0.13'
                    GUID = 'eb76426a-1992-40a5-82cd-6480f883ef4d'
                    RootModule = 'PSPublishModule.psm1'
                    Author = 'Evotec'
                    Description = 'PowerForge publish test module.'
                    RequiredModules = @(
                        @{ ModuleName = 'DependencyModule'; ModuleVersion = '1.0.0'; MaximumVersion = '1.5.0' }
                    )
                }
                """);
            File.WriteAllText(Path.Combine(stagingRoot, "PSPublishModule.psm1"), string.Empty);

            var publisher = new ModulePublisher(
                new NullLogger(),
                new StubPowerShellRunner(_ => throw new InvalidOperationException("PowerShell runner should not be used by managed publish.")));
            var publish = new PublishConfiguration
            {
                Destination = PublishDestination.PowerShellGallery,
                Enabled = true,
                Tool = PublishTool.ManagedModule,
                RepositoryName = "Local",
                PublishRequiredModules = true,
                RequiredModuleSourceRepository = sourceFeed.Path,
                Repository = new PublishRepositoryConfiguration
                {
                    Name = "Local",
                    Uri = targetFeed.Path
                }
            };
            var buildResult = new ModuleBuildResult(
                stagingPath: stagingRoot,
                manifestPath: manifestPath,
                exports: new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));

            var result = publisher.Publish(publish, CreatePlan(), buildResult, Array.Empty<ArtefactBuildResult>());

            Assert.True(result.Succeeded);
            Assert.True(File.Exists(Path.Combine(targetFeed.Path, "dependencysupport.1.0.0.nupkg")));
            Assert.True(File.Exists(Path.Combine(targetFeed.Path, "dependencymodule.1.2.3.nupkg")));
            Assert.True(File.Exists(Path.Combine(targetFeed.Path, "PSPublishModule.3.0.13.nupkg")));
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
                Directory.Delete(stagingRoot, recursive: true);
        }
    }

    private static ModulePipelinePlan CreatePlan()
    {
        return new ModulePipelinePlan(
            moduleName: "PSPublishModule",
            projectRoot: @"C:\repo\PSPublishModule",
            expectedVersion: "3.0.13",
            resolvedVersion: "3.0.13",
            preRelease: null,
            manifest: null,
            buildSpec: new ModuleBuildSpec
            {
                Name = "PSPublishModule",
                SourcePath = @"C:\repo\PSPublishModule",
                Version = "3.0.13"
            },
            resolvedCsprojPath: null,
            syncNETProjectVersion: false,
            compatiblePSEditions: Array.Empty<string>(),
            requiredModules: Array.Empty<RequiredModuleReference>(),
            externalModuleDependencies: Array.Empty<string>(),
            requiredModulesForPackaging: Array.Empty<RequiredModuleReference>(),
            information: null,
            documentation: null,
            delivery: null,
            documentationBuild: null,
            compatibilitySettings: null,
            fileConsistencySettings: null,
            validationSettings: null,
            formatting: null,
            importModules: null,
            placeHolders: Array.Empty<PlaceHolderReplacement>(),
            placeHolderOption: null,
            commandModuleDependencies: new Dictionary<string, string[]>(),
            testsAfterMerge: Array.Empty<TestConfiguration>(),
            actions: Array.Empty<ConfigurationActionSegment>(),
            mergeModule: false,
            mergeMissing: false,
            doNotAttemptToFixRelativePaths: false,
            approvedModules: Array.Empty<string>(),
            moduleSkip: null,
            signModule: false,
            signing: null,
            publishes: Array.Empty<ConfigurationPublishSegment>(),
            artefacts: Array.Empty<ConfigurationArtefactSegment>(),
            installEnabled: false,
            installStrategy: InstallationStrategy.AutoRevision,
            installKeepVersions: 3,
            installRoots: Array.Empty<string>(),
            installLegacyFlatHandling: LegacyFlatModuleHandling.Warn,
            installPreserveVersions: Array.Empty<string>(),
            installMissingModules: false,
            installMissingModulesForce: false,
            installMissingModulesPrerelease: false,
            installMissingModulesRepository: null,
            installMissingModulesCredential: null,
            stagingWasGenerated: true,
            deleteGeneratedStagingAfterRun: true);
    }

    private sealed class StubPowerShellRunner : IPowerShellRunner
    {
        private readonly Func<PowerShellRunRequest, PowerShellRunResult> _run;

        public StubPowerShellRunner(Func<PowerShellRunRequest, PowerShellRunResult> run)
        {
            _run = run;
        }

        public PowerShellRunResult Run(PowerShellRunRequest request)
            => _run(request);
    }
}
