using System;
using System.IO;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePipelineApprovedModulesTests
{
    [Fact]
    public void Plan_KeepsApprovedModulesInRequired_WhenMergeMissingEnabled()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = BuildSpec(root.FullName, moduleName, mergeMissing: true);
            var plan = new ModulePipelineRunner(new NullLogger()).Plan(spec);

            var requiredNames = plan.RequiredModules
                .Select(m => m.ModuleName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToArray();

            Assert.Contains("PSWriteHTML", requiredNames, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("Graphimo", requiredNames, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Run_RemovesApprovedModulesFromManifest_WhenMergeMissingEnabled()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = BuildSpec(root.FullName, moduleName, mergeMissing: true);
            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);
            var result = runner.Run(spec, plan);

            var requiredNames = ReadRequiredModuleNames(result.BuildResult.ManifestPath);

            Assert.Contains("PSWriteHTML", requiredNames, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("Graphimo", requiredNames, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Run_KeepsApprovedModulesInManifest_WhenMergeMissingDisabled()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = BuildSpec(root.FullName, moduleName, mergeMissing: false);
            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);
            var result = runner.Run(spec, plan);

            var requiredNames = ReadRequiredModuleNames(result.BuildResult.ManifestPath);

            Assert.Contains("PSWriteHTML", requiredNames, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("Graphimo", requiredNames, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Run_ClearsStaleRequiredModules_WhenAllConfiguredRequiredAreFilteredByMergeMissing()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var manifestPath = Path.Combine(root.FullName, $"{moduleName}.psd1");
            Assert.True(ManifestEditor.TrySetRequiredModules(
                manifestPath,
                new[]
                {
                    new ManifestEditor.RequiredModule("LegacyOnly", moduleVersion: "1.0.0"),
                    new ManifestEditor.RequiredModule("Microsoft.PowerShell.Utility")
                }));

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0",
                    CsprojPath = null,
                    KeepStaging = true
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
                            ModuleName = "Graphimo",
                            ModuleVersion = "1.0.0"
                        }
                    },
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.ApprovedModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = "Graphimo"
                        }
                    }
                }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);
            var result = runner.Run(spec, plan);
            var requiredNames = ReadRequiredModuleNames(result.BuildResult.ManifestPath);

            Assert.Empty(requiredNames);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Run_ClearsStaleManifestDependencies_WhenNoModuleDependencySegmentsAreConfigured()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var manifestPath = Path.Combine(root.FullName, $"{moduleName}.psd1");
            Assert.True(ManifestEditor.TrySetRequiredModules(
                manifestPath,
                new[]
                {
                    new ManifestEditor.RequiredModule("LegacyOnly", moduleVersion: "1.0.0"),
                    new ManifestEditor.RequiredModule("Microsoft.PowerShell.Utility")
                }));
            Assert.True(ManifestEditor.TrySetPsDataStringArray(
                manifestPath,
                "ExternalModuleDependencies",
                new[] { "Microsoft.PowerShell.Utility", "Microsoft.PowerShell.Management" }));

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0",
                    CsprojPath = null,
                    KeepStaging = true
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = Array.Empty<IConfigurationSegment>()
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);
            var result = runner.Run(spec, plan);

            var requiredNames = ReadRequiredModuleNames(result.BuildResult.ManifestPath);
            Assert.Empty(requiredNames);

            Assert.True(ManifestEditor.TryGetPsDataStringArray(result.BuildResult.ManifestPath, "ExternalModuleDependencies", out var externalDeps));
            Assert.NotNull(externalDeps);
            Assert.Empty(externalDeps!);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static ModulePipelineSpec BuildSpec(string sourcePath, string moduleName, bool mergeMissing)
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
                new ConfigurationBuildSegment
                {
                    BuildModule = new BuildModuleConfiguration
                    {
                        MergeMissing = mergeMissing
                    }
                },
                new ConfigurationModuleSegment
                {
                    Kind = ModuleDependencyKind.RequiredModule,
                    Configuration = new ModuleDependencyConfiguration
                    {
                        ModuleName = "PSWriteHTML",
                        ModuleVersion = "1.0.0",
                        Guid = "11111111-1111-1111-1111-111111111111"
                    }
                },
                new ConfigurationModuleSegment
                {
                    Kind = ModuleDependencyKind.RequiredModule,
                    Configuration = new ModuleDependencyConfiguration
                    {
                        ModuleName = "Graphimo",
                        ModuleVersion = "1.0.0",
                        Guid = "22222222-2222-2222-2222-222222222222"
                    }
                },
                new ConfigurationModuleSegment
                {
                    Kind = ModuleDependencyKind.ApprovedModule,
                    Configuration = new ModuleDependencyConfiguration
                    {
                        ModuleName = "Graphimo"
                    }
                }
            }
        };
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

    private static string[] ReadRequiredModuleNames(string manifestPath)
    {
        Assert.True(ManifestEditor.TryGetRequiredModules(manifestPath, out var required));
        return (required ?? Array.Empty<ManifestEditor.RequiredModule>())
            .Select(m => m.ModuleName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!.Trim())
            .ToArray();
    }
}
