using System.IO.Compression;
using System.Text;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class ArtefactBuilderScriptClosureTests
{
    [Theory]
    [InlineData(ArtefactType.Script)]
    [InlineData(ArtefactType.ScriptPacked)]
    public void Build_DllOnlyModuleIsRejectedBeforeExistingOutputIsChanged(ArtefactType artefactType)
    {
        var root = CreateRoot();
        try
        {
            const string moduleName = "CompiledModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            File.WriteAllText(
                Path.Combine(stagingRoot, moduleName + ".psd1"),
                "@{ RootModule = 'CompiledModule.dll'; ModuleVersion = '1.0.0' }");
            File.WriteAllBytes(Path.Combine(stagingRoot, moduleName + ".dll"), new byte[] { 1, 2, 3 });

            string outputRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "output")).FullName;
            string marker = Path.Combine(outputRoot, "existing.txt");
            File.WriteAllText(marker, "preserve");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => Build(
                root.FullName,
                stagingRoot,
                outputRoot,
                moduleName,
                artefactType));

            Assert.Contains("script root module", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(marker));
            Assert.Equal("preserve", File.ReadAllText(marker));
        }
        finally
        {
            Delete(root);
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script)]
    [InlineData(ArtefactType.ScriptPacked)]
    public void Build_EquivalentRelativeScriptRootModuleIsAccepted(ArtefactType artefactType)
    {
        var root = CreateRoot();
        try
        {
            const string moduleName = "RelativeModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            File.WriteAllText(
                Path.Combine(stagingRoot, moduleName + ".psd1"),
                "@{ RootModule = '.\\RelativeModule.psm1'; ModuleVersion = '1.0.0' }");
            File.WriteAllText(Path.Combine(stagingRoot, moduleName + ".psm1"), "'ok'");

            ArtefactBuildResult result = Build(
                root.FullName,
                stagingRoot,
                Path.Combine(root.FullName, "output"),
                moduleName,
                artefactType);

            Assert.True(artefactType == ArtefactType.Script
                ? File.Exists(Path.Combine(result.OutputPath, moduleName + ".ps1"))
                : File.Exists(result.OutputPath));
        }
        finally
        {
            Delete(root);
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script)]
    [InlineData(ArtefactType.ScriptPacked)]
    public void Build_ManifestLoadedContentIsRejectedBeforeExistingOutputIsChanged(ArtefactType artefactType)
    {
        var root = CreateRoot();
        try
        {
            const string moduleName = "ManifestModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            File.WriteAllText(
                Path.Combine(stagingRoot, moduleName + ".psd1"),
                "@{ RootModule = 'ManifestModule.psm1'; ModuleVersion = '1.0.0'; " +
                "NestedModules = @('Nested.psm1'); RequiredAssemblies = @('Support.dll'); " +
                "RequiredModules = @('RequiredModule'); " +
                "ScriptsToProcess = @('Initialize.ps1'); TypesToProcess = @('Types.ps1xml'); " +
                "FormatsToProcess = @('Formats.ps1xml') }");
            File.WriteAllText(Path.Combine(stagingRoot, moduleName + ".psm1"), "'root'");

            string outputRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "output")).FullName;
            string marker = Path.Combine(outputRoot, "existing.txt");
            File.WriteAllText(marker, "preserve");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => Build(
                root.FullName,
                stagingRoot,
                outputRoot,
                moduleName,
                artefactType));

            Assert.Contains("manifest-loaded content", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("NestedModules", exception.Message, StringComparison.Ordinal);
            Assert.Contains("RequiredAssemblies", exception.Message, StringComparison.Ordinal);
            Assert.Contains("RequiredModules", exception.Message, StringComparison.Ordinal);
            Assert.Contains("ScriptsToProcess", exception.Message, StringComparison.Ordinal);
            Assert.Contains("TypesToProcess", exception.Message, StringComparison.Ordinal);
            Assert.Contains("FormatsToProcess", exception.Message, StringComparison.Ordinal);
            Assert.True(File.Exists(marker));
        }
        finally
        {
            Delete(root);
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script)]
    [InlineData(ArtefactType.ScriptPacked)]
    public void Build_TransformedScriptOmitsStaleCompilationEvidence(ArtefactType artefactType)
    {
        var root = CreateRoot();
        string? extractionRoot = null;
        try
        {
            const string moduleName = "HybridModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            string manifest = Path.Combine(stagingRoot, moduleName + ".psd1");
            string scriptModule = Path.Combine(stagingRoot, moduleName + ".psm1");
            string assembly = Path.Combine(stagingRoot, moduleName + ".dll");
            string compilationEvidence = Path.Combine(stagingRoot, moduleName + ".powerforge-compilation.json");
            string authenticatedEvidence = Path.Combine(stagingRoot, moduleName + ".powerforge-compilation.p7s");
            File.WriteAllText(manifest, "@{ RootModule = 'HybridModule.psm1'; ModuleVersion = '1.0.0' }");
            File.WriteAllText(scriptModule, "function Invoke-HybridModule { 'ok' }");
            File.WriteAllBytes(assembly, new byte[] { 1, 2, 3 });
            File.WriteAllText(compilationEvidence, "{\"files\":[\"HybridModule.psm1\"]}");
            File.WriteAllBytes(authenticatedEvidence, new byte[] { 4, 5, 6 });

            ArtefactBuildResult result = Build(
                root.FullName,
                stagingRoot,
                Path.Combine(root.FullName, "output"),
                moduleName,
                artefactType,
                finalizedPayloadFiles: new[] { manifest, scriptModule, assembly, compilationEvidence, authenticatedEvidence });

            string inspectionRoot = result.OutputPath;
            if (artefactType == ArtefactType.ScriptPacked)
            {
                extractionRoot = Path.Combine(root.FullName, "extracted");
                ZipFile.ExtractToDirectory(result.OutputPath, extractionRoot);
                inspectionRoot = extractionRoot;
            }

            Assert.True(File.Exists(Path.Combine(inspectionRoot, moduleName + ".ps1")));
            Assert.True(File.Exists(Path.Combine(inspectionRoot, moduleName + ".dll")));
            Assert.False(File.Exists(Path.Combine(inspectionRoot, moduleName + ".powerforge-compilation.json")));
            Assert.False(File.Exists(Path.Combine(inspectionRoot, moduleName + ".powerforge-compilation.p7s")));
        }
        finally
        {
            Delete(root);
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script, true)]
    [InlineData(ArtefactType.Script, false)]
    [InlineData(ArtefactType.ScriptPacked, true)]
    [InlineData(ArtefactType.ScriptPacked, false)]
    public void Build_AuthoritativePayloadMustContainScriptInputsBeforeOutputChanges(
        ArtefactType artefactType,
        bool omitManifest)
    {
        var root = CreateRoot();
        try
        {
            const string moduleName = "SelectedPayloadModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            string manifest = Path.Combine(stagingRoot, moduleName + ".psd1");
            string scriptModule = Path.Combine(stagingRoot, moduleName + ".psm1");
            File.WriteAllText(manifest, "@{ RootModule = 'SelectedPayloadModule.psm1'; ModuleVersion = '1.0.0' }");
            File.WriteAllText(scriptModule, "function Invoke-SelectedPayloadModule { 'ok' }");

            string outputRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "output")).FullName;
            string marker = Path.Combine(outputRoot, "existing.txt");
            string staleRootModule = Path.Combine(outputRoot, moduleName + ".psm1");
            File.WriteAllText(marker, "preserve");
            File.WriteAllText(staleRootModule, "stale-output-must-not-be-used");
            ConfigurationArtefactSegment segment = CreateSegment(outputRoot, artefactType);
            segment.Configuration.DoNotClear = true;
            string[] selectedPayload = omitManifest ? new[] { scriptModule } : new[] { manifest };

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                new ArtefactBuilder(new NullLogger()).BuildWithFinalizer(
                    segment,
                    root.FullName,
                    stagingRoot,
                    moduleName,
                    "1.0.0",
                    null,
                    Array.Empty<RequiredModuleReference>(),
                    finalizePackedArtefact: null,
                    information: null,
                    delivery: null,
                    includeScriptFolders: true,
                    finalizedPayloadFiles: selectedPayload));

            Assert.Contains("finalized module payload", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(omitManifest ? ".psd1" : ".psm1", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(marker));
            Assert.Equal("preserve", File.ReadAllText(marker));
            Assert.Equal("stale-output-must-not-be-used", File.ReadAllText(staleRootModule));
            Assert.False(File.Exists(Path.Combine(outputRoot, moduleName + ".ps1")));
        }
        finally
        {
            Delete(root);
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script, true)]
    [InlineData(ArtefactType.Script, false)]
    [InlineData(ArtefactType.ScriptPacked, true)]
    [InlineData(ArtefactType.ScriptPacked, false)]
    public void Build_PackageSelectionMustContainScriptInputsBeforeOutputChanges(
        ArtefactType artefactType,
        bool omitManifest)
    {
        var root = CreateRoot();
        try
        {
            const string moduleName = "FilteredPayloadModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            WriteScriptModule(stagingRoot, moduleName);

            string outputRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "output")).FullName;
            string marker = Path.Combine(outputRoot, "existing.txt");
            string staleRootModule = Path.Combine(outputRoot, moduleName + ".psm1");
            File.WriteAllText(marker, "preserve");
            File.WriteAllText(staleRootModule, "stale-output-must-not-be-used");
            ConfigurationArtefactSegment segment = CreateSegment(outputRoot, artefactType);
            segment.Configuration.DoNotClear = true;
            var information = new InformationConfiguration
            {
                IncludeRoot = omitManifest ? ["*.psm1"] : ["*.psd1"]
            };

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                new ArtefactBuilder(new NullLogger()).BuildWithFinalizer(
                    segment,
                    root.FullName,
                    stagingRoot,
                    moduleName,
                    "1.0.0",
                    null,
                    Array.Empty<RequiredModuleReference>(),
                    finalizePackedArtefact: null,
                    information,
                    delivery: null,
                    includeScriptFolders: true,
                    finalizedPayloadFiles: null));

            Assert.Contains("module package selection", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(omitManifest ? ".psd1" : ".psm1", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(marker));
            Assert.Equal("preserve", File.ReadAllText(marker));
            Assert.Equal("stale-output-must-not-be-used", File.ReadAllText(staleRootModule));
            Assert.False(File.Exists(Path.Combine(outputRoot, moduleName + ".ps1")));
        }
        finally
        {
            Delete(root);
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script)]
    [InlineData(ArtefactType.ScriptPacked)]
    public void Build_ExistingModuleParamBlockRejectsPreScriptInjectionBeforeOutputChanges(ArtefactType artefactType)
    {
        var root = CreateRoot();
        try
        {
            const string moduleName = "ParameterizedModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            File.WriteAllText(
                Path.Combine(stagingRoot, moduleName + ".psd1"),
                "@{ RootModule = 'ParameterizedModule.psm1'; ModuleVersion = '1.0.0' }");
            File.WriteAllText(
                Path.Combine(stagingRoot, moduleName + ".psm1"),
                "# module help\n[CmdletBinding()]\nparam([string] $Name)\n\"Hello $Name\"");

            string outputRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "output")).FullName;
            string marker = Path.Combine(outputRoot, "existing.txt");
            File.WriteAllText(marker, "preserve");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => Build(
                root.FullName,
                stagingRoot,
                outputRoot,
                moduleName,
                artefactType,
                preScriptMerge: "$script:Initialized = $true"));

            Assert.Contains("parameter block", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("PreScriptMerge", exception.Message, StringComparison.Ordinal);
            Assert.True(File.Exists(marker));
        }
        finally
        {
            Delete(root);
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script)]
    [InlineData(ArtefactType.ScriptPacked)]
    public void Build_DirectoryMappingCannotEraseGeneratedScriptRoot(ArtefactType artefactType)
    {
        var root = CreateRoot();
        try
        {
            const string moduleName = "MappedModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            WriteScriptModule(stagingRoot, moduleName);
            string mappingSource = Directory.CreateDirectory(Path.Combine(root.FullName, "mapping-source")).FullName;
            File.WriteAllText(Path.Combine(mappingSource, "mapped.txt"), "mapped");

            string outputRoot = Path.Combine(root.FullName, "output");
            ConfigurationArtefactSegment segment = CreateSegment(outputRoot, artefactType);
            segment.Configuration.RequiredModules.ModulesPath = "app";
            segment.Configuration.DirectoryOutput = new[]
            {
                new ArtefactCopyMapping { Source = mappingSource, Destination = "." }
            };

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => new ArtefactBuilder(new NullLogger()).Build(
                segment,
                root.FullName,
                stagingRoot,
                moduleName,
                "1.0.0",
                null,
                Array.Empty<RequiredModuleReference>()));

            Assert.Contains("directory copy destination", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("script root", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Delete(root);
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script)]
    [InlineData(ArtefactType.ScriptPacked)]
    public void Build_FileMappingCannotOverwriteGeneratedEntryPoint(ArtefactType artefactType)
    {
        var root = CreateRoot();
        try
        {
            const string moduleName = "MappedModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            WriteScriptModule(stagingRoot, moduleName);
            string replacement = Path.Combine(root.FullName, "replacement.ps1");
            File.WriteAllText(replacement, "'replacement'");

            string outputRoot = Path.Combine(root.FullName, "output");
            ConfigurationArtefactSegment segment = CreateSegment(outputRoot, artefactType);
            segment.Configuration.FilesOutput = new[]
            {
                new ArtefactCopyMapping { Source = replacement, Destination = moduleName + ".ps1" }
            };

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => new ArtefactBuilder(new NullLogger()).Build(
                segment,
                root.FullName,
                stagingRoot,
                moduleName,
                "1.0.0",
                null,
                Array.Empty<RequiredModuleReference>()));

            Assert.Contains("file copy destination", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("entry point", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Delete(root);
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script, false, "deps")]
    [InlineData(ArtefactType.Script, false, "deps/Dependency")]
    [InlineData(ArtefactType.Script, false, "deps/Dependency/content")]
    [InlineData(ArtefactType.Script, true, "deps/Dependency/replacement.psm1")]
    [InlineData(ArtefactType.ScriptPacked, false, "deps")]
    [InlineData(ArtefactType.ScriptPacked, false, "deps/Dependency")]
    [InlineData(ArtefactType.ScriptPacked, false, "deps/Dependency/content")]
    [InlineData(ArtefactType.ScriptPacked, true, "deps/Dependency/replacement.psm1")]
    public void Build_CopyMappingCannotOverlapBundledRequiredModule(
        ArtefactType artefactType,
        bool fileMapping,
        string mappingDestination)
    {
        var root = CreateRoot();
        try
        {
            const string moduleName = "MappedRequiredModule";
            const string dependencyName = "Dependency";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            WriteScriptModule(stagingRoot, moduleName);
            string outputRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "output")).FullName;
            string marker = Path.Combine(outputRoot, "preserve.txt");
            File.WriteAllText(marker, "preserve");
            ConfigurationArtefactSegment segment = CreateSegment(outputRoot, artefactType);
            segment.Configuration.RequiredModules = new ArtefactRequiredModulesConfiguration
            {
                Enabled = true,
                Path = "deps",
                ModulesPath = "app"
            };

            if (fileMapping)
            {
                string source = Path.Combine(root.FullName, "replacement.psm1");
                File.WriteAllText(source, "'replacement'");
                segment.Configuration.FilesOutput =
                [
                    new ArtefactCopyMapping { Source = source, Destination = mappingDestination }
                ];
            }
            else
            {
                string source = Directory.CreateDirectory(Path.Combine(root.FullName, "mapping-source")).FullName;
                File.WriteAllText(Path.Combine(source, "replacement.psm1"), "'replacement'");
                segment.Configuration.DirectoryOutput =
                [
                    new ArtefactCopyMapping { Source = source, Destination = mappingDestination }
                ];
            }

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                new ArtefactBuilder(new NullLogger()).Build(
                    segment,
                    root.FullName,
                    stagingRoot,
                    moduleName,
                    "1.0.0",
                    null,
                    [new RequiredModuleReference(dependencyName)]));

            Assert.Contains("required module destination", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("preserve", File.ReadAllText(marker));
        }
        finally
        {
            Delete(root);
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script)]
    [InlineData(ArtefactType.ScriptPacked)]
    public void Build_RemovesExportCommandsEmbeddedInCompoundStatements(ArtefactType artefactType)
    {
        var root = CreateRoot();
        string? extractionRoot = null;
        try
        {
            const string moduleName = "CompoundExportModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            File.WriteAllText(
                Path.Combine(stagingRoot, moduleName + ".psd1"),
                "@{ RootModule = 'CompoundExportModule.psm1'; ModuleVersion = '1.0.0' }");
            File.WriteAllText(
                Path.Combine(stagingRoot, moduleName + ".psm1"),
                "$script:Enabled = $true; Export-ModuleMember -Function Invoke-CompoundExportModule\n" +
                "if ($script:Enabled) { Microsoft.PowerShell.Core\\Export-ModuleMember -Function Invoke-CompoundExportModule }\n" +
                "function Invoke-CompoundExportModule { 'ok' }");

            ArtefactBuildResult result = Build(
                root.FullName,
                stagingRoot,
                Path.Combine(root.FullName, "output"),
                moduleName,
                artefactType);

            string inspectionRoot = result.OutputPath;
            if (artefactType == ArtefactType.ScriptPacked)
            {
                extractionRoot = Path.Combine(root.FullName, "extracted");
                ZipFile.ExtractToDirectory(result.OutputPath, extractionRoot);
                inspectionRoot = extractionRoot;
            }

            string script = File.ReadAllText(Path.Combine(inspectionRoot, moduleName + ".ps1"));
            Assert.DoesNotContain("Export-ModuleMember", script, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("$script:Enabled = $true;", script, StringComparison.Ordinal);
            Assert.Contains("if ($script:Enabled) { }", script, StringComparison.Ordinal);
            Assert.Contains("function Invoke-CompoundExportModule", script, StringComparison.Ordinal);
        }
        finally
        {
            Delete(root);
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script)]
    [InlineData(ArtefactType.ScriptPacked)]
    public void Build_PreservesLegacyWindowsEncodedScriptText(ArtefactType artefactType)
    {
        var root = CreateRoot();
        string? extractionRoot = null;
        try
        {
            const string moduleName = "LegacyEncodingModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            File.WriteAllText(
                Path.Combine(stagingRoot, moduleName + ".psd1"),
                "@{ RootModule = 'LegacyEncodingModule.psm1'; ModuleVersion = '1.0.0' }");
            byte[] prefix = Encoding.ASCII.GetBytes("function Get-LegacyText { 'caf");
            byte[] suffix = Encoding.ASCII.GetBytes("' }");
            File.WriteAllBytes(
                Path.Combine(stagingRoot, moduleName + ".psm1"),
                prefix.Concat(new byte[] { 0xE9 }).Concat(suffix).ToArray());

            ArtefactBuildResult result = Build(
                root.FullName,
                stagingRoot,
                Path.Combine(root.FullName, "output"),
                moduleName,
                artefactType);

            string inspectionRoot = result.OutputPath;
            if (artefactType == ArtefactType.ScriptPacked)
            {
                extractionRoot = Path.Combine(root.FullName, "extracted");
                ZipFile.ExtractToDirectory(result.OutputPath, extractionRoot);
                inspectionRoot = extractionRoot;
            }

            string script = File.ReadAllText(Path.Combine(inspectionRoot, moduleName + ".ps1"), Encoding.UTF8);
            Assert.Contains("café", script, StringComparison.Ordinal);
            Assert.DoesNotContain('\uFFFD', script);
        }
        finally
        {
            Delete(root);
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script)]
    [InlineData(ArtefactType.ScriptPacked)]
    public void Build_GeneratedExportMarkerInsideHereStringDoesNotTruncateScript(ArtefactType artefactType)
    {
        var root = CreateRoot();
        string? extractionRoot = null;
        try
        {
            const string moduleName = "LiteralMarkerModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            File.WriteAllText(Path.Combine(stagingRoot, moduleName + ".psd1"),
                "@{ RootModule = 'LiteralMarkerModule.psm1'; ModuleVersion = '1.0.0' }");
            File.WriteAllText(Path.Combine(stagingRoot, moduleName + ".psm1"),
                "$literal = @'\n" +
                "# Export functions and aliases as required\n" +
                "'@\n" +
                "function Get-LiteralMarker { $literal }\n" +
                "Export-ModuleMember -Function Get-LiteralMarker\n" +
                "$script:Tail = 'preserved'");

            ArtefactBuildResult result = Build(root.FullName, stagingRoot, Path.Combine(root.FullName, "output"), moduleName, artefactType);
            string inspectionRoot = result.OutputPath;
            if (artefactType == ArtefactType.ScriptPacked)
            {
                extractionRoot = Path.Combine(root.FullName, "extracted");
                ZipFile.ExtractToDirectory(result.OutputPath, extractionRoot);
                inspectionRoot = extractionRoot;
            }

            string script = File.ReadAllText(Path.Combine(inspectionRoot, moduleName + ".ps1"));
            Assert.Contains("# Export functions and aliases as required", script, StringComparison.Ordinal);
            Assert.Contains("$script:Tail = 'preserved'", script, StringComparison.Ordinal);
            Assert.DoesNotContain("Export-ModuleMember -Function Get-LiteralMarker", script, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Delete(root);
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script)]
    [InlineData(ArtefactType.ScriptPacked)]
    public void Build_HandWrittenExportMarkerDoesNotTruncateFollowingCode(ArtefactType artefactType)
    {
        var root = CreateRoot();
        string? extractionRoot = null;
        try
        {
            const string moduleName = "HandWrittenMarkerModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            File.WriteAllText(Path.Combine(stagingRoot, moduleName + ".psd1"),
                "@{ RootModule = 'HandWrittenMarkerModule.psm1'; ModuleVersion = '1.0.0' }");
            File.WriteAllText(Path.Combine(stagingRoot, moduleName + ".psm1"),
                "function Get-HandWrittenMarker { 'ok' }\n" +
                "# Export functions and aliases as required\n" +
                "Export-ModuleMember -Function Get-HandWrittenMarker\n" +
                "# Auto-generated by PowerForge. Do not edit.\n" +
                "$script:Tail = 'preserved'\n");

            ArtefactBuildResult result = Build(
                root.FullName,
                stagingRoot,
                Path.Combine(root.FullName, "output"),
                moduleName,
                artefactType);
            string inspectionRoot = result.OutputPath;
            if (artefactType == ArtefactType.ScriptPacked)
            {
                extractionRoot = Path.Combine(root.FullName, "extracted-hand-written-marker");
                ZipFile.ExtractToDirectory(result.OutputPath, extractionRoot);
                inspectionRoot = extractionRoot;
            }

            string script = File.ReadAllText(Path.Combine(inspectionRoot, moduleName + ".ps1"));
            Assert.Contains("# Export functions and aliases as required", script, StringComparison.Ordinal);
            Assert.Contains("$script:Tail = 'preserved'", script, StringComparison.Ordinal);
            Assert.DoesNotContain("Export-ModuleMember", script, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Delete(root);
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script, "& Export-ModuleMember")]
    [InlineData(ArtefactType.Script, ". Microsoft.PowerShell.Core\\Export-ModuleMember")]
    [InlineData(ArtefactType.ScriptPacked, "& Export-ModuleMember")]
    [InlineData(ArtefactType.ScriptPacked, ". Microsoft.PowerShell.Core\\Export-ModuleMember")]
    public void Build_RemovesCallOperatorExportInvocations(ArtefactType artefactType, string invocation)
    {
        var root = CreateRoot();
        string? extractionRoot = null;
        try
        {
            const string moduleName = "CallOperatorModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            File.WriteAllText(Path.Combine(stagingRoot, moduleName + ".psd1"),
                "@{ RootModule = 'CallOperatorModule.psm1'; ModuleVersion = '1.0.0' }");
            File.WriteAllText(Path.Combine(stagingRoot, moduleName + ".psm1"),
                "function Get-CallOperator { 'ok' }\n" +
                invocation + " -Function Get-CallOperator; $script:Tail = 'preserved'\n");

            ArtefactBuildResult result = Build(root.FullName, stagingRoot, Path.Combine(root.FullName, "output"), moduleName, artefactType);
            string inspectionRoot = result.OutputPath;
            if (artefactType == ArtefactType.ScriptPacked)
            {
                extractionRoot = Path.Combine(root.FullName, "extracted");
                ZipFile.ExtractToDirectory(result.OutputPath, extractionRoot);
                inspectionRoot = extractionRoot;
            }

            string script = File.ReadAllText(Path.Combine(inspectionRoot, moduleName + ".ps1"));
            Assert.DoesNotContain("Export-ModuleMember", script, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("$script:Tail = 'preserved'", script, StringComparison.Ordinal);
            System.Management.Automation.Language.Parser.ParseInput(script, out _, out var parseErrors);
            Assert.Empty(parseErrors);
        }
        finally
        {
            Delete(root);
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script)]
    [InlineData(ArtefactType.ScriptPacked)]
    public void Build_RequiresManifestToSelectExpectedScriptRootModule(ArtefactType artefactType)
    {
        var root = CreateRoot();
        try
        {
            const string moduleName = "EmptyRootModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            File.WriteAllText(Path.Combine(stagingRoot, moduleName + ".psd1"), "@{ ModuleVersion = '1.0.0' }");
            File.WriteAllText(Path.Combine(stagingRoot, moduleName + ".psm1"), "function Get-Value { 'stale' }");
            string outputRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "output")).FullName;
            string marker = Path.Combine(outputRoot, "preserve.txt");
            File.WriteAllText(marker, "preserve");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                Build(root.FullName, stagingRoot, outputRoot, moduleName, artefactType));

            Assert.Contains("manifest to select", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(marker));
        }
        finally
        {
            Delete(root);
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script, "Dependency")]
    [InlineData(ArtefactType.Script, "Dependency/main")]
    [InlineData(ArtefactType.ScriptPacked, "Dependency")]
    [InlineData(ArtefactType.ScriptPacked, "Dependency/main")]
    public void Build_RequiredModuleDestinationCannotContainGeneratedScriptRoot(
        ArtefactType artefactType,
        string modulesPath)
    {
        var root = CreateRoot();
        try
        {
            const string moduleName = "RequiredCollisionModule";
            const string dependencyName = "Dependency";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            WriteScriptModule(stagingRoot, moduleName);
            string outputRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "output")).FullName;
            string marker = Path.Combine(outputRoot, "preserve.txt");
            File.WriteAllText(marker, "preserve");
            ConfigurationArtefactSegment segment = CreateSegment(outputRoot, artefactType);
            segment.Configuration.RequiredModules = new ArtefactRequiredModulesConfiguration
            {
                Enabled = true,
                ModulesPath = modulesPath
            };

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                new ArtefactBuilder(new NullLogger()).Build(
                    segment,
                    root.FullName,
                    stagingRoot,
                    moduleName,
                    "1.0.0",
                    null,
                    new[] { new RequiredModuleReference(dependencyName) }));

            Assert.Contains("required module destination", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("script root", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(marker));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Build_ScriptPackedRejectsRelativeDirectoryMappingTraversalBeforeCopy()
    {
        var root = CreateRoot();
        string uniqueName = "PowerForgeTraversal_" + Guid.NewGuid().ToString("N");
        string victim = Path.Combine(Path.GetTempPath(), "PowerForge", "artefacts", uniqueName);
        try
        {
            const string moduleName = "TraversalModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            WriteScriptModule(stagingRoot, moduleName);
            string source = Directory.CreateDirectory(Path.Combine(root.FullName, "mapping-source")).FullName;
            File.WriteAllText(Path.Combine(source, "replacement.txt"), "replacement");
            Directory.CreateDirectory(victim);
            string marker = Path.Combine(victim, "preserve.txt");
            File.WriteAllText(marker, "preserve");
            ConfigurationArtefactSegment segment = CreateSegment(Path.Combine(root.FullName, "output"), ArtefactType.ScriptPacked);
            segment.Configuration.DestinationDirectoriesRelative = true;
            segment.Configuration.DirectoryOutput = new[]
            {
                new ArtefactCopyMapping { Source = source, Destination = "../" + uniqueName }
            };

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                new ArtefactBuilder(new NullLogger()).Build(
                    segment,
                    root.FullName,
                    stagingRoot,
                    moduleName,
                    "1.0.0",
                    null,
                    Array.Empty<RequiredModuleReference>()));

            Assert.Contains("outside the temporary packed artefact root", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("preserve", File.ReadAllText(marker));
        }
        finally
        {
            Delete(root);
            try { Directory.Delete(victim, recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script)]
    [InlineData(ArtefactType.ScriptPacked)]
    public void Build_ScriptNameCannotReplacePackagedPayloadBeforeOutputChanges(ArtefactType artefactType)
    {
        var root = CreateRoot();
        try
        {
            const string moduleName = "CollisionModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            WriteScriptModule(stagingRoot, moduleName);
            string collidingName = moduleName + ".Libraries.ps1";
            File.WriteAllText(Path.Combine(stagingRoot, collidingName), "'library loader'");
            string outputRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "output")).FullName;
            string marker = Path.Combine(outputRoot, "existing.txt");
            File.WriteAllText(marker, "preserve");
            ConfigurationArtefactSegment segment = CreateSegment(outputRoot, artefactType);
            segment.Configuration.ScriptName = collidingName;

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                new ArtefactBuilder(new NullLogger()).Build(
                    segment,
                    root.FullName,
                    stagingRoot,
                    moduleName,
                    "1.0.0",
                    null,
                    Array.Empty<RequiredModuleReference>()));

            Assert.Contains("ScriptName", exception.Message, StringComparison.Ordinal);
            Assert.Contains("packaged payload", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(marker));
            Assert.Equal("preserve", File.ReadAllText(marker));
        }
        finally
        {
            Delete(root);
        }
    }

    private static ArtefactBuildResult Build(
        string projectRoot,
        string stagingRoot,
        string outputRoot,
        string moduleName,
        ArtefactType artefactType,
        string? preScriptMerge = null,
        IReadOnlyList<string>? finalizedPayloadFiles = null)
        => new ArtefactBuilder(new NullLogger()).BuildWithFinalizer(
            CreateSegment(outputRoot, artefactType, preScriptMerge),
            projectRoot,
            stagingRoot,
            moduleName,
            "1.0.0",
            null,
            Array.Empty<RequiredModuleReference>(),
            finalizePackedArtefact: null,
            information: null,
            delivery: null,
            includeScriptFolders: true,
            finalizedPayloadFiles);

    private static ConfigurationArtefactSegment CreateSegment(
        string outputRoot,
        ArtefactType artefactType,
        string? preScriptMerge = null)
        => new()
        {
            ArtefactType = artefactType,
            Configuration = new ArtefactConfiguration
            {
                Enabled = true,
                Path = outputRoot,
                ArtefactName = "MappedModule.zip",
                PreScriptMerge = preScriptMerge
            }
        };

    private static void WriteScriptModule(string stagingRoot, string moduleName)
    {
        File.WriteAllText(
            Path.Combine(stagingRoot, moduleName + ".psd1"),
            "@{ RootModule = '" + moduleName + ".psm1'; ModuleVersion = '1.0.0' }");
        File.WriteAllText(
            Path.Combine(stagingRoot, moduleName + ".psm1"),
            "function Invoke-" + moduleName + " { 'ok' }");
    }

    private static DirectoryInfo CreateRoot()
        => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));

    private static void Delete(DirectoryInfo root)
    {
        try { root.Delete(recursive: true); } catch { /* best effort */ }
    }
}
