using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePipelineDependencyMetadataProviderTests
{
    [Fact]
    public void Plan_UsesInjectedDependencyMetadataProvider_ForAutoGuidResolution()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            const string dependencyName = "PSSharedGoods";
            const string dependencyGuid = "11111111-2222-3333-4444-555555555555";

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
                            ModuleName = dependencyName,
                            ModuleVersion = "0.25.0",
                            Guid = "Auto"
                        }
                    }
                }
            };

            var provider = new FakeModuleDependencyMetadataProvider(
                installedModules: new Dictionary<string, InstalledModuleMetadata>(StringComparer.OrdinalIgnoreCase),
                onlineModules: new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase)
                {
                    [dependencyName] = ("0.30.0", dependencyGuid)
                });

            var runner = new ModulePipelineRunner(new NullLogger(), new ThrowingPowerShellRunner(), provider);
            var plan = runner.Plan(spec);

            var required = Assert.Single(plan.RequiredModules);
            Assert.Equal(dependencyGuid, required.Guid);
            Assert.Equal(1, provider.InstalledLookups);
            Assert.Equal(1, provider.OnlineLookups);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_UsesPublishRepositoryAsDependencyVersionSource_WhenPublishOptInIsEnabled()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            const string dependencyName = "PSWriteHTML";

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
                            ModuleName = dependencyName,
                            ModuleVersion = "Latest"
                        }
                    },
                    new ConfigurationPublishSegment
                    {
                        Configuration = new PublishConfiguration
                        {
                            Destination = PublishDestination.PowerShellGallery,
                            Enabled = true,
                            RepositoryName = "PSGallery",
                            UseAsDependencyVersionSource = true
                        }
                    }
                }
            };

            var provider = new FakeModuleDependencyMetadataProvider(
                installedModules: new Dictionary<string, InstalledModuleMetadata>(StringComparer.OrdinalIgnoreCase)
                {
                    [dependencyName] = new(dependencyName, "1.41.0.5", null, @"C:\Modules\PSWriteHTML\1.41.0.5")
                },
                onlineModules: new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase)
                {
                    [dependencyName] = ("1.41.0", null)
                });

            var runner = new ModulePipelineRunner(new NullLogger(), new ThrowingPowerShellRunner(), provider);
            var plan = runner.Plan(spec);

            var required = Assert.Single(plan.RequiredModules);
            Assert.Equal(dependencyName, required.ModuleName);
            Assert.Equal("1.41.0", required.ModuleVersion);
            Assert.Equal("PSGallery", provider.LastOnlineRepository);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_HonorsInstalledVersionSource_WhenPublishOptInIsEnabled()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            const string dependencyName = "PSWriteHTML";

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
                            ModuleName = dependencyName,
                            ModuleVersion = "Latest",
                            VersionSource = ModuleDependencyVersionSource.Installed
                        }
                    },
                    new ConfigurationPublishSegment
                    {
                        Configuration = new PublishConfiguration
                        {
                            Destination = PublishDestination.PowerShellGallery,
                            Enabled = true,
                            RepositoryName = "PSGallery",
                            UseAsDependencyVersionSource = true
                        }
                    }
                }
            };

            var provider = new FakeModuleDependencyMetadataProvider(
                installedModules: new Dictionary<string, InstalledModuleMetadata>(StringComparer.OrdinalIgnoreCase)
                {
                    [dependencyName] = new(dependencyName, "1.41.0.5", null, @"C:\Modules\PSWriteHTML\1.41.0.5")
                },
                onlineModules: new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase)
                {
                    [dependencyName] = ("1.41.0", null)
                });

            var runner = new ModulePipelineRunner(new NullLogger(), new ThrowingPowerShellRunner(), provider);
            var plan = runner.Plan(spec);

            var required = Assert.Single(plan.RequiredModules);
            Assert.Equal("1.41.0.5", required.ModuleVersion);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_DoesNotResolveInstalledVersionSourceOnline_WhenInstalledModuleIsMissing()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            const string dependencyName = "PSWriteHTML";

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
                            ModuleName = dependencyName,
                            ModuleVersion = "Latest",
                            VersionSource = ModuleDependencyVersionSource.Installed
                        }
                    }
                }
            };

            var provider = new FakeModuleDependencyMetadataProvider(
                installedModules: new Dictionary<string, InstalledModuleMetadata>(StringComparer.OrdinalIgnoreCase),
                onlineModules: new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase)
                {
                    [dependencyName] = ("1.41.0", null)
                });

            var runner = new ModulePipelineRunner(new NullLogger(), new ThrowingPowerShellRunner(), provider);
            var plan = runner.Plan(spec);

            var required = Assert.Single(plan.RequiredModules);
            Assert.Null(required.ModuleVersion);
            Assert.Equal(0, provider.OnlineLookups);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_ThrowsForPublishRepositoryVersionSource_WhenNoPublishSourceIsConfigured()
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
                            ModuleName = "PSWriteHTML",
                            ModuleVersion = "Latest",
                            VersionSource = ModuleDependencyVersionSource.PublishRepository
                        }
                    }
                }
            };

            var provider = new FakeModuleDependencyMetadataProvider(
                installedModules: new Dictionary<string, InstalledModuleMetadata>(StringComparer.OrdinalIgnoreCase),
                onlineModules: new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase));

            var runner = new ModulePipelineRunner(new NullLogger(), new ThrowingPowerShellRunner(), provider);

            var ex = Assert.Throws<InvalidOperationException>(() => runner.Plan(spec));
            Assert.Contains("UseAsDependencyVersionSource", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_ReordersRequiredModules_ForBinaryConflictOrder_UsingInjectedDependencyMetadataProvider()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var alphaAssembly = BuildLibrary(root.FullName, "SharedAuth", "1.0.0", projectFolderName: "SharedAuth_Alpha");
            var betaAssembly = BuildLibrary(root.FullName, "SharedAuth", "2.0.0", projectFolderName: "SharedAuth_Beta");

            var alphaModuleBase = Directory.CreateDirectory(Path.Combine(root.FullName, "Alpha.Tools", "1.0.0", "bin"));
            File.Copy(alphaAssembly, Path.Combine(alphaModuleBase.FullName, "SharedAuth.dll"), overwrite: true);

            var betaModuleBase = Directory.CreateDirectory(Path.Combine(root.FullName, "Beta.Tools", "2.0.0", "bin"));
            File.Copy(betaAssembly, Path.Combine(betaModuleBase.FullName, "SharedAuth.dll"), overwrite: true);

            var provider = new FakeModuleDependencyMetadataProvider(
                installedModules: new Dictionary<string, InstalledModuleMetadata>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Alpha.Tools"] = new("Alpha.Tools", "1.0.0", null, alphaModuleBase.Parent!.FullName),
                    ["Beta.Tools"] = new("Beta.Tools", "2.0.0", null, betaModuleBase.Parent!.FullName)
                },
                onlineModules: new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase));

            var logger = new CollectingLogger();
            var runner = new ModulePipelineRunner(logger, new ThrowingPowerShellRunner(), provider);

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0",
                    CsprojPath = null
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.RequiredModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = "Alpha.Tools",
                            RequiredVersion = "1.0.0"
                        }
                    },
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.RequiredModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = "Beta.Tools",
                            RequiredVersion = "2.0.0"
                        }
                    },
                    new ConfigurationImportModulesSegment
                    {
                        ImportModules = new ImportModulesConfiguration
                        {
                            RequiredModules = true,
                            PreferBinaryConflictOrder = true
                        }
                    }
                }
            };

            var plan = runner.Plan(spec);

            Assert.Equal(new[] { "Beta.Tools", "Alpha.Tools" }, plan.RequiredModules.Select(static module => module.ModuleName).ToArray());
            Assert.Equal(2, provider.InstalledLookups);
            Assert.Contains(logger.Infos, static message => message.Contains("PreferBinaryConflictOrder reordered RequiredModules", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void MissingAnalysis_ResolvesDependentModules_UsingInjectedDependencyMetadataProvider()
    {
        var provider = new FakeModuleDependencyMetadataProvider(
            installedModules: new Dictionary<string, InstalledModuleMetadata>(StringComparer.OrdinalIgnoreCase),
            onlineModules: new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase),
            installedRequiredModules: new Dictionary<string, IReadOnlyList<RequiredModuleReference>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Alpha.Tools"] = new[] { new RequiredModuleReference("Beta.Tools"), new RequiredModuleReference("Gamma.Tools") },
                ["Beta.Tools"] = new[] { new RequiredModuleReference("Gamma.Tools"), new RequiredModuleReference("Delta.Tools") },
                ["Gamma.Tools"] = Array.Empty<RequiredModuleReference>(),
                ["Delta.Tools"] = Array.Empty<RequiredModuleReference>()
            });

        var runner = new ModulePipelineRunner(new NullLogger(), new ThrowingPowerShellRunner(), provider);
        var method = typeof(ModulePipelineRunner).GetMethod("ResolveDependentRequiredModules", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.True(method is not null, "ResolveDependentRequiredModules method signature may have changed.");

        var dependencies = (string[])method!.Invoke(
            runner,
            new object?[]
            {
                new[] { "Alpha.Tools" },
                Array.Empty<string>()
            })!;

        Assert.Equal(3, dependencies.Length);
        Assert.Contains("Beta.Tools", dependencies);
        Assert.Contains("Gamma.Tools", dependencies);
        Assert.Contains("Delta.Tools", dependencies);
        Assert.Equal(4, provider.RequiredModuleLookups);
    }

    [Fact]
    public void Run_PreservesTransitiveDependency_WhenApprovedModuleIsMergedAway()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var provider = new FakeModuleDependencyMetadataProvider(
                installedModules: new Dictionary<string, InstalledModuleMetadata>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Parent.Tools"] = new("Parent.Tools", "1.0.0", null, @"C:\Modules\Parent.Tools\1.0.0"),
                    ["Child.Tools"] = new("Child.Tools", "2.0.0", "22222222-2222-2222-2222-222222222222", @"C:\Modules\Child.Tools\2.0.0")
                },
                onlineModules: new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase),
                installedRequiredModules: new Dictionary<string, IReadOnlyList<RequiredModuleReference>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Parent.Tools"] = new[]
                    {
                        new RequiredModuleReference(
                            "Child.Tools",
                            moduleVersion: "2.0.0",
                            guid: "22222222-2222-2222-2222-222222222222")
                    }
                });

            var runner = new ModulePipelineRunner(new NullLogger(), new ThrowingPowerShellRunner(), provider);
            var result = runner.Run(CreateApprovedParentSpec(root.FullName, moduleName));

            Assert.True(ManifestEditor.TryGetRequiredModules(result.BuildResult.ManifestPath, out RequiredModuleReference[]? required));
            var requiredModules = required!;
            Assert.DoesNotContain(requiredModules, module => string.Equals(module.ModuleName, "Parent.Tools", StringComparison.OrdinalIgnoreCase));
            var child = Assert.Single(requiredModules, module => string.Equals(module.ModuleName, "Child.Tools", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("2.0.0", child.ModuleVersion);
            Assert.Equal("22222222-2222-2222-2222-222222222222", child.Guid);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Run_IgnoresRuntimeProvidedTransitiveDependencies_WhenApprovedModuleIsMergedAway()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var provider = new FakeModuleDependencyMetadataProvider(
                installedModules: new Dictionary<string, InstalledModuleMetadata>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Parent.Tools"] = new("Parent.Tools", "1.0.0", null, @"C:\Modules\Parent.Tools\1.0.0")
                },
                onlineModules: new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase),
                installedRequiredModules: new Dictionary<string, IReadOnlyList<RequiredModuleReference>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Parent.Tools"] = new[]
                    {
                        new RequiredModuleReference("PackageManagement", moduleVersion: "1.0.0.1"),
                        new RequiredModuleReference("PowerShellGet", moduleVersion: "1.0.0.1"),
                        new RequiredModuleReference("PSReadLine", moduleVersion: "2.0.0")
                    }
                });

            var runner = new ModulePipelineRunner(new NullLogger(), new ThrowingPowerShellRunner(), provider);
            var result = runner.Run(CreateApprovedParentSpec(root.FullName, moduleName));

            Assert.True(ManifestEditor.TryGetRequiredModules(result.BuildResult.ManifestPath, out RequiredModuleReference[]? required));
            var requiredModules = required!;
            Assert.DoesNotContain(requiredModules, module => string.Equals(module.ModuleName, "Parent.Tools", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(requiredModules, module => string.Equals(module.ModuleName, "PackageManagement", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(requiredModules, module => string.Equals(module.ModuleName, "PowerShellGet", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(requiredModules, module => string.Equals(module.ModuleName, "PSReadLine", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_IncludesTransitiveDependenciesForPackaging_WhenParentRemainsRequired()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var provider = new FakeModuleDependencyMetadataProvider(
                installedModules: new Dictionary<string, InstalledModuleMetadata>(StringComparer.OrdinalIgnoreCase),
                onlineModules: new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase),
                installedRequiredModules: new Dictionary<string, IReadOnlyList<RequiredModuleReference>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Parent.Tools"] = new[] { new RequiredModuleReference("Child.Tools", moduleVersion: "2.0.0") }
                });

            var spec = CreateRequiredParentSpec(root.FullName, moduleName, ModuleDependencyVersionSource.Auto);
            var plan = new ModulePipelineRunner(new NullLogger(), new ThrowingPowerShellRunner(), provider).Plan(spec);

            Assert.Contains(plan.RequiredModules, module => string.Equals(module.ModuleName, "Parent.Tools", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(plan.RequiredModules, module => string.Equals(module.ModuleName, "Child.Tools", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(plan.RequiredModulesForPackaging, module => string.Equals(module.ModuleName, "Parent.Tools", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(plan.RequiredModulesForPackaging, module => string.Equals(module.ModuleName, "Child.Tools", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_IncludesTransitiveDependenciesForEmbeddedModules()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var provider = new FakeModuleDependencyMetadataProvider(
                installedModules: new Dictionary<string, InstalledModuleMetadata>(StringComparer.OrdinalIgnoreCase),
                onlineModules: new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase),
                installedRequiredModules: new Dictionary<string, IReadOnlyList<RequiredModuleReference>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Parent.Tools"] = new[] { new RequiredModuleReference("Child.Tools", moduleVersion: "2.0.0") }
                });

            var spec = CreateEmbeddedParentSpec(root.FullName, moduleName, ModuleDependencyVersionSource.Auto);
            var plan = new ModulePipelineRunner(new NullLogger(), new ThrowingPowerShellRunner(), provider).Plan(spec);

            Assert.Equal(new[] { "Child.Tools", "Parent.Tools" }, plan.EmbeddedModules.Select(static module => module.ModuleName));
            Assert.Empty(plan.RequiredModules);
            Assert.Empty(plan.RequiredModulesForPackaging);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_UsesResolvedEmbeddedRootReference_WhenExpandingTransitiveDependencies()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var provider = new FakeModuleDependencyMetadataProvider(
                installedModules: new Dictionary<string, InstalledModuleMetadata>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Parent.Tools"] = new("Parent.Tools", "1.0.0", "11111111-1111-1111-1111-111111111111", @"C:\Modules\Parent.Tools\1.0.0")
                },
                onlineModules: new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase),
                installedRequiredModules: new Dictionary<string, IReadOnlyList<RequiredModuleReference>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Parent.Tools"] = new[] { new RequiredModuleReference("Child.Tools", moduleVersion: "2.0.0") }
                });

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0",
                    CsprojPath = null
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.EmbeddedModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = "Parent.Tools",
                            RequiredVersion = "1.0.0",
                            Guid = "11111111-1111-1111-1111-111111111111"
                        }
                    }
                }
            };

            var plan = new ModulePipelineRunner(new NullLogger(), new ThrowingPowerShellRunner(), provider).Plan(spec);

            Assert.Equal(new[] { "Child.Tools", "Parent.Tools" }, plan.EmbeddedModules.Select(static module => module.ModuleName));
            Assert.Contains(provider.RequiredModuleReferenceLookups, module =>
                string.Equals(module.ModuleName, "Parent.Tools", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(module.RequiredVersion, "1.0.0", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(module.Guid, "11111111-1111-1111-1111-111111111111", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void PowerShellProvider_SerializesGuidConstraintsForInstalledModuleLookup()
    {
        PowerShellRunRequest? captured = null;
        var runner = new CapturingPowerShellRunner(request =>
        {
            captured = request;
            return new PowerShellRunResult(0, string.Empty, string.Empty, "pwsh.exe");
        });
        var provider = new PowerShellModuleDependencyMetadataProvider(runner, new NullLogger());

        provider.GetInstalledModules(new[]
        {
            new RequiredModuleReference(
                "Parent.Tools",
                requiredVersion: "1.0.0",
                guid: "11111111-1111-1111-1111-111111111111")
        });

        Assert.NotNull(captured);
        Assert.True(captured!.Arguments.Count >= 2);
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(captured.Arguments[1]));
        using var document = JsonDocument.Parse(json);
        var item = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("11111111-1111-1111-1111-111111111111", item.GetProperty("Guid").GetString());
    }

    [Fact]
    public void Plan_IgnoresRuntimeProvidedTransitiveDependenciesForPackaging_WhenParentRemainsRequired()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var provider = new FakeModuleDependencyMetadataProvider(
                installedModules: new Dictionary<string, InstalledModuleMetadata>(StringComparer.OrdinalIgnoreCase),
                onlineModules: new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase),
                installedRequiredModules: new Dictionary<string, IReadOnlyList<RequiredModuleReference>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Parent.Tools"] = new[]
                    {
                        new RequiredModuleReference("Child.Tools", moduleVersion: "2.0.0"),
                        new RequiredModuleReference("PackageManagement", moduleVersion: "1.0.0.1"),
                        new RequiredModuleReference("PowerShellGet", moduleVersion: "1.0.0.1"),
                        new RequiredModuleReference("PSReadLine", moduleVersion: "2.0.0")
                    }
                });

            var spec = CreateRequiredParentSpec(root.FullName, moduleName, ModuleDependencyVersionSource.Auto);
            var plan = new ModulePipelineRunner(new NullLogger(), new ThrowingPowerShellRunner(), provider).Plan(spec);

            Assert.Contains(plan.RequiredModulesForPackaging, module => string.Equals(module.ModuleName, "Parent.Tools", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(plan.RequiredModulesForPackaging, module => string.Equals(module.ModuleName, "Child.Tools", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(plan.RequiredModulesForPackaging, module => string.Equals(module.ModuleName, "PackageManagement", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(plan.RequiredModulesForPackaging, module => string.Equals(module.ModuleName, "PowerShellGet", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(plan.RequiredModulesForPackaging, module => string.Equals(module.ModuleName, "PSReadLine", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_InheritsDependencyVersionSource_ForTransitiveRequiredModules()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var provider = new FakeModuleDependencyMetadataProvider(
                installedModules: new Dictionary<string, InstalledModuleMetadata>(StringComparer.OrdinalIgnoreCase),
                onlineModules: new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Parent.Tools"] = ("1.1.0", null),
                    ["Child.Tools"] = ("2.5.0", "55555555-5555-5555-5555-555555555555")
                },
                installedRequiredModules: new Dictionary<string, IReadOnlyList<RequiredModuleReference>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Parent.Tools"] = new[]
                    {
                        new RequiredModuleReference(
                            "Child.Tools")
                    }
                });

            var spec = CreateRequiredParentSpec(root.FullName, moduleName, ModuleDependencyVersionSource.PSGallery);
            var plan = new ModulePipelineRunner(new NullLogger(), new ThrowingPowerShellRunner(), provider).Plan(spec);

            var child = Assert.Single(plan.RequiredModulesForPackaging, module => string.Equals(module.ModuleName, "Child.Tools", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("2.5.0", child.ModuleVersion);
            Assert.Equal("55555555-5555-5555-5555-555555555555", child.Guid);
            Assert.Equal("PSGallery", provider.LastOnlineRepository);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_PreservesTransitiveGuidConstraint_WhenInheritingRepositoryVersionSource()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var provider = new FakeModuleDependencyMetadataProvider(
                installedModules: new Dictionary<string, InstalledModuleMetadata>(StringComparer.OrdinalIgnoreCase),
                onlineModules: new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Child.Tools"] = ("2.5.0", "55555555-5555-5555-5555-555555555555")
                },
                installedRequiredModules: new Dictionary<string, IReadOnlyList<RequiredModuleReference>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Parent.Tools"] = new[]
                    {
                        new RequiredModuleReference(
                            "Child.Tools",
                            guid: "22222222-2222-2222-2222-222222222222")
                    }
                });

            var spec = CreateRequiredParentSpec(root.FullName, moduleName, ModuleDependencyVersionSource.PSGallery);
            var plan = new ModulePipelineRunner(new NullLogger(), new ThrowingPowerShellRunner(), provider).Plan(spec);

            var child = Assert.Single(plan.RequiredModulesForPackaging, module => string.Equals(module.ModuleName, "Child.Tools", StringComparison.OrdinalIgnoreCase));
            Assert.Null(child.ModuleVersion);
            Assert.Equal("22222222-2222-2222-2222-222222222222", child.Guid);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_PreservesTransitiveVersionConstraints_WhenInheritingRepositoryVersionSource()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var provider = new FakeModuleDependencyMetadataProvider(
                installedModules: new Dictionary<string, InstalledModuleMetadata>(StringComparer.OrdinalIgnoreCase),
                onlineModules: new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Exact.Child"] = ("5.0.0", "55555555-5555-5555-5555-555555555555"),
                    ["Minimum.Child"] = ("6.0.0", "66666666-6666-6666-6666-666666666666")
                },
                installedRequiredModules: new Dictionary<string, IReadOnlyList<RequiredModuleReference>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Parent.Tools"] = new[]
                    {
                        new RequiredModuleReference(
                            "Exact.Child",
                            requiredVersion: "2.0.0",
                            guid: "22222222-2222-2222-2222-222222222222"),
                        new RequiredModuleReference(
                            "Minimum.Child",
                            moduleVersion: "3.0.0",
                            maximumVersion: "3.9.9",
                            guid: "33333333-3333-3333-3333-333333333333")
                    }
                });

            var spec = CreateRequiredParentSpec(root.FullName, moduleName, ModuleDependencyVersionSource.PSGallery);
            var plan = new ModulePipelineRunner(new NullLogger(), new ThrowingPowerShellRunner(), provider).Plan(spec);

            var exact = Assert.Single(plan.RequiredModulesForPackaging, module => string.Equals(module.ModuleName, "Exact.Child", StringComparison.OrdinalIgnoreCase));
            Assert.Null(exact.ModuleVersion);
            Assert.Equal("2.0.0", exact.RequiredVersion);
            Assert.Equal("22222222-2222-2222-2222-222222222222", exact.Guid);

            var minimum = Assert.Single(plan.RequiredModulesForPackaging, module => string.Equals(module.ModuleName, "Minimum.Child", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("3.0.0", minimum.ModuleVersion);
            Assert.Null(minimum.RequiredVersion);
            Assert.Equal("3.9.9", minimum.MaximumVersion);
            Assert.Equal("33333333-3333-3333-3333-333333333333", minimum.Guid);
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

    private static ModulePipelineSpec CreateApprovedParentSpec(string sourcePath, string moduleName)
    {
        var spec = CreateRequiredParentSpec(sourcePath, moduleName, ModuleDependencyVersionSource.Auto);
        spec.Segments = spec.Segments
            .Concat(new IConfigurationSegment[]
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
                    Kind = ModuleDependencyKind.ApprovedModule,
                    Configuration = new ModuleDependencyConfiguration
                    {
                        ModuleName = "Parent.Tools"
                    }
                }
            })
            .ToArray();
        return spec;
    }

    private static ModulePipelineSpec CreateRequiredParentSpec(
        string sourcePath,
        string moduleName,
        ModuleDependencyVersionSource versionSource)
    {
        return new ModulePipelineSpec
        {
            Build = new ModuleBuildSpec
            {
                Name = moduleName,
                SourcePath = sourcePath,
                Version = "1.0.0",
                CsprojPath = null,
                KeepStaging = true
            },
            Install = new ModulePipelineInstallOptions { Enabled = false },
            Segments = new IConfigurationSegment[]
            {
                new ConfigurationModuleSegment
                {
                    Kind = ModuleDependencyKind.RequiredModule,
                    Configuration = new ModuleDependencyConfiguration
                    {
                        ModuleName = "Parent.Tools",
                        ModuleVersion = "1.0.0",
                        VersionSource = versionSource
                    }
                }
            }
        };
    }

    private static ModulePipelineSpec CreateEmbeddedParentSpec(
        string sourcePath,
        string moduleName,
        ModuleDependencyVersionSource versionSource)
    {
        return new ModulePipelineSpec
        {
            Build = new ModuleBuildSpec
            {
                Name = moduleName,
                SourcePath = sourcePath,
                Version = "1.0.0",
                CsprojPath = null,
                KeepStaging = true
            },
            Install = new ModulePipelineInstallOptions { Enabled = false },
            Segments = new IConfigurationSegment[]
            {
                new ConfigurationModuleSegment
                {
                    Kind = ModuleDependencyKind.EmbeddedModule,
                    Configuration = new ModuleDependencyConfiguration
                    {
                        ModuleName = "Parent.Tools",
                        ModuleVersion = "1.0.0",
                        VersionSource = versionSource
                    }
                }
            }
        };
    }

    private static string BuildLibrary(string rootPath, string assemblyName, string version, string projectFolderName)
    {
        var projectRoot = Directory.CreateDirectory(Path.Combine(rootPath, projectFolderName));
        var projectPath = Path.Combine(projectRoot.FullName, assemblyName + ".csproj");
        var sourcePath = Path.Combine(projectRoot.FullName, "Class1.cs");

        File.WriteAllText(projectPath, $$"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>{{assemblyName}}</AssemblyName>
    <Version>{{version}}</Version>
    <AssemblyVersion>{{version}}.0</AssemblyVersion>
    <FileVersion>{{version}}.0</FileVersion>
  </PropertyGroup>
</Project>
""");

        File.WriteAllText(sourcePath, $$"""
namespace {{assemblyName}}Lib;

public sealed class Marker
{
}
""");

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{projectPath}\" -c Release -nologo --verbosity quiet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = projectRoot.FullName
        };

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, $"dotnet build failed for test fixture.{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");

        var assemblyPath = Path.Combine(projectRoot.FullName, "bin", "Release", "net8.0", assemblyName + ".dll");
        Assert.True(File.Exists(assemblyPath), $"Built assembly not found: {assemblyPath}");
        return assemblyPath;
    }

    private sealed class FakeModuleDependencyMetadataProvider : IModuleDependencyMetadataProvider, IModuleDependencyReferenceMetadataProvider
    {
        private readonly IReadOnlyDictionary<string, InstalledModuleMetadata> _installedModules;
        private readonly IReadOnlyDictionary<string, (string? Version, string? Guid)> _onlineModules;
        private readonly IReadOnlyDictionary<string, IReadOnlyList<RequiredModuleReference>> _installedRequiredModules;

        internal int InstalledLookups { get; private set; }
        internal int OnlineLookups { get; private set; }
        internal int RequiredModuleLookups { get; private set; }
        internal List<RequiredModuleReference> RequiredModuleReferenceLookups { get; } = new();
        internal string? LastOnlineRepository { get; private set; }

        internal FakeModuleDependencyMetadataProvider(
            IReadOnlyDictionary<string, InstalledModuleMetadata> installedModules,
            IReadOnlyDictionary<string, (string? Version, string? Guid)> onlineModules,
            IReadOnlyDictionary<string, IReadOnlyList<RequiredModuleReference>>? installedRequiredModules = null)
        {
            _installedModules = installedModules;
            _onlineModules = onlineModules;
            _installedRequiredModules = installedRequiredModules ?? new Dictionary<string, IReadOnlyList<RequiredModuleReference>>(StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyDictionary<string, InstalledModuleMetadata> GetLatestInstalledModules(IReadOnlyList<string> names)
        {
            InstalledLookups++;
            var result = new Dictionary<string, InstalledModuleMetadata>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in names ?? Array.Empty<string>())
            {
                if (_installedModules.TryGetValue(name, out var module))
                    result[name] = module;
                else
                    result[name] = new InstalledModuleMetadata(name, null, null, null);
            }

            return result;
        }

        public IReadOnlyList<RequiredModuleReference> GetRequiredModulesForInstalledModule(string moduleName)
        {
            RequiredModuleLookups++;
            if (string.IsNullOrWhiteSpace(moduleName))
                return Array.Empty<RequiredModuleReference>();

            return _installedRequiredModules.TryGetValue(moduleName, out var modules)
                ? modules
                : Array.Empty<RequiredModuleReference>();
        }

        public IReadOnlyList<RequiredModuleReference> GetRequiredModulesForInstalledModule(RequiredModuleReference reference)
        {
            RequiredModuleLookups++;
            if (reference is null || string.IsNullOrWhiteSpace(reference.ModuleName))
                return Array.Empty<RequiredModuleReference>();

            RequiredModuleReferenceLookups.Add(reference);
            return _installedRequiredModules.TryGetValue(reference.ModuleName, out var modules)
                ? modules
                : Array.Empty<RequiredModuleReference>();
        }

        public IReadOnlyDictionary<string, (string? Version, string? Guid)> ResolveLatestOnlineVersions(
            IReadOnlyCollection<string> names,
            string? repository,
            RepositoryCredential? credential,
            bool prerelease)
        {
            OnlineLookups++;
            LastOnlineRepository = repository;
            var result = new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in names ?? Array.Empty<string>())
            {
                if (_onlineModules.TryGetValue(name, out var module))
                    result[name] = module;
            }

            return result;
        }
    }

    private sealed class ThrowingPowerShellRunner : IPowerShellRunner
    {
        public PowerShellRunResult Run(PowerShellRunRequest request)
            => throw new InvalidOperationException("PowerShell runner should not be used when dependency metadata provider is injected.");
    }

    private sealed class CapturingPowerShellRunner : IPowerShellRunner
    {
        private readonly Func<PowerShellRunRequest, PowerShellRunResult> _run;

        public CapturingPowerShellRunner(Func<PowerShellRunRequest, PowerShellRunResult> run)
        {
            _run = run;
        }

        public PowerShellRunResult Run(PowerShellRunRequest request)
            => _run(request);
    }

    private sealed class CollectingLogger : ILogger
    {
        public List<string> Infos { get; } = new();
        public bool IsVerbose => false;

        public void Info(string message) => Infos.Add(message ?? string.Empty);
        public void Success(string message) { }
        public void Warn(string message) { }
        public void Error(string message) { }
        public void Verbose(string message) { }
    }
}
