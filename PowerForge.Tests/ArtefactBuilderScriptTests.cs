using System.IO.Compression;
using PowerForge;

namespace PowerForge.Tests;

public sealed class ArtefactBuilderScriptTests
{
    [Theory]
    [InlineData(ArtefactType.Script)]
    [InlineData(ArtefactType.ScriptPacked)]
    public void Build_ScriptFinalizerObservesAndFinalizesRewrittenEntryPoint(ArtefactType artefactType)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        string? extractedRoot = null;
        try
        {
            const string moduleName = "DemoModule";
            var stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging"));
            WriteStagingFixture(stagingRoot.FullName, moduleName);
            string outputRoot = Path.Combine(root.FullName, "Artefacts", artefactType.ToString());
            string evidencePath = Path.Combine(root.FullName, "evidence", artefactType + ".json");

            ArtefactBuildResult result = new ArtefactBuilder(new NullLogger()).BuildWithFinalizer(
                new ConfigurationArtefactSegment
                {
                    ArtefactType = artefactType,
                    Configuration = new ArtefactConfiguration
                    {
                        Enabled = true,
                        Path = outputRoot,
                        ScriptName = "Invoke-DemoModule.ps1",
                        ArtefactName = "DemoModule.zip",
                        PostScriptMerge = "Invoke-DemoModule"
                    }
                },
                root.FullName,
                stagingRoot.FullName,
                moduleName,
                "1.0.0",
                null,
                Array.Empty<RequiredModuleReference>(),
                finalizePackedArtefact: context =>
                {
                    Assert.Equal(artefactType, context.ArtefactType);
                    Assert.Equal(string.Empty, context.ManifestPath);
                    Assert.True(File.Exists(context.EntryPointPath));
                    string rewritten = File.ReadAllText(context.EntryPointPath);
                    Assert.DoesNotContain("Export-ModuleMember", rewritten, StringComparison.OrdinalIgnoreCase);
                    Assert.Contains("Invoke-DemoModule", rewritten, StringComparison.Ordinal);
                    File.AppendAllText(context.EntryPointPath, "# finalizer-signature" + Environment.NewLine);
                    Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
                    File.WriteAllText(evidencePath, "{}");
                    return new[] { evidencePath };
                });

            Assert.Equal(new[] { Path.GetFullPath(evidencePath) }, result.EvidencePaths);
            string inspectionRoot = result.OutputPath;
            if (artefactType == ArtefactType.ScriptPacked)
            {
                extractedRoot = Path.Combine(root.FullName, "extracted");
                ZipFile.ExtractToDirectory(result.OutputPath, extractedRoot);
                inspectionRoot = extractedRoot;
            }

            Assert.Contains(
                "# finalizer-signature",
                File.ReadAllText(Path.Combine(inspectionRoot, "Invoke-DemoModule.ps1")),
                StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script)]
    [InlineData(ArtefactType.ScriptPacked)]
    public void Build_RestoresScriptArtefactContract(ArtefactType artefactType)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        string? extractedRoot = null;
        try
        {
            const string moduleName = "DemoModule";
            var stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging"));
            WriteStagingFixture(stagingRoot.FullName, moduleName);
            var outputRoot = Path.Combine(root.FullName, "Artefacts", artefactType.ToString());
            var segment = new ConfigurationArtefactSegment
            {
                ArtefactType = artefactType,
                Configuration = new ArtefactConfiguration
                {
                    Enabled = true,
                    Path = outputRoot,
                    ScriptName = "Invoke-<ModuleName>-<ModuleVersionWithPreRelease>",
                    ArtefactName = "Invoke-<ModuleName>.zip",
                    PreScriptMerge = "param([string] $Mode)",
                    PostScriptMerge = "Invoke-DemoModule -Mode $Mode"
                }
            };

            var result = new ArtefactBuilder(new NullLogger()).Build(
                segment,
                root.FullName,
                stagingRoot.FullName,
                moduleName,
                "1.2.3",
                "preview1",
                Array.Empty<RequiredModuleReference>());

            Assert.Equal(artefactType, result.Type);
            var inspectionRoot = result.OutputPath;
            if (artefactType == ArtefactType.ScriptPacked)
            {
                Assert.Equal("Invoke-DemoModule.zip", Path.GetFileName(result.OutputPath));
                extractedRoot = Path.Combine(root.FullName, "extracted");
                ZipFile.ExtractToDirectory(result.OutputPath, extractedRoot);
                inspectionRoot = extractedRoot;
            }

            var scriptPath = Path.Combine(inspectionRoot, "Invoke-DemoModule-1.2.3-preview1.ps1");
            Assert.True(File.Exists(scriptPath));
            Assert.False(File.Exists(Path.Combine(inspectionRoot, moduleName + ".psd1")));
            Assert.False(File.Exists(Path.Combine(inspectionRoot, moduleName + ".psm1")));
            Assert.True(File.Exists(Path.Combine(inspectionRoot, "Resources", "data.txt")));

            var content = File.ReadAllText(scriptPath);
            Assert.DoesNotContain("Export-ModuleMember", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("content after export", content, StringComparison.Ordinal);
            Assert.True(content.IndexOf("param([string] $Mode)", StringComparison.Ordinal) <
                        content.IndexOf("function Invoke-DemoModule", StringComparison.Ordinal));
            Assert.True(content.IndexOf("function Invoke-DemoModule", StringComparison.Ordinal) <
                        content.IndexOf("Invoke-DemoModule -Mode $Mode", StringComparison.Ordinal));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Build_ScriptUsesModuleNameByDefault()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "DemoModule";
            var stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging"));
            WriteStagingFixture(stagingRoot.FullName, moduleName);
            var outputRoot = Path.Combine(root.FullName, "Artefacts", "Script");

            var result = new ArtefactBuilder(new NullLogger()).Build(
                new ConfigurationArtefactSegment
                {
                    ArtefactType = ArtefactType.Script,
                    Configuration = new ArtefactConfiguration { Enabled = true, Path = outputRoot }
                },
                root.FullName,
                stagingRoot.FullName,
                moduleName,
                "1.0.0",
                null,
                Array.Empty<RequiredModuleReference>());

            Assert.Equal(Path.GetFullPath(outputRoot), result.OutputPath);
            Assert.True(File.Exists(Path.Combine(outputRoot, moduleName + ".ps1")));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Build_ScriptDoNotClearPreservesUnrelatedOutputFiles()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "DemoModule";
            var stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging"));
            WriteStagingFixture(stagingRoot.FullName, moduleName);
            string outputRoot = Path.Combine(root.FullName, "Artefacts", "Script");
            Directory.CreateDirectory(outputRoot);
            string preservedPath = Path.Combine(outputRoot, "keep.txt");
            File.WriteAllText(preservedPath, "preserve me");
            File.WriteAllText(Path.Combine(outputRoot, moduleName + ".ps1"), "stale script");

            _ = new ArtefactBuilder(new NullLogger()).Build(
                new ConfigurationArtefactSegment
                {
                    ArtefactType = ArtefactType.Script,
                    Configuration = new ArtefactConfiguration
                    {
                        Enabled = true,
                        Path = outputRoot,
                        DoNotClear = true
                    }
                },
                root.FullName,
                stagingRoot.FullName,
                moduleName,
                "1.0.0",
                null,
                Array.Empty<RequiredModuleReference>());

            Assert.Equal("preserve me", File.ReadAllText(preservedPath));
            Assert.DoesNotContain("stale script", File.ReadAllText(Path.Combine(outputRoot, moduleName + ".ps1")), StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData(ArtefactType.Packed, "../outside.zip")]
    [InlineData(ArtefactType.Packed, "..\\outside.zip")]
    [InlineData(ArtefactType.ScriptPacked, "folder/outside.zip")]
    [InlineData(ArtefactType.ScriptPacked, "folder\\outside.zip")]
    public void Build_PackedArtefactRejectsArchiveNamesOutsideOutputRoot(
        ArtefactType artefactType,
        string artefactName)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "DemoModule";
            var stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging"));
            WriteStagingFixture(stagingRoot.FullName, moduleName);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                new ArtefactBuilder(new NullLogger()).Build(
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = artefactType,
                        Configuration = new ArtefactConfiguration
                        {
                            Enabled = true,
                            Path = Path.Combine(root.FullName, "Artefacts", artefactType.ToString()),
                            ArtefactName = artefactName
                        }
                    },
                    root.FullName,
                    stagingRoot.FullName,
                    moduleName,
                    "1.0.0",
                    null,
                    Array.Empty<RequiredModuleReference>()));

            Assert.Contains("ArtefactName must be a file name", exception.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(root.FullName, "Artefacts", "outside.zip")));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("..\\outside")]
    [InlineData("folder/script.ps1")]
    public void Build_ScriptRejectsScriptNamesOutsideArtefactRoot(string scriptName)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "DemoModule";
            var stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging"));
            WriteStagingFixture(stagingRoot.FullName, moduleName);

            var exception = Assert.Throws<InvalidOperationException>(() => new ArtefactBuilder(new NullLogger()).Build(
                new ConfigurationArtefactSegment
                {
                    ArtefactType = ArtefactType.Script,
                    Configuration = new ArtefactConfiguration
                    {
                        Enabled = true,
                        Path = Path.Combine(root.FullName, "Artefacts", "Script"),
                        ScriptName = scriptName
                    }
                },
                root.FullName,
                stagingRoot.FullName,
                moduleName,
                "1.0.0",
                null,
                Array.Empty<RequiredModuleReference>()));

            Assert.Contains("ScriptName must be a file name", exception.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(root.FullName, "Artefacts", "outside.ps1")));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static void WriteStagingFixture(string stagingRoot, string moduleName)
    {
        File.WriteAllText(
            Path.Combine(stagingRoot, moduleName + ".psd1"),
            "@{ RootModule = '" + moduleName + ".psm1'; ModuleVersion = '1.0.0'; GUID = '" + Guid.NewGuid() + "' }");
        File.WriteAllText(
            Path.Combine(stagingRoot, moduleName + ".psm1"),
            "# merged module\nfunction Invoke-DemoModule { 'ok' }\n# Export functions and aliases as required\nExport-ModuleMember -Function Invoke-DemoModule\n'content after export'");
        var resources = Directory.CreateDirectory(Path.Combine(stagingRoot, "Resources"));
        File.WriteAllText(Path.Combine(resources.FullName, "data.txt"), "resource");
    }
}
