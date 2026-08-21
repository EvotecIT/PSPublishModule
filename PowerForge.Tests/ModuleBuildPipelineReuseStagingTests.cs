namespace PowerForge.Tests;

public sealed class ModuleBuildPipelineReuseStagingTests
{
    [Fact]
    public void BuildToStaging_PrebuiltPayloadsGenerateFolderDrivenRuntimeSelectionWithoutFrameworkMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        var sourcePath = Path.Combine(root, "source");
        var stagingPath = Path.Combine(root, "staging");
        try
        {
            Directory.CreateDirectory(sourcePath);
            File.WriteAllText(
                Path.Combine(sourcePath, "SampleModule.psd1"),
                "@{ RootModule = 'SampleModule.psm1'; ModuleVersion = '1.0.0'; CmdletsToExport = @(); AliasesToExport = @() }");
            File.WriteAllText(Path.Combine(sourcePath, "SampleModule.psm1"), string.Empty);
            foreach (var payload in new[] { "Standard", "Core", "Core-net10.0" })
            {
                var payloadPath = Directory.CreateDirectory(Path.Combine(sourcePath, "Lib", payload));
                File.WriteAllText(Path.Combine(payloadPath.FullName, "SampleModule.dll"), string.Empty);
            }

            var result = ModuleBuildPipelineFactory.Create(new NullLogger()).BuildToStaging(new ModuleBuildSpec
            {
                Name = "SampleModule",
                SourcePath = sourcePath,
                StagingPath = stagingPath,
                Version = "1.0.0",
                SkipDotNetBuild = true,
                Frameworks = Array.Empty<string>(),
                ExportAssemblies = new[] { "SampleModule.dll" },
                DisableBinaryCmdletScan = true,
            });

            var bootstrapper = File.ReadAllText(Path.Combine(result.StagingPath, "SampleModule.psm1"));
            Assert.Contains("foreach ($PowerForgeRuntimeFolder in @($AssemblyFolders.Name))", bootstrapper);
            Assert.Contains("$Framework = $PowerForgeSelectedRuntimeFolder", bootstrapper);
            Assert.DoesNotContain("$Framework = 'Core-net10.0'", bootstrapper, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StageToStaging_ReusesExistingPayloadWithoutCopyingSourceAgain()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        var sourcePath = Path.Combine(root, "source");
        var stagingPath = Path.Combine(root, "staging");
        try
        {
            Directory.CreateDirectory(sourcePath);
            Directory.CreateDirectory(stagingPath);
            File.WriteAllText(Path.Combine(sourcePath, "SampleModule.psd1"), "@{ ModuleVersion = '1.0.0' }");
            File.WriteAllText(Path.Combine(sourcePath, "source-only.txt"), "must not be copied");
            File.WriteAllText(Path.Combine(stagingPath, "SampleModule.psd1"), "@{ ModuleVersion = '1.0.0' }");
            File.WriteAllText(Path.Combine(stagingPath, "checkpoint.txt"), "approved output");

            var staged = ModuleBuildPipelineFactory
                .Create(new NullLogger())
                .StageToStaging(new ModuleBuildSpec
                {
                    Name = "SampleModule",
                    SourcePath = sourcePath,
                    StagingPath = stagingPath,
                    Version = "1.0.0",
                    SkipDotNetBuild = true,
                    ReuseStaging = true
                });

            Assert.Equal(Path.GetFullPath(stagingPath), staged.StagingPath);
            Assert.Equal("approved output", File.ReadAllText(Path.Combine(stagingPath, "checkpoint.txt")));
            Assert.False(File.Exists(Path.Combine(stagingPath, "source-only.txt")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
