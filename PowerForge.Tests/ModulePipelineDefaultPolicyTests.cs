using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePipelineDefaultPolicyTests
{
    [Fact]
    public void Plan_DefaultsInstallStrategyToExact()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            WriteMinimalModule(root.FullName, "DefaultPolicyModule");
            var plan = new ModulePipelineRunner(new NullLogger()).Plan(CreateSpec(root.FullName));

            Assert.Equal(InstallationStrategy.Exact, plan.InstallStrategy);
            Assert.Equal(InstallationStrategy.Exact, new ModuleBuilder.Options().Strategy);
            Assert.Equal(InstallationStrategy.Exact, new ModuleBuildProfileRequest().VersionedInstallStrategy);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_KeepsAutoRevisionAsExplicitOptIn()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            WriteMinimalModule(root.FullName, "DefaultPolicyModule");
            var spec = CreateSpec(root.FullName);
            spec.Install = new ModulePipelineInstallOptions { Enabled = false, Strategy = InstallationStrategy.AutoRevision };

            var plan = new ModulePipelineRunner(new NullLogger()).Plan(spec);
            Assert.Equal(InstallationStrategy.AutoRevision, plan.InstallStrategy);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_AutoSwitchExactOnPublishIsAnExplicitPublishOnlyOverride()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            WriteMinimalModule(root.FullName, "DefaultPolicyModule");
            var spec = CreateSpec(root.FullName);
            spec.Install = new ModulePipelineInstallOptions { Enabled = false, Strategy = InstallationStrategy.AutoRevision };
            spec.Segments = new IConfigurationSegment[]
            {
                new ConfigurationGateSegment { Configuration = new GateConfiguration { Mode = ConfigurationGateMode.Publish } },
                new ConfigurationBuildSegment
                {
                    BuildModule = new BuildModuleConfiguration { AutoSwitchExactOnPublish = true }
                }
            };

            var plan = new ModulePipelineRunner(new NullLogger()).Plan(spec);
            Assert.Equal(InstallationStrategy.Exact, plan.InstallStrategy);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_ApprovedModuleWithoutItsOwnConstraintInheritsRequiredMinimumAndSource()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            WriteMinimalModule(root.FullName, "DefaultPolicyModule");
            var spec = CreateSpec(root.FullName);
            spec.Segments = new IConfigurationSegment[]
            {
                new ConfigurationModuleSegment
                {
                    Kind = ModuleDependencyKind.RequiredModule,
                    Configuration = new ModuleDependencyConfiguration
                    {
                        ModuleName = "PSEventViewer",
                        MinimumVersion = "1.0.22",
                        VersionSource = ModuleDependencyVersionSource.Installed
                    }
                },
                new ConfigurationModuleSegment
                {
                    Kind = ModuleDependencyKind.ApprovedModule,
                    Configuration = new ModuleDependencyConfiguration { ModuleName = "PSEventViewer" }
                }
            };
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ModulePipelineMissingAnalysisServiceTests.ThrowingPowerShellRunner(),
                new ModulePipelineMissingAnalysisServiceTests.FakeDependencyMetadataProvider());

            var plan = runner.Plan(spec);
            var manifestDependency = Assert.Single(plan.RequiredModules);
            var donor = Assert.Single(plan.ApprovedModuleResolutions);
            Assert.Equal("1.0.22", manifestDependency.ModuleVersion);
            Assert.Equal("1.0.22", donor.Constraint.ModuleVersion);
            Assert.Equal(ModuleDependencyVersionSource.Installed, donor.VersionSource);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_ApprovedModuleInheritsRequiredConstraintWithoutLosingExplicitSource()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            WriteMinimalModule(root.FullName, "DefaultPolicyModule");
            var spec = CreateSpec(root.FullName);
            spec.Segments = new IConfigurationSegment[]
            {
                new ConfigurationModuleSegment
                {
                    Kind = ModuleDependencyKind.RequiredModule,
                    Configuration = new ModuleDependencyConfiguration
                    {
                        ModuleName = "PSEventViewer",
                        MinimumVersion = "1.0.22",
                        VersionSource = ModuleDependencyVersionSource.Installed
                    }
                },
                new ConfigurationModuleSegment
                {
                    Kind = ModuleDependencyKind.ApprovedModule,
                    Configuration = new ModuleDependencyConfiguration
                    {
                        ModuleName = "PSEventViewer",
                        VersionSource = ModuleDependencyVersionSource.PSGallery
                    }
                }
            };
            var provider = new ModulePipelineMissingAnalysisServiceTests.FakeDependencyMetadataProvider();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ModulePipelineMissingAnalysisServiceTests.ThrowingPowerShellRunner(),
                provider);

            var donor = Assert.Single(runner.Plan(spec).ApprovedModuleResolutions);

            Assert.Equal("1.0.22", donor.Constraint.ModuleVersion);
            Assert.Equal(ModuleDependencyVersionSource.PSGallery, donor.VersionSource);
            Assert.Equal("PSGallery", donor.Repository);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_AutoApprovedModuleKeepsConfiguredFallbackRepository()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            WriteMinimalModule(root.FullName, "DefaultPolicyModule");
            var spec = CreateSpec(root.FullName);
            spec.Segments = new IConfigurationSegment[]
            {
                new ConfigurationBuildSegment
                {
                    BuildModule = new BuildModuleConfiguration
                    {
                        InstallMissingModulesRepository = "InternalModules"
                    }
                },
                new ConfigurationModuleSegment
                {
                    Kind = ModuleDependencyKind.ApprovedModule,
                    Configuration = new ModuleDependencyConfiguration
                    {
                        ModuleName = "Internal.Donor",
                        VersionSource = ModuleDependencyVersionSource.Auto
                    }
                }
            };
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ModulePipelineMissingAnalysisServiceTests.ThrowingPowerShellRunner(),
                new ModulePipelineMissingAnalysisServiceTests.FakeDependencyMetadataProvider());

            var donor = Assert.Single(runner.Plan(spec).ApprovedModuleResolutions);
            Assert.Equal(ModuleDependencyVersionSource.Auto, donor.VersionSource);
            Assert.Equal("InternalModules", donor.Repository);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void RepositoryApprovedModuleSelectionEnforcesGuidConstraint()
    {
        const string expectedGuid = "11111111-1111-1111-1111-111111111111";
        var constraint = new RequiredModuleReference(
            "Approved.Donor",
            requiredVersion: "2.0.0",
            guid: expectedGuid);
        var wrongIdentity = new PSResourceInfo(
            "Approved.Donor",
            "2.0.0",
            "PSGallery",
            author: null,
            description: null,
            guid: "22222222-2222-2222-2222-222222222222");
        var expectedIdentity = new PSResourceInfo(
            "Approved.Donor",
            "2.0.0",
            "PSGallery",
            author: null,
            description: null,
            guid: expectedGuid);

        var selected = ModulePipelineRunner.SelectApprovedModuleRepositoryCandidate(
            constraint,
            new[] { wrongIdentity, expectedIdentity });

        Assert.Same(expectedIdentity, selected);
        Assert.Null(ModulePipelineRunner.SelectApprovedModuleRepositoryCandidate(
            constraint,
            new[] { wrongIdentity }));
    }

    [Fact]
    public void Plan_CarriesDefaultInstalledSourceForExternalModules()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            WriteMinimalModule(root.FullName, "DefaultPolicyModule");
            var spec = CreateSpec(root.FullName);
            spec.Segments = new IConfigurationSegment[]
            {
                new ConfigurationModuleSegment
                {
                    Kind = ModuleDependencyKind.ExternalModule,
                    Configuration = new ModuleDependencyConfiguration { ModuleName = "Az.Accounts" }
                }
            };
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ModulePipelineMissingAnalysisServiceTests.ThrowingPowerShellRunner(),
                new ModulePipelineMissingAnalysisServiceTests.FakeDependencyMetadataProvider());

            var source = Assert.Single(runner.Plan(spec).DependencySourceResolutions);
            Assert.Equal("Az.Accounts", source.Name);
            Assert.Equal(ModuleDependencyVersionSource.Installed, source.VersionSource);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_AutoApprovedModuleDoesNotBorrowCredentialFromAnotherRepository()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            WriteMinimalModule(root.FullName, "DefaultPolicyModule");
            var fallbackCredential = new RepositoryCredential { UserName = "internal", Secret = "token" };
            var spec = CreateSpec(root.FullName);
            spec.Segments = new IConfigurationSegment[]
            {
                new ConfigurationBuildSegment
                {
                    BuildModule = new BuildModuleConfiguration
                    {
                        InstallMissingModulesRepository = "InternalFeed",
                        InstallMissingModulesCredential = fallbackCredential
                    }
                },
                new ConfigurationPublishSegment
                {
                    Configuration = new PublishConfiguration
                    {
                        Destination = PublishDestination.PowerShellGallery,
                        Enabled = true,
                        RepositoryName = "AnonymousMirror",
                        UseAsDependencyVersionSource = true
                    }
                },
                new ConfigurationModuleSegment
                {
                    Kind = ModuleDependencyKind.ApprovedModule,
                    Configuration = new ModuleDependencyConfiguration
                    {
                        ModuleName = "Public.Donor",
                        VersionSource = ModuleDependencyVersionSource.Auto
                    }
                }
            };
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ModulePipelineMissingAnalysisServiceTests.ThrowingPowerShellRunner(),
                new ModulePipelineMissingAnalysisServiceTests.FakeDependencyMetadataProvider());

            var donor = Assert.Single(runner.Plan(spec).ApprovedModuleResolutions);
            Assert.Equal("AnonymousMirror", donor.Repository);
            Assert.Null(donor.Credential);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static ModulePipelineSpec CreateSpec(string root)
        => new()
        {
            Build = new ModuleBuildSpec
            {
                Name = "DefaultPolicyModule",
                SourcePath = root,
                Version = "1.0.0",
                CsprojPath = null,
                KeepStaging = true
            },
            Install = new ModulePipelineInstallOptions { Enabled = false },
            Segments = Array.Empty<IConfigurationSegment>()
        };

    private static void WriteMinimalModule(string root, string moduleName)
    {
        File.WriteAllText(Path.Combine(root, moduleName + ".psm1"), string.Empty);
        File.WriteAllText(
            Path.Combine(root, moduleName + ".psd1"),
            $"@{{ RootModule = '{moduleName}.psm1'; ModuleVersion = '1.0.0'; FunctionsToExport = @(); CmdletsToExport = @(); AliasesToExport = @() }}");
    }
}
