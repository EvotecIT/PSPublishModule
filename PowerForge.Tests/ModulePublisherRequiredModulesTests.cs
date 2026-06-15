using System;
using System.Collections.Generic;
using System.IO;
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

    private static void WriteMinimalModule(string moduleRoot, string moduleName, string version)
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
            "}"
        }) + Environment.NewLine;

        File.WriteAllText(Path.Combine(moduleRoot, $"{moduleName}.psd1"), psd1);
    }
}
