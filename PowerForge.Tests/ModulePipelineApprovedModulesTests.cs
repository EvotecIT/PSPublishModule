using System;
using System.IO;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePipelineApprovedModulesTests
{
    [Fact]
    public void Plan_RemovesApprovedModulesFromRequired_WhenMergeMissingEnabled()
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
            Assert.DoesNotContain("Graphimo", requiredNames, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_KeepsApprovedModulesInRequired_WhenMergeMissingDisabled()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = BuildSpec(root.FullName, moduleName, mergeMissing: false);
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

    private static ModulePipelineSpec BuildSpec(string sourcePath, string moduleName, bool mergeMissing)
    {
        return new ModulePipelineSpec
        {
            Build = new ModuleBuildSpec
            {
                Name = moduleName,
                SourcePath = sourcePath,
                Version = "1.0.0",
                CsprojPath = null
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
}
