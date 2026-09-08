using System.IO.Compression;
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
    [InlineData(ArtefactType.Script, "COM¹.ps1")]
    [InlineData(ArtefactType.ScriptPacked, "LPT1.ps1")]
    [InlineData(ArtefactType.ScriptPacked, "bad?name.ps1")]
    [InlineData(ArtefactType.ScriptPacked, "LPT³.ps1")]
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
    [InlineData(ArtefactType.Packed, "COM².zip")]
    [InlineData(ArtefactType.Packed, "archive.bin")]
    [InlineData(ArtefactType.ScriptPacked, "LPT1.zip")]
    [InlineData(ArtefactType.ScriptPacked, "bad?name.zip")]
    [InlineData(ArtefactType.ScriptPacked, "LPT¹.zip")]
    [InlineData(ArtefactType.ScriptPacked, "archive.bin")]
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

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Build_ScriptDestinationRootCannotOverlapStagingBeforeAnyMutation(
        bool configureRequiredModulesRoot,
        bool useStagingAncestor)
    {
        var root = CreateRoot();
        try
        {
            const string moduleName = "OverlappingStagingModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            WriteScriptModule(stagingRoot, moduleName);
            string stagingMarker = Path.Combine(stagingRoot, "preserve.txt");
            File.WriteAllText(stagingMarker, "preserve");
            string outputRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "output")).FullName;
            ConfigurationArtefactSegment segment = CreateSegment(outputRoot, ArtefactType.Script);
            string overlappingPath = useStagingAncestor ? root.FullName : stagingRoot;
            if (configureRequiredModulesRoot)
            {
                segment.Configuration.RequiredModules.Enabled = true;
                segment.Configuration.RequiredModules.Path = overlappingPath;
                segment.Configuration.RequiredModules.ModulesPath = "app";
            }
            else
            {
                segment.Configuration.RequiredModules.ModulesPath = overlappingPath;
            }

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                new ArtefactBuilder(new NullLogger()).Build(
                    segment,
                    root.FullName,
                    stagingRoot,
                    moduleName,
                    "1.0.0",
                    null,
                    Array.Empty<RequiredModuleReference>()));

            Assert.Contains(
                configureRequiredModulesRoot ? "required modules root" : "script root",
                exception.Message,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains("overlaps staging", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("preserve", File.ReadAllText(stagingMarker));
        }
        finally
        {
            Delete(root);
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script)]
    [InlineData(ArtefactType.ScriptPacked)]
    public void Build_OrdinaryConversionOmitsStaleReleaseProvenance(ArtefactType artefactType)
    {
        var root = CreateRoot();
        string? extractionRoot = null;
        try
        {
            const string moduleName = "StaleProvenanceModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            WriteScriptModule(stagingRoot, moduleName);
            File.WriteAllText(
                Path.Combine(stagingRoot, PowerForgeModuleSourceAttestationWriter.FileName),
                "@{ SchemaVersion = '1' }");
            File.WriteAllText(
                Path.Combine(stagingRoot, PublishedRegistryProvenanceValidator.ModuleProvenanceFileName),
                "{\"schemaVersion\":1}");

            ArtefactBuildResult result = Build(
                root.FullName,
                stagingRoot,
                Path.Combine(root.FullName, "output"),
                moduleName,
                artefactType);

            string inspectionRoot = result.OutputPath;
            if (artefactType == ArtefactType.ScriptPacked)
            {
                extractionRoot = Path.Combine(root.FullName, "extracted-provenance");
                ZipFile.ExtractToDirectory(result.OutputPath, extractionRoot);
                inspectionRoot = extractionRoot;
            }

            Assert.False(File.Exists(Path.Combine(inspectionRoot, PowerForgeModuleSourceAttestationWriter.FileName)));
            Assert.False(File.Exists(Path.Combine(inspectionRoot, PublishedRegistryProvenanceValidator.ModuleProvenanceFileName)));
        }
        finally
        {
            Delete(root);
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script)]
    [InlineData(ArtefactType.ScriptPacked)]
    public void Build_OutputRootCannotOverlapStagingBeforeAnyMutation(ArtefactType artefactType)
    {
        var root = CreateRoot();
        try
        {
            const string moduleName = "OverlappingOutputModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            WriteScriptModule(stagingRoot, moduleName);
            string marker = Path.Combine(stagingRoot, "preserve.txt");
            File.WriteAllText(marker, "preserve");
            ConfigurationArtefactSegment segment = CreateSegment(stagingRoot, artefactType);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                new ArtefactBuilder(new NullLogger()).Build(
                    segment,
                    root.FullName,
                    stagingRoot,
                    moduleName,
                    "1.0.0",
                    null,
                    Array.Empty<RequiredModuleReference>()));

            Assert.Contains("output root", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("overlaps staging", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("preserve", File.ReadAllText(marker));
        }
        finally
        {
            Delete(root);
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script, false)]
    [InlineData(ArtefactType.Script, true)]
    [InlineData(ArtefactType.ScriptPacked, false)]
    [InlineData(ArtefactType.ScriptPacked, true)]
    public void Build_MissingCopySourceIsRejectedBeforeExistingOutputIsChanged(
        ArtefactType artefactType,
        bool directoryMapping)
    {
        var root = CreateRoot();
        try
        {
            const string moduleName = "MissingMappingSourceModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            WriteScriptModule(stagingRoot, moduleName);
            string outputRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "output")).FullName;
            string marker = Path.Combine(outputRoot, "existing.txt");
            File.WriteAllText(marker, "preserve");
            ConfigurationArtefactSegment segment = CreateSegment(outputRoot, artefactType);
            var mapping = new ArtefactCopyMapping
            {
                Source = Path.Combine(root.FullName, directoryMapping ? "missing-directory" : "missing-file.txt"),
                Destination = directoryMapping ? "assets" : "notice.txt"
            };
            if (directoryMapping)
                segment.Configuration.DirectoryOutput = [mapping];
            else
                segment.Configuration.FilesOutput = [mapping];

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                new ArtefactBuilder(new NullLogger()).Build(
                    segment,
                    root.FullName,
                    stagingRoot,
                    moduleName,
                    "1.0.0",
                    null,
                    Array.Empty<RequiredModuleReference>()));

            Assert.Contains("copy source", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("does not exist", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("preserve", File.ReadAllText(marker));
        }
        finally
        {
            Delete(root);
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script, false)]
    [InlineData(ArtefactType.Script, true)]
    [InlineData(ArtefactType.ScriptPacked, false)]
    public void Build_CopySourceRemovedByOutputClearIsRejectedBeforeExistingOutputIsChanged(
        ArtefactType artefactType,
        bool directoryMapping)
    {
        var root = CreateRoot();
        try
        {
            const string moduleName = "ClearedMappingSourceModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            WriteScriptModule(stagingRoot, moduleName);
            string outputRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "output")).FullName;
            string marker = Path.Combine(outputRoot, "existing.txt");
            File.WriteAllText(marker, "preserve");
            ConfigurationArtefactSegment segment = CreateSegment(outputRoot, artefactType);
            if (directoryMapping)
            {
                string source = Directory.CreateDirectory(Path.Combine(outputRoot, "source-assets")).FullName;
                File.WriteAllText(Path.Combine(source, "asset.txt"), "asset");
                segment.Configuration.DirectoryOutput =
                    [new ArtefactCopyMapping { Source = source, Destination = "copied-assets" }];
            }
            else
            {
                string source = Path.Combine(outputRoot, "source.txt");
                File.WriteAllText(source, "source");
                segment.Configuration.FilesOutput =
                    [new ArtefactCopyMapping { Source = source, Destination = "copied.txt" }];
            }

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                new ArtefactBuilder(new NullLogger()).Build(
                    segment,
                    root.FullName,
                    stagingRoot,
                    moduleName,
                    "1.0.0",
                    null,
                    Array.Empty<RequiredModuleReference>()));

            Assert.Contains("would be removed", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("preserve", File.ReadAllText(marker));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Build_OverlappingDirectoryCopySourceAndDestinationIsRejectedBeforeMutation()
    {
        var root = CreateRoot();
        try
        {
            const string moduleName = "OverlappingMappingModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            WriteScriptModule(stagingRoot, moduleName);
            string outputRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "output")).FullName;
            string source = Directory.CreateDirectory(Path.Combine(root.FullName, "source-assets")).FullName;
            string marker = Path.Combine(source, "asset.txt");
            File.WriteAllText(marker, "preserve");
            ConfigurationArtefactSegment segment = CreateSegment(outputRoot, ArtefactType.Script);
            segment.Configuration.DoNotClear = true;
            segment.Configuration.DestinationDirectoriesRelative = false;
            segment.Configuration.DirectoryOutput =
                [new ArtefactCopyMapping { Source = source, Destination = Path.Combine(source, "nested") }];

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                new ArtefactBuilder(new NullLogger()).Build(
                    segment,
                    root.FullName,
                    stagingRoot,
                    moduleName,
                    "1.0.0",
                    null,
                    Array.Empty<RequiredModuleReference>()));

            Assert.Contains("overlap", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("preserve", File.ReadAllText(marker));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Build_DirectoryCopyDestinationCannotEraseAnotherMappingSource()
    {
        var root = CreateRoot();
        try
        {
            const string moduleName = "CrossMappingOverlapModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            WriteScriptModule(stagingRoot, moduleName);
            string outputRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "output")).FullName;
            string replacementRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "replacement")).FullName;
            string sourceToPreserve = Path.Combine(replacementRoot, "notice.txt");
            File.WriteAllText(sourceToPreserve, "preserve");
            string otherSource = Directory.CreateDirectory(Path.Combine(root.FullName, "other-source")).FullName;
            File.WriteAllText(Path.Combine(otherSource, "other.txt"), "other");
            ConfigurationArtefactSegment segment = CreateSegment(outputRoot, ArtefactType.Script);
            segment.Configuration.DoNotClear = true;
            segment.Configuration.DestinationDirectoriesRelative = false;
            segment.Configuration.DirectoryOutput =
                [new ArtefactCopyMapping { Source = otherSource, Destination = replacementRoot }];
            segment.Configuration.FilesOutput =
                [new ArtefactCopyMapping { Source = sourceToPreserve, Destination = "notice.txt" }];

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                new ArtefactBuilder(new NullLogger()).Build(
                    segment,
                    root.FullName,
                    stagingRoot,
                    moduleName,
                    "1.0.0",
                    null,
                    Array.Empty<RequiredModuleReference>()));

            Assert.Contains("overlaps configured copy source", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("preserve", File.ReadAllText(sourceToPreserve));
        }
        finally
        {
            Delete(root);
        }
    }
}
