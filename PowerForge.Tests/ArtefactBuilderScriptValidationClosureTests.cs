using PowerForge;

namespace PowerForge.Tests;

public sealed partial class ArtefactBuilderScriptClosureTests
{
    [Theory]
    [InlineData(ArtefactType.Script, "NestedModules")]
    [InlineData(ArtefactType.Script, "RequiredAssemblies")]
    [InlineData(ArtefactType.Script, "RequiredModules")]
    [InlineData(ArtefactType.Script, "ScriptsToProcess")]
    [InlineData(ArtefactType.Script, "TypesToProcess")]
    [InlineData(ArtefactType.Script, "FormatsToProcess")]
    [InlineData(ArtefactType.ScriptPacked, "NestedModules")]
    [InlineData(ArtefactType.ScriptPacked, "RequiredAssemblies")]
    [InlineData(ArtefactType.ScriptPacked, "RequiredModules")]
    [InlineData(ArtefactType.ScriptPacked, "ScriptsToProcess")]
    [InlineData(ArtefactType.ScriptPacked, "TypesToProcess")]
    [InlineData(ArtefactType.ScriptPacked, "FormatsToProcess")]
    public void Build_NonliteralManifestLoadedContentIsRejectedBeforeExistingOutputIsChanged(
        ArtefactType artefactType,
        string propertyName)
    {
        var root = CreateRoot();
        try
        {
            const string moduleName = "ComputedManifestModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            File.WriteAllText(
                Path.Combine(stagingRoot, moduleName + ".psd1"),
                $"@{{ RootModule = '{moduleName}.psm1'; ModuleVersion = '1.0.0'; {propertyName} = @($PSScriptRoot + '\\Initialize.ps1') }}");
            File.WriteAllText(Path.Combine(stagingRoot, moduleName + ".psm1"), "'root'");

            string outputRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "output")).FullName;
            string marker = Path.Combine(outputRoot, "existing.txt");
            File.WriteAllText(marker, "preserve");

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => Build(
                root.FullName,
                stagingRoot,
                outputRoot,
                moduleName,
                artefactType));

            Assert.Contains(propertyName, exception.Message, StringComparison.Ordinal);
            Assert.Contains("literal", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("preserve", File.ReadAllText(marker));
        }
        finally
        {
            Delete(root);
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script, "CON.ps1")]
    [InlineData(ArtefactType.Script, "bad:name.ps1")]
    [InlineData(ArtefactType.ScriptPacked, "LPT1.ps1")]
    [InlineData(ArtefactType.ScriptPacked, "bad?name.ps1")]
    public void Build_InvalidScriptNameIsRejectedBeforeExistingOutputIsChanged(
        ArtefactType artefactType,
        string scriptName)
    {
        var root = CreateRoot();
        try
        {
            const string moduleName = "InvalidNameModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            WriteScriptModule(stagingRoot, moduleName);
            string outputRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "output")).FullName;
            string marker = Path.Combine(outputRoot, "existing.txt");
            File.WriteAllText(marker, "preserve");
            ConfigurationArtefactSegment segment = CreateSegment(outputRoot, artefactType);
            segment.Configuration.ScriptName = scriptName;

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                new ArtefactBuilder(new NullLogger()).Build(
                    segment,
                    root.FullName,
                    stagingRoot,
                    moduleName,
                    "1.0.0",
                    null,
                    Array.Empty<RequiredModuleReference>()));

            Assert.Contains("ScriptName must be a file name", exception.Message, StringComparison.Ordinal);
            Assert.Equal("preserve", File.ReadAllText(marker));
        }
        finally
        {
            Delete(root);
        }
    }

    [Theory]
    [InlineData(ArtefactType.Packed, "CON.zip")]
    [InlineData(ArtefactType.Packed, "bad:name.zip")]
    [InlineData(ArtefactType.ScriptPacked, "LPT1.zip")]
    [InlineData(ArtefactType.ScriptPacked, "bad?name.zip")]
    public void Build_InvalidArchiveNameIsRejectedBeforeExistingOutputIsChanged(
        ArtefactType artefactType,
        string artefactName)
    {
        var root = CreateRoot();
        try
        {
            const string moduleName = "InvalidArchiveNameModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            WriteScriptModule(stagingRoot, moduleName);
            string outputRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "output")).FullName;
            string marker = Path.Combine(outputRoot, "existing.txt");
            File.WriteAllText(marker, "preserve");
            ConfigurationArtefactSegment segment = CreateSegment(outputRoot, artefactType);
            segment.Configuration.ArtefactName = artefactName;

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                new ArtefactBuilder(new NullLogger()).Build(
                    segment,
                    root.FullName,
                    stagingRoot,
                    moduleName,
                    "1.0.0",
                    null,
                    Array.Empty<RequiredModuleReference>()));

            Assert.Contains("ArtefactName must be a file name", exception.Message, StringComparison.Ordinal);
            Assert.Equal("preserve", File.ReadAllText(marker));
        }
        finally
        {
            Delete(root);
        }
    }
}
