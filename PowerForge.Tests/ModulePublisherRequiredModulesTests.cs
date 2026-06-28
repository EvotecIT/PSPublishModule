using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePublisherRequiredModulesTests
{
    [Fact]
    public void DoesVersionMatchRequiredModule_ExactRequiredVersion()
    {
        var required = new RequiredModuleReference(
            moduleName: "PSSharedGoods",
            requiredVersion: "0.0.312");

        Assert.True(ModulePublisher.DoesVersionMatchRequiredModule(required, "0.0.312"));
        Assert.False(ModulePublisher.DoesVersionMatchRequiredModule(required, "0.0.313"));
    }

    [Fact]
    public void DoesVersionMatchRequiredModule_MinimumVersion()
    {
        var required = new RequiredModuleReference(
            moduleName: "PSSharedGoods",
            moduleVersion: "0.0.312");

        Assert.True(ModulePublisher.DoesVersionMatchRequiredModule(required, "0.0.312"));
        Assert.True(ModulePublisher.DoesVersionMatchRequiredModule(required, "0.0.400"));
        Assert.False(ModulePublisher.DoesVersionMatchRequiredModule(required, "0.0.311"));
    }

    [Fact]
    public void DoesVersionMatchRequiredModule_RangeVersion()
    {
        var required = new RequiredModuleReference(
            moduleName: "PSSharedGoods",
            moduleVersion: "0.0.300",
            maximumVersion: "0.0.350");

        Assert.True(ModulePublisher.DoesVersionMatchRequiredModule(required, "0.0.300"));
        Assert.True(ModulePublisher.DoesVersionMatchRequiredModule(required, "0.0.350"));
        Assert.False(ModulePublisher.DoesVersionMatchRequiredModule(required, "0.0.299"));
        Assert.False(ModulePublisher.DoesVersionMatchRequiredModule(required, "0.0.351"));
    }

    [Fact]
    public void HasMatchingRequiredModuleVersion_ReturnsTrueWhenAnyVersionMatches()
    {
        var required = new RequiredModuleReference(
            moduleName: "PSSharedGoods",
            moduleVersion: "0.0.312");

        var result = ModulePublisher.HasMatchingRequiredModuleVersion(
            required,
            new[] { "0.0.200", "0.0.312", "0.0.313" });

        Assert.True(result);
    }

    [Fact]
    public void ShouldSkipRepositoryDependencyValidation_WhenRequiredModuleIsExternal()
    {
        var required = new RequiredModuleReference(
            moduleName: "Microsoft.PowerShell.Utility");

        var external = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "microsoft.powershell.utility",
            "Microsoft.PowerShell.Management"
        };

        var shouldSkip = ModulePublisher.ShouldSkipRepositoryDependencyValidation(required, external);

        Assert.True(shouldSkip);
    }

    [Fact]
    public void ShouldSkipRepositoryDependencyValidation_WhenRequiredModuleIsNotExternal()
    {
        var required = new RequiredModuleReference(
            moduleName: "PSSharedGoods");

        var external = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft.PowerShell.Utility",
            "Microsoft.PowerShell.Management"
        };

        var shouldSkip = ModulePublisher.ShouldSkipRepositoryDependencyValidation(required, external);

        Assert.False(shouldSkip);
    }

    [Fact]
    public void GetExternalModulesForPublish_ReadsManifestPsDataBeforePlanFallback()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(
                root.FullName,
                moduleName,
                "1.0.0",
                "            ExternalModuleDependencies = @('external.dependency', 'External.Dependency')");
            var buildResult = new ModuleBuildResult(
                stagingPath: root.FullName,
                manifestPath: Path.Combine(root.FullName, $"{moduleName}.psd1"),
                exports: new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));
            var plan = CreatePlan(externalModuleDependencies: new[] { "Plan.Only" });

            var externalModules = RequiredModuleRepositoryValidator.GetExternalModulesForPublish(buildResult, plan);

            Assert.Contains("external.dependency", externalModules, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("Plan.Only", externalModules, StringComparer.OrdinalIgnoreCase);
            Assert.Single(externalModules);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void SelectRequiredModuleVersionForPublish_PicksHighestMatchingVersion()
    {
        var required = new RequiredModuleReference(
            moduleName: "Microsoft.Graph.Authentication",
            moduleVersion: "2.0.0",
            maximumVersion: "2.10.0");

        var selected = RequiredModuleRepositoryPublisher.SelectRequiredModuleVersionForPublish(
            required,
            new[]
            {
                new PSResourceInfo("Microsoft.Graph.Authentication", "1.0.0", "PSGallery", null, null),
                new PSResourceInfo("Microsoft.Graph.Authentication", "2.2.0", "PSGallery", null, null),
                new PSResourceInfo("Microsoft.Graph.Authentication", "2.9.1", "PSGallery", null, null),
                new PSResourceInfo("Microsoft.Graph.Authentication", "2.11.0", "PSGallery", null, null)
            });

        Assert.NotNull(selected);
        Assert.Equal("2.9.1", selected!.Version);
    }

    [Fact]
    public void SelectRequiredModuleVersionForPublish_PrefersStableWhenPrereleaseIsNotRequired()
    {
        var required = new RequiredModuleReference(
            moduleName: "DependencyModule",
            moduleVersion: "1.0.0");

        var selected = RequiredModuleRepositoryPublisher.SelectRequiredModuleVersionForPublish(
            required,
            new[]
            {
                new PSResourceInfo("DependencyModule", "1.9.0", "PSGallery", null, null),
                new PSResourceInfo("DependencyModule", "2.0.0", "PSGallery", null, null, preRelease: "preview1")
            });

        Assert.NotNull(selected);
        Assert.Equal("1.9.0", ModulePublisher.GetRepositoryVersionText(selected!));
    }

    [Theory]
    [InlineData("1.2.3", null, null, "[1.2.3]")]
    [InlineData(null, "1.0.0", "1.5.0", "[1.0.0,1.5.0]")]
    [InlineData(null, "1.0.0", null, "[1.0.0,)")]
    [InlineData(null, null, "1.5.0", "(,1.5.0]")]
    public void BuildPSResourceGetVersionRange_UsesRequiredModuleConstraint(
        string? requiredVersion,
        string? moduleVersion,
        string? maximumVersion,
        string expected)
    {
        var required = new RequiredModuleReference(
            moduleName: "DependencyModule",
            requiredVersion: requiredVersion,
            moduleVersion: moduleVersion,
            maximumVersion: maximumVersion);

        var range = RequiredModuleRepositoryPublisher.BuildPSResourceGetVersionRange(required);

        Assert.Equal(expected, range);
    }

    [Fact]
    public void FindSavedModulePath_ReturnsVersionFolderContainingManifest()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var modulePath = Path.Combine(root.FullName, "Microsoft.Graph.Authentication", "2.9.1");
            Directory.CreateDirectory(modulePath);
            File.WriteAllText(Path.Combine(modulePath, "Microsoft.Graph.Authentication.psd1"), "@{ ModuleVersion = '2.9.1' }");

            var selected = RequiredModuleRepositoryPublisher.FindSavedModulePath(
                root.FullName,
                "Microsoft.Graph.Authentication",
                "2.9.1");

            Assert.Equal(modulePath, selected);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void FindSavedModulePackagesForPublish_ReturnsSavedDependenciesBeforePrimaryModule()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var dependencyPath = Path.Combine(root.FullName, "DependencySupport", "1.0.0");
            var primaryPath = Path.Combine(root.FullName, "DependencyModule", "1.2.3");
            Directory.CreateDirectory(dependencyPath);
            Directory.CreateDirectory(primaryPath);
            File.WriteAllText(Path.Combine(dependencyPath, "DependencySupport.psd1"), "@{ ModuleVersion = '1.0.0' }");
            File.WriteAllText(Path.Combine(primaryPath, "DependencyModule.psd1"), "@{ ModuleVersion = '1.2.3' }");

            var paths = RequiredModuleRepositoryPublisher.FindSavedModulePackagesForPublish(
                root.FullName,
                new[]
                {
                    new PSResourceInfo("DependencyModule", "1.2.3", "PSGallery", null, null),
                    new PSResourceInfo("DependencySupport", "1.0.0", "PSGallery", null, null)
                },
                "DependencyModule",
                "1.2.3");

            Assert.Equal(new[] { dependencyPath, primaryPath }, paths.Select(path => path.Path));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void GetRequiredModulesForPublish_UsesExistingManifestEvenWhenRequiredModulesAreEmpty()
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
                    Version = "1.0.0"
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildSegment
                    {
                        BuildModule = new BuildModuleConfiguration
                        {
                            MergeMissing = true
                        }
                    },
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.RequiredModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = "PSSharedGoods",
                            ModuleVersion = "0.0.313.1"
                        }
                    },
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.ApprovedModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = "PSSharedGoods"
                        }
                    }
                }
            };

            var plan = new ModulePipelineRunner(new NullLogger()).Plan(spec);
            Assert.Contains(plan.RequiredModules, module => string.Equals(module.ModuleName, "PSSharedGoods", StringComparison.OrdinalIgnoreCase));

            var buildResult = new ModuleBuildResult(
                stagingPath: root.FullName,
                manifestPath: Path.Combine(root.FullName, $"{moduleName}.psd1"),
                exports: new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));

            var requiredForPublish = ModulePublisher.GetRequiredModulesForPublish(buildResult, plan);

            Assert.Empty(requiredForPublish);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void GetRequiredModulesForPublish_FallsBackToPlanWhenManifestIsMissing()
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
                    Version = "1.0.0"
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.RequiredModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = "PSSharedGoods",
                            ModuleVersion = "0.0.313.1"
                        }
                    }
                }
            };

            var plan = new ModulePipelineRunner(new NullLogger()).Plan(spec);
            var buildResult = new ModuleBuildResult(
                stagingPath: root.FullName,
                manifestPath: Path.Combine(root.FullName, "Missing.psd1"),
                exports: new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));

            var requiredForPublish = ModulePublisher.GetRequiredModulesForPublish(buildResult, plan);

            var required = Assert.Single(requiredForPublish);
            Assert.Equal("PSSharedGoods", required.ModuleName);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static void WriteMinimalModule(
        string moduleRoot,
        string moduleName,
        string version,
        string? psDataLine = null)
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
            "    PrivateData = @{",
            "        PSData = @{",
            string.IsNullOrWhiteSpace(psDataLine) ? string.Empty : psDataLine!,
            "        }",
            "    }",
            "}"
        }.Where(static line => line.Length > 0)) + Environment.NewLine;

        File.WriteAllText(Path.Combine(moduleRoot, $"{moduleName}.psd1"), psd1);
    }

    private static ModulePipelinePlan CreatePlan(string[]? externalModuleDependencies = null)
    {
        return new ModulePipelinePlan(
            moduleName: "TestModule",
            projectRoot: @"C:\repo\TestModule",
            expectedVersion: "1.0.0",
            resolvedVersion: "1.0.0",
            preRelease: null,
            manifest: null,
            buildSpec: new ModuleBuildSpec
            {
                Name = "TestModule",
                SourcePath = @"C:\repo\TestModule",
                Version = "1.0.0"
            },
            resolvedCsprojPath: null,
            syncNETProjectVersion: false,
            compatiblePSEditions: Array.Empty<string>(),
            requiredModules: Array.Empty<RequiredModuleReference>(),
            externalModuleDependencies: externalModuleDependencies ?? Array.Empty<string>(),
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
}
