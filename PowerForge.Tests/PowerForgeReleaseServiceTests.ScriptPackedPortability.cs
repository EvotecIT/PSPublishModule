using System.IO.Compression;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void CreateModuleAssetEntries_AcceptsPortableScriptPackedDirectoryEntry()
    {
        string root = CreateSandbox();
        try
        {
            string scriptPackedPath = Path.Combine(root, "Company.Tools.zip");
            using (ZipArchive archive = ZipFile.Open(scriptPackedPath, ZipArchiveMode.Create))
            {
                archive.CreateEntry("scripts/");
                using var writer = new StreamWriter(archive.CreateEntry("scripts/Company.Tools.ps1").Open());
                writer.Write("Get-Date");
            }

            PowerForgeReleaseAssetEntry entry = Assert.Single(
                PowerForgeReleaseService.CreateModuleAssetEntries(
                    scriptPackedPath,
                    CreateScriptPackedPlan(root, scriptPackedPath, "scripts/Company.Tools.ps1"),
                    new[] { scriptPackedPath }));

            Assert.True(entry.IsFinalPackageOutput);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData("../escape/", true)]
    [InlineData("safe/../../escape/", true)]
    [InlineData("safe//payload.dll", false)]
    [InlineData("safe/demo:final.dll", false)]
    [InlineData("safe/CON.dll", false)]
    [InlineData("safe/com1.log", false)]
    [InlineData("safe/CON .txt", false)]
    [InlineData("safe/trailing-dot.", false)]
    [InlineData("safe/trailing-space ", false)]
    [InlineData("safe/bad?.dll", false)]
    [InlineData("safe/control\u0001.dll", false)]
    public void CreateModuleAssetEntries_RejectsNonPortableScriptPackedEntryOnEveryHost(
        string unsafeEntryName,
        bool directoryEntry)
    {
        string root = CreateSandbox();
        try
        {
            string scriptPackedPath = Path.Combine(root, "Company.Tools.zip");
            using (ZipArchive archive = ZipFile.Open(scriptPackedPath, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("Company.Tools.ps1").Open()))
                    writer.Write("Get-Date");

                ZipArchiveEntry unsafeEntry = archive.CreateEntry(unsafeEntryName);
                if (!directoryEntry)
                {
                    using var writer = new StreamWriter(unsafeEntry.Open());
                    writer.Write("unsafe");
                }
            }

            PowerForgeReleaseAssetEntry entry = Assert.Single(
                PowerForgeReleaseService.CreateModuleAssetEntries(
                    scriptPackedPath,
                    CreateScriptPackedPlan(root, scriptPackedPath, "Company.Tools.ps1"),
                    new[] { scriptPackedPath }));

            Assert.False(entry.IsFinalPackageOutput);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData("scripts/Invoke:Tools.ps1")]
    [InlineData("CON/Company.Tools.ps1")]
    [InlineData("scripts./Company.Tools.ps1")]
    public void CreateModuleAssetEntries_RejectsNonPortableConfiguredScriptPackedEntryPointOnEveryHost(
        string entryPoint)
    {
        string root = CreateSandbox();
        try
        {
            string scriptPackedPath = Path.Combine(root, "Company.Tools.zip");
            using (ZipArchive archive = ZipFile.Open(scriptPackedPath, ZipArchiveMode.Create))
            {
                using var writer = new StreamWriter(archive.CreateEntry(entryPoint).Open());
                writer.Write("Get-Date");
            }

            PowerForgeReleaseAssetEntry entry = Assert.Single(
                PowerForgeReleaseService.CreateModuleAssetEntries(
                    scriptPackedPath,
                    CreateScriptPackedPlan(root, scriptPackedPath, entryPoint),
                    new[] { scriptPackedPath }));

            Assert.False(entry.IsFinalPackageOutput);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static PowerForgeModuleReleasePlanSummary CreateScriptPackedPlan(
        string root,
        string archivePath,
        string entryPoint)
        => new()
        {
            ModuleName = "Company.Tools",
            ModuleVersion = "4.0.0",
            ArtefactOutputs =
            [
                new PowerForgeModuleArtefactOutputSummary
                {
                    Type = ArtefactType.ScriptPacked,
                    OutputRoot = root,
                    OutputPath = archivePath,
                    EntryPointRelativePath = entryPoint
                }
            ]
        };
}
