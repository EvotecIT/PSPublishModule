using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePipelineSourceDonorManifestTests
{
    [Fact]
    public void SourceRefreshKeepsExplicitRuntimeConstraintWhenDonorConstraintDiffers()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var manifestPath = Path.Combine(root.FullName, "SourceConsumer.psd1");
            File.WriteAllText(manifestPath, "@{ RootModule = 'SourceConsumer.psm1'; ModuleVersion = '1.0.0'; RequiredModules = @('Source.Donor') }");
            File.WriteAllText(Path.Combine(root.FullName, "SourceConsumer.psm1"), "# source loader");
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec { Name = "SourceConsumer", SourcePath = root.FullName, Version = "1.0.1" },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.RequiredModule,
                        Configuration = new ModuleDependencyConfiguration { ModuleName = "Source.Donor", ModuleVersion = "1.5.0" }
                    },
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.ApprovedModule,
                        Configuration = new ModuleDependencyConfiguration { ModuleName = "source.donor", ModuleVersion = "2.0.0" }
                    }
                }
            };
            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);

            runner.SyncBuildManifestToProjectRoot(plan);

            Assert.True(ManifestEditor.TryGetRequiredModules(manifestPath, out RequiredModuleReference[]? sourceModules));
            Assert.Equal("1.5.0", Assert.Single(sourceModules!).ModuleVersion);
            Assert.Equal("1.5.0", Assert.Single(plan.RequiredModules).ModuleVersion);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void SourceRefreshPreservesDeclaredDonorsWithoutChangingReleaseDependencies()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "SourceConsumer";
            var manifestPath = Path.Combine(root.FullName, moduleName + ".psd1");
            const string donorGuid = "11111111-1111-1111-1111-111111111111";
            File.WriteAllText(manifestPath, $"@{{ RootModule = 'SourceConsumer.psm1'; ModuleVersion = '1.0.0'; RequiredModules = @(@{{ ModuleName = 'Source.Donor'; GUID = '{donorGuid}' }}, 'Stale.Dependency') }}");
            File.WriteAllText(Path.Combine(root.FullName, moduleName + ".psm1"), ". $PSScriptRoot/Public/Get-SourceValue.ps1");
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec { Name = moduleName, SourcePath = root.FullName, Version = "1.0.1" },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.ApprovedModule,
                        Configuration = new ModuleDependencyConfiguration { ModuleName = "source.donor", RequiredVersion = "2.0.0" }
                    },
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.ApprovedModule,
                        Configuration = new ModuleDependencyConfiguration { ModuleName = "Unused.Donor" }
                    }
                }
            };
            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);

            runner.SyncBuildManifestToProjectRoot(plan);
            runner.SyncBuildManifestToProjectRoot(plan);

            Assert.True(ManifestEditor.TryGetRequiredModules(manifestPath, out RequiredModuleReference[]? sourceModules));
            var donor = Assert.Single(sourceModules!);
            Assert.Equal(donorGuid, donor.Guid);
            Assert.Equal("Source.Donor", donor.ModuleName);
            Assert.Equal("2.0.0", donor.RequiredVersion);
            Assert.Empty(plan.RequiredModules);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
