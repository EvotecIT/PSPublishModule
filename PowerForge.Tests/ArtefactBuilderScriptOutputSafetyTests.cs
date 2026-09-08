using PowerForge;

namespace PowerForge.Tests;

public sealed class ArtefactBuilderScriptOutputSafetyTests
{
    [Theory]
    [InlineData(ArtefactType.Script, false)]
    [InlineData(ArtefactType.Script, true)]
    [InlineData(ArtefactType.ScriptPacked, false)]
    [InlineData(ArtefactType.ScriptPacked, true)]
    public void Build_RejectsOutputRootThatContainsProjectBeforeCleanup(
        ArtefactType artefactType,
        bool useProjectRoot)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "SafeModule";
            string outputRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "repository-container")).FullName;
            string projectRoot = Directory.CreateDirectory(Path.Combine(outputRoot, "project")).FullName;
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            File.WriteAllText(Path.Combine(stagingRoot, moduleName + ".psd1"), "@{ RootModule = 'SafeModule.psm1'; ModuleVersion = '1.0.0' }");
            File.WriteAllText(Path.Combine(stagingRoot, moduleName + ".psm1"), "'ok'");
            string marker = Path.Combine(projectRoot, "preserve.txt");
            File.WriteAllText(marker, "preserve");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new ArtefactBuilder(new NullLogger()).Build(
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = artefactType,
                        Configuration = new ArtefactConfiguration
                        {
                            Enabled = true,
                            Path = useProjectRoot ? projectRoot : outputRoot,
                            ArtefactName = "SafeModule.zip"
                        }
                    },
                    projectRoot,
                    stagingRoot,
                    moduleName,
                    "1.0.0",
                    null,
                    Array.Empty<RequiredModuleReference>()));

            Assert.Contains("contains project root", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("preserve", File.ReadAllText(marker));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Build_ScriptPackedAllowsProjectOutputWhenCleanupIsDisabled()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "SafeModule";
            string projectRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "project")).FullName;
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            File.WriteAllText(Path.Combine(stagingRoot, moduleName + ".psd1"), "@{ RootModule = 'SafeModule.psm1'; ModuleVersion = '1.0.0' }");
            File.WriteAllText(Path.Combine(stagingRoot, moduleName + ".psm1"), "'ok'");
            string marker = Path.Combine(projectRoot, "preserve.txt");
            File.WriteAllText(marker, "preserve");

            ArtefactBuildResult result = new ArtefactBuilder(new NullLogger()).Build(
                new ConfigurationArtefactSegment
                {
                    ArtefactType = ArtefactType.ScriptPacked,
                    Configuration = new ArtefactConfiguration
                    {
                        Enabled = true,
                        Path = projectRoot,
                        ArtefactName = "SafeModule.zip",
                        DoNotClear = true
                    }
                },
                projectRoot,
                stagingRoot,
                moduleName,
                "1.0.0",
                null,
                Array.Empty<RequiredModuleReference>());

            Assert.Equal(Path.Combine(projectRoot, "SafeModule.zip"), result.OutputPath);
            Assert.True(File.Exists(result.OutputPath));
            Assert.Equal("preserve", File.ReadAllText(marker));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }
}
