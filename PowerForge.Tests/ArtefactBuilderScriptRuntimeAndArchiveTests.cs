using System.IO.Compression;
using System.Management.Automation.Language;

namespace PowerForge.Tests;

public sealed class ArtefactBuilderScriptRuntimeAndArchiveTests
{
    [Theory]
    [InlineData(ArtefactType.Script)]
    [InlineData(ArtefactType.ScriptPacked)]
    public void Build_PreservesManifestPowerShellRuntimeRequirements(ArtefactType artefactType)
    {
        var root = CreateRoot();
        string? extractionRoot = null;
        try
        {
            const string moduleName = "RuntimeModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            File.WriteAllText(
                Path.Combine(stagingRoot, moduleName + ".psd1"),
                "@{ RootModule = 'RuntimeModule.psm1'; ModuleVersion = '1.0.0'; PowerShellVersion = '7.0'; CompatiblePSEditions = @('Core'); ProcessorArchitecture = 'None' }");
            File.WriteAllText(
                Path.Combine(stagingRoot, moduleName + ".psm1"),
                "using namespace System\nfunction Get-Value { [DateTime]::UtcNow }");

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

            string content = File.ReadAllText(Path.Combine(inspectionRoot, moduleName + ".ps1"));
            int versionRequirement = content.IndexOf("#requires -Version 7.0", StringComparison.Ordinal);
            int editionRequirement = content.IndexOf("#requires -PSEdition Core", StringComparison.Ordinal);
            int usingStatement = content.IndexOf("using namespace System", StringComparison.Ordinal);
            Assert.True(versionRequirement >= 0);
            Assert.True(editionRequirement > versionRequirement);
            Assert.True(usingStatement > editionRequirement);
            Parser.ParseInput(content, out _, out ParseError[] errors);
            Assert.Empty(errors);
        }
        finally
        {
            TryDelete(root.FullName);
            TryDelete(extractionRoot);
        }
    }

    [Theory]
    [InlineData("ProcessorArchitecture = 'Amd64'")]
    [InlineData("DotNetFrameworkVersion = '4.8'")]
    [InlineData("PowerShellHostName = 'ConsoleHost'")]
    [InlineData("PowerShellHostVersion = '5.1'")]
    [InlineData("CLRVersion = '4.0'")]
    [InlineData("CompatiblePSEditions = @('Core', 'Future')")]
    public void Build_RejectsUnrepresentableManifestRuntimeRequirementBeforeOutputMutation(
        string manifestRequirement)
    {
        var root = CreateRoot();
        try
        {
            const string moduleName = "RuntimeModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            File.WriteAllText(
                Path.Combine(stagingRoot, moduleName + ".psd1"),
                $"@{{ RootModule = 'RuntimeModule.psm1'; ModuleVersion = '1.0.0'; {manifestRequirement} }}");
            File.WriteAllText(Path.Combine(stagingRoot, moduleName + ".psm1"), "function Get-Value { 1 }");
            string outputRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "output")).FullName;
            string marker = Path.Combine(outputRoot, "preserve.txt");
            File.WriteAllText(marker, "preserve");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => Build(
                root.FullName,
                stagingRoot,
                outputRoot,
                moduleName,
                ArtefactType.Script));

            Assert.Contains("cannot preserve", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("preserve", File.ReadAllText(marker));
        }
        finally
        {
            TryDelete(root.FullName);
        }
    }

    [Fact]
    public void Build_ScriptPackedPreservesMappedEmptyDirectories()
    {
        var root = CreateRoot();
        try
        {
            const string moduleName = "DirectoryModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            File.WriteAllText(
                Path.Combine(stagingRoot, moduleName + ".psd1"),
                "@{ RootModule = 'DirectoryModule.psm1'; ModuleVersion = '1.0.0' }");
            File.WriteAllText(Path.Combine(stagingRoot, moduleName + ".psm1"), "function Get-Value { 1 }");
            string mappedRoot = Directory.CreateDirectory(
                Path.Combine(root.FullName, "mapped", "nested", "empty")).Parent!.Parent!.FullName;
            var segment = CreateSegment(Path.Combine(root.FullName, "output"), ArtefactType.ScriptPacked);
            segment.Configuration.DirectoryOutput =
            [
                new ArtefactCopyMapping
                {
                    Source = mappedRoot,
                    Destination = "assets"
                }
            ];

            ArtefactBuildResult result = new ArtefactBuilder(new NullLogger()).Build(
                segment,
                root.FullName,
                stagingRoot,
                moduleName,
                "1.0.0",
                null,
                Array.Empty<RequiredModuleReference>());

            using ZipArchive archive = ZipFile.OpenRead(result.OutputPath);
            Assert.Contains(archive.Entries, static entry => entry.FullName == "assets/nested/empty/");
        }
        finally
        {
            TryDelete(root.FullName);
        }
    }

    [Fact]
    public void CreateDeterministicZipFromDirectoryContents_RejectsDescendantSymlink()
    {
        var root = CreateRoot();
        try
        {
            string sourceRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "source")).FullName;
            string externalRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "external")).FullName;
            File.WriteAllText(Path.Combine(sourceRoot, "Invoke-Sample.ps1"), "'sample'");
            File.WriteAllText(Path.Combine(externalRoot, "outside.ps1"), "'outside'");
            try
            {
                Directory.CreateSymbolicLink(Path.Combine(sourceRoot, "linked"), externalRoot);
            }
            catch (Exception linkError) when (linkError is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
            {
                return;
            }

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                ArtefactBuilder.CreateDeterministicZipFromDirectoryContents(
                    sourceRoot,
                    Path.Combine(root.FullName, "output.zip")));

            Assert.Contains("symbolic links or reparse points", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(root.FullName, "output.zip")));
        }
        finally
        {
            TryDelete(root.FullName);
        }
    }

    private static ArtefactBuildResult Build(
        string projectRoot,
        string stagingRoot,
        string outputRoot,
        string moduleName,
        ArtefactType artefactType) =>
        new ArtefactBuilder(new NullLogger()).Build(
            CreateSegment(outputRoot, artefactType),
            projectRoot,
            stagingRoot,
            moduleName,
            "1.0.0",
            null,
            Array.Empty<RequiredModuleReference>());

    private static ConfigurationArtefactSegment CreateSegment(string outputRoot, ArtefactType artefactType) =>
        new()
        {
            ArtefactType = artefactType,
            Configuration = new ArtefactConfiguration
            {
                Enabled = true,
                Path = outputRoot,
                ArtefactName = "DirectoryModule.zip"
            }
        };

    private static DirectoryInfo CreateRoot() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;
        try { Directory.Delete(path, recursive: true); } catch { }
    }
}
