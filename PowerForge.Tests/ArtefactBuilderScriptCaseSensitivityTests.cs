namespace PowerForge.Tests;

public sealed class ArtefactBuilderScriptCaseSensitivityTests
{
    [Fact]
    public void Build_ScriptAcceptsCaseVariantModuleFilesOnCaseInsensitiveFileSystem()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            if (FrameworkCompatibility.GetPathStringComparisonForPath(root.FullName) != StringComparison.OrdinalIgnoreCase)
                return;

            const string moduleName = "CaseModule";
            var stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging"));
            File.WriteAllText(
                Path.Combine(stagingRoot.FullName, "casemodule.psd1"),
                "@{ RootModule = 'CaseModule.psm1'; ModuleVersion = '1.0.0' }");
            File.WriteAllText(Path.Combine(stagingRoot.FullName, "casemodule.psm1"), "'ok'");

            ArtefactBuildResult result = new ArtefactBuilder(new NullLogger()).Build(
                new ConfigurationArtefactSegment
                {
                    ArtefactType = ArtefactType.Script,
                    Configuration = new ArtefactConfiguration
                    {
                        Enabled = true,
                        Path = Path.Combine(root.FullName, "Artefacts", "Script")
                    }
                },
                root.FullName,
                stagingRoot.FullName,
                moduleName,
                "1.0.0",
                null,
                Array.Empty<RequiredModuleReference>());

            Assert.True(File.Exists(Path.Combine(result.OutputPath, moduleName + ".ps1")));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }
}
