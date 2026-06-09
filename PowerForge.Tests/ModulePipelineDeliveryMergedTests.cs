using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePipelineDeliveryMergedTests
{
    [Fact]
    public void Run_MergedModule_IncludesGeneratedDeliveryCommandsInRootPsm1_AndUnpackedArtefact()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";

            var projectRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "src"));
            var publicDir = Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "Public"));
            var privateDir = Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "Private"));
            var internalsDir = Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "Internals"));
            var artefactsDir = Path.Combine(tempRoot.FullName, "artefacts");

            File.WriteAllText(Path.Combine(projectRoot.FullName, $"{moduleName}.psd1"), "@{ ModuleVersion = '1.0.0'; RootModule = 'TestModule.psm1'; FunctionsToExport = @(); CmdletsToExport = @(); AliasesToExport = @() }");
            File.WriteAllText(Path.Combine(projectRoot.FullName, $"{moduleName}.psm1"), string.Empty);
            File.WriteAllText(Path.Combine(publicDir.FullName, "Get-Test.ps1"), "function Get-Test { 'ok' }");
            File.WriteAllText(Path.Combine(privateDir.FullName, "Invoke-Hidden.ps1"), "function Invoke-Hidden { 'ok' }");
            File.WriteAllText(Path.Combine(internalsDir.FullName, "tool.txt"), "hello");

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = projectRoot.FullName,
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
                    },
                    new ConfigurationOptionsSegment
                    {
                        Options = new ConfigurationOptions
                        {
                            Delivery = new DeliveryOptionsConfiguration
                            {
                                Enable = true,
                                InternalsPath = "Internals",
                                GenerateInstallCommand = true,
                                GenerateUpdateCommand = true
                            }
                        }
                    },
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Unpacked,
                        Configuration = new ArtefactConfiguration
                        {
                            Enabled = true,
                            Path = artefactsDir
                        }
                    }
                }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);
            var result = runner.Run(spec, plan);

            var stagingPsm1 = File.ReadAllText(Path.Combine(result.BuildResult.StagingPath, $"{moduleName}.psm1"));
            Assert.Contains("function Install-TestModule", stagingPsm1, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("function Update-TestModule", stagingPsm1, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("$FunctionsToExport = @(", stagingPsm1, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Install-TestModule", stagingPsm1, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Update-TestModule", stagingPsm1, StringComparison.OrdinalIgnoreCase);

            var artefactPsm1Path = Path.Combine(artefactsDir, moduleName, $"{moduleName}.psm1");
            Assert.True(File.Exists(artefactPsm1Path));

            var artefactPsm1 = File.ReadAllText(artefactPsm1Path);
            Assert.Contains("function Install-TestModule", artefactPsm1, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("function Update-TestModule", artefactPsm1, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(Path.Combine(artefactsDir, moduleName, "Internals", "tool.txt")));
            Assert.False(Directory.Exists(Path.Combine(artefactsDir, moduleName, "Public")));
            Assert.False(Directory.Exists(Path.Combine(artefactsDir, moduleName, "Private")));
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Run_ReusedExistingPsm1_EmbedsGeneratedDeliveryCommands_WithoutLooseScriptFolders()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";

            var projectRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "src"));
            var publicDir = Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "Public"));
            var privateDir = Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "Private"));
            var internalsDir = Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "Internals"));
            var artefactsDir = Path.Combine(tempRoot.FullName, "artefacts");

            File.WriteAllText(Path.Combine(projectRoot.FullName, $"{moduleName}.psd1"), "@{ ModuleVersion = '1.0.0'; RootModule = 'TestModule.psm1'; FunctionsToExport = @(); CmdletsToExport = @(); AliasesToExport = @() }");
            File.WriteAllText(Path.Combine(projectRoot.FullName, $"{moduleName}.psm1"), "function Get-TestModule { 'ok' }");
            File.WriteAllText(Path.Combine(internalsDir.FullName, "tool.txt"), "hello");

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = projectRoot.FullName,
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
                    },
                    new ConfigurationOptionsSegment
                    {
                        Options = new ConfigurationOptions
                        {
                            Delivery = new DeliveryOptionsConfiguration
                            {
                                Enable = true,
                                InternalsPath = "Internals",
                                GenerateInstallCommand = true,
                                GenerateUpdateCommand = true
                            }
                        }
                    },
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Unpacked,
                        Configuration = new ArtefactConfiguration
                        {
                            Enabled = true,
                            Path = artefactsDir
                        }
                    }
                }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);
            var result = runner.Run(spec, plan);

            var stagingPsm1 = File.ReadAllText(Path.Combine(result.BuildResult.StagingPath, $"{moduleName}.psm1"));
            Assert.Contains("Auto-generated by PowerForge", stagingPsm1, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("function Install-TestModule", stagingPsm1, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("function Update-TestModule", stagingPsm1, StringComparison.OrdinalIgnoreCase);

            var artefactPsm1Path = Path.Combine(artefactsDir, moduleName, $"{moduleName}.psm1");
            Assert.True(File.Exists(artefactPsm1Path));

            var artefactPsm1 = File.ReadAllText(artefactPsm1Path);
            Assert.Contains("Auto-generated by PowerForge", artefactPsm1, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("function Install-TestModule", artefactPsm1, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("function Update-TestModule", artefactPsm1, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(Path.Combine(artefactsDir, moduleName, "Internals", "tool.txt")));
            Assert.False(Directory.Exists(Path.Combine(artefactsDir, moduleName, "Public")));
            Assert.False(Directory.Exists(Path.Combine(artefactsDir, moduleName, "Private")));
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}
