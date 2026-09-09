using System.IO.Compression;
using System.Management.Automation.Language;
using PowerForge;

namespace PowerForge.Tests;

public sealed class ArtefactBuilderScriptPreambleTests
{
    [Theory]
    [InlineData(ArtefactType.Script)]
    [InlineData(ArtefactType.ScriptPacked)]
    public void Build_InjectedShebangPrecedesPreservedModuleDirectives(ArtefactType artefactType)
    {
        var root = CreateRoot();
        string? extractedRoot = null;
        try
        {
            const string moduleName = "PreambleModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            WriteModule(
                stagingRoot,
                moduleName,
                "#requires -Version 7.0\nusing namespace System\nfunction Get-Preamble { [DateTime]::UtcNow }");
            string outputRoot = Path.Combine(root.FullName, "output");

            ArtefactBuildResult result = Build(
                outputRoot,
                root.FullName,
                stagingRoot,
                moduleName,
                artefactType,
                "#!/usr/bin/env pwsh");

            string inspectionRoot = result.OutputPath;
            if (artefactType == ArtefactType.ScriptPacked)
            {
                extractedRoot = Path.Combine(root.FullName, "extracted");
                ZipFile.ExtractToDirectory(result.OutputPath, extractedRoot);
                inspectionRoot = extractedRoot;
            }

            string scriptPath = Path.Combine(inspectionRoot, moduleName + ".ps1");
            byte[] bytes = File.ReadAllBytes(scriptPath);
            Assert.True(bytes.Length >= 2);
            Assert.Equal((byte)'#', bytes[0]);
            Assert.Equal((byte)'!', bytes[1]);
            string content = File.ReadAllText(scriptPath);
            Assert.StartsWith("#!/usr/bin/env pwsh", content, StringComparison.Ordinal);
            Assert.True(content.IndexOf("#requires -Version 7.0", StringComparison.Ordinal) > 0);
            Assert.True(content.IndexOf("using namespace System", StringComparison.Ordinal) >
                        content.IndexOf("#requires -Version 7.0", StringComparison.Ordinal));
            Parser.ParseInput(content, out _, out ParseError[] parseErrors);
            Assert.Empty(parseErrors);

            if (artefactType == ArtefactType.ScriptPacked)
            {
                using ZipArchive archive = ZipFile.OpenRead(result.OutputPath);
                ZipArchiveEntry entry = Assert.Single(archive.Entries, item => item.FullName == moduleName + ".ps1");
                Assert.Equal(0x1ED, (entry.ExternalAttributes >> 16) & 0x1FF);
            }
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script)]
    [InlineData(ArtefactType.ScriptPacked)]
    public void Build_RootModuleShebangPrecedesGeneratedManifestRequirements(ArtefactType artefactType)
    {
        var root = CreateRoot();
        string? extractedRoot = null;
        try
        {
            const string moduleName = "RootShebangModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            File.WriteAllText(
                Path.Combine(stagingRoot, moduleName + ".psd1"),
                $"@{{ RootModule = '{moduleName}.psm1'; ModuleVersion = '1.0.0'; PowerShellVersion = '7.0' }}");
            File.WriteAllText(
                Path.Combine(stagingRoot, moduleName + ".psm1"),
                "#!/usr/bin/env pwsh\nfunction Get-RootShebang { 'ok' }");

            ArtefactBuildResult result = Build(
                Path.Combine(root.FullName, "output"),
                root.FullName,
                stagingRoot,
                moduleName,
                artefactType,
                string.Empty);

            string inspectionRoot = result.OutputPath;
            if (artefactType == ArtefactType.ScriptPacked)
            {
                extractedRoot = Path.Combine(root.FullName, "extracted-root-shebang");
                ZipFile.ExtractToDirectory(result.OutputPath, extractedRoot);
                inspectionRoot = extractedRoot;
            }

            string scriptPath = Path.Combine(inspectionRoot, moduleName + ".ps1");
            byte[] bytes = File.ReadAllBytes(scriptPath);
            Assert.True(bytes.Length >= 2);
            Assert.Equal((byte)'#', bytes[0]);
            Assert.Equal((byte)'!', bytes[1]);
            string content = File.ReadAllText(scriptPath);
            Assert.StartsWith("#!/usr/bin/env pwsh", content, StringComparison.Ordinal);
            Assert.True(content.IndexOf("#requires -Version 7.0", StringComparison.Ordinal) > 0);
            Parser.ParseInput(content, out _, out ParseError[] parseErrors);
            Assert.Empty(parseErrors);

            if (artefactType == ArtefactType.ScriptPacked)
            {
                using ZipArchive archive = ZipFile.OpenRead(result.OutputPath);
                ZipArchiveEntry entry = Assert.Single(
                    archive.Entries,
                    item => item.FullName == moduleName + ".ps1");
                Assert.Equal(0x1ED, (entry.ExternalAttributes >> 16) & 0x1FF);
            }
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script, "#requires -Version 7.0")]
    [InlineData(ArtefactType.Script, "# premerge comment")]
    [InlineData(ArtefactType.Script, "using namespace System")]
    [InlineData(ArtefactType.ScriptPacked, "#requires -Version 7.0")]
    [InlineData(ArtefactType.ScriptPacked, "# premerge comment")]
    [InlineData(ArtefactType.ScriptPacked, "using namespace System")]
    public void Build_DirectiveOrTriviaOnlyPremergeCanPrecedeModuleParameterBlock(
        ArtefactType artefactType,
        string preScriptMerge)
    {
        var root = CreateRoot();
        string? extractedRoot = null;
        try
        {
            const string moduleName = "ParameterizedModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            WriteModule(
                stagingRoot,
                moduleName,
                "# module help\n[CmdletBinding()]\nparam([string] $Name)\n\"Hello $Name\"");

            ArtefactBuildResult result = Build(
                Path.Combine(root.FullName, "output"),
                root.FullName,
                stagingRoot,
                moduleName,
                artefactType,
                preScriptMerge);

            string inspectionRoot = result.OutputPath;
            if (artefactType == ArtefactType.ScriptPacked)
            {
                extractedRoot = Path.Combine(root.FullName, "extracted");
                ZipFile.ExtractToDirectory(result.OutputPath, extractedRoot);
                inspectionRoot = extractedRoot;
            }

            string content = File.ReadAllText(Path.Combine(inspectionRoot, moduleName + ".ps1"));
            Assert.Contains(preScriptMerge, content, StringComparison.Ordinal);
            Assert.True(content.IndexOf(preScriptMerge, StringComparison.Ordinal) <
                        content.IndexOf("[CmdletBinding()]", StringComparison.Ordinal));
            Parser.ParseInput(content, out _, out ParseError[] parseErrors);
            Assert.Empty(parseErrors);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    private static ArtefactBuildResult Build(
        string outputRoot,
        string projectRoot,
        string stagingRoot,
        string moduleName,
        ArtefactType artefactType,
        string preScriptMerge) =>
        new ArtefactBuilder(new NullLogger()).Build(
            new ConfigurationArtefactSegment
            {
                ArtefactType = artefactType,
                Configuration = new ArtefactConfiguration
                {
                    Enabled = true,
                    Path = outputRoot,
                    ArtefactName = moduleName + ".zip",
                    PreScriptMerge = preScriptMerge
                }
            },
            projectRoot,
            stagingRoot,
            moduleName,
            "1.0.0",
            null,
            Array.Empty<RequiredModuleReference>());

    private static void WriteModule(string stagingRoot, string moduleName, string moduleContent)
    {
        File.WriteAllText(
            Path.Combine(stagingRoot, moduleName + ".psd1"),
            $"@{{ RootModule = '{moduleName}.psm1'; ModuleVersion = '1.0.0' }}");
        File.WriteAllText(Path.Combine(stagingRoot, moduleName + ".psm1"), moduleContent);
    }

    private static DirectoryInfo CreateRoot() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
}
