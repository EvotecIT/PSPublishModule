using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePipelineRelativePathRewriteTests
{
    [Fact]
    public void Run_Merge_RewritesLegacyPSScriptRootParentPathsByDefault()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMergeModule(root.FullName, moduleName);

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0",
                    KeepStaging = true
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildSegment
                    {
                        BuildModule = new BuildModuleConfiguration
                        {
                            Merge = true
                        }
                    }
                }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);
            var result = runner.Run(spec, plan);

            var mergedPsm1 = File.ReadAllText(Path.Combine(result.BuildResult.StagingPath, moduleName + ".psm1"));
            Assert.DoesNotContain("$PSScriptRoot\\..\\Resources\\JS\\jquery.min.js", mergedPsm1, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("[IO.Path]::Combine($PSScriptRoot, '..', 'Resources')", mergedPsm1, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("$PSScriptRoot\\Resources\\JS\\jquery.min.js", mergedPsm1, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[IO.Path]::Combine($PSScriptRoot, 'Resources')", mergedPsm1, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Run_Merge_PreservesLegacyPSScriptRootParentPathsWhenOptedOut()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMergeModule(root.FullName, moduleName);

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0",
                    KeepStaging = true
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildSegment
                    {
                        BuildModule = new BuildModuleConfiguration
                        {
                            Merge = true,
                            DoNotAttemptToFixRelativePaths = true
                        }
                    }
                }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);
            var result = runner.Run(spec, plan);

            var mergedPsm1 = File.ReadAllText(Path.Combine(result.BuildResult.StagingPath, moduleName + ".psm1"));
            Assert.Contains("$PSScriptRoot\\..\\Resources\\JS\\jquery.min.js", mergedPsm1, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[IO.Path]::Combine($PSScriptRoot, '..', 'Resources')", mergedPsm1, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static void WriteMergeModule(string rootPath, string moduleName)
    {
        Directory.CreateDirectory(Path.Combine(rootPath, "Public"));

        File.WriteAllText(Path.Combine(rootPath, moduleName + ".psm1"), "# bootstrap");
        File.WriteAllText(
            Path.Combine(rootPath, moduleName + ".psd1"),
            "@{" + Environment.NewLine +
            "    RootModule = '" + moduleName + ".psm1'" + Environment.NewLine +
            "    ModuleVersion = '1.0.0'" + Environment.NewLine +
            "    GUID = '11111111-1111-1111-1111-111111111111'" + Environment.NewLine +
            "    Author = 'Tests'" + Environment.NewLine +
            "    CompanyName = 'Tests'" + Environment.NewLine +
            "    Description = 'Test module'" + Environment.NewLine +
            "    FunctionsToExport = @('*')" + Environment.NewLine +
            "    CmdletsToExport = @()" + Environment.NewLine +
            "    VariablesToExport = @('*')" + Environment.NewLine +
            "    AliasesToExport = @('*')" + Environment.NewLine +
            "}");

        File.WriteAllText(
            Path.Combine(rootPath, "Public", "Get-TestExample.ps1"),
            "function Get-TestExample {" + Environment.NewLine +
            "    $pathA = '$PSScriptRoot\\..\\Resources\\JS\\jquery.min.js'" + Environment.NewLine +
            "    $pathB = \"[IO.Path]::Combine($PSScriptRoot, '..', 'Resources')\"" + Environment.NewLine +
            "    $pathA" + Environment.NewLine +
            "    $pathB" + Environment.NewLine +
            "}");
    }
}
