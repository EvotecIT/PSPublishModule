using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePipelineSourceDonorManifestTests
{
    [Fact]
    public void SourceRefreshPreservesDeclaredDonorsWithoutChangingReleaseDependencies()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "SourceConsumer";
            var manifestPath = Path.Combine(root.FullName, moduleName + ".psd1");
            File.WriteAllText(manifestPath, "@{ RootModule = 'SourceConsumer.psm1'; ModuleVersion = '1.0.0'; RequiredModules = @('Source.Donor', 'Stale.Dependency') }");
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
            Assert.Equal("source.donor", donor.ModuleName, ignoreCase: true);
            Assert.Equal("2.0.0", donor.RequiredVersion);
            Assert.Empty(plan.RequiredModules);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
