using System.IO.Compression;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineScriptExecutionSeamTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CollectModuleReleaseAssets_ArchivesCompleteNestedScriptLayoutAndSelectsEvidence(
        bool leadingShebang)
    {
        string root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        string scriptPath = Path.Combine(root, "app", "Invoke-Sample.ps1");
        string resourcePath = Path.Combine(root, "app", "Resources", "data.json");
        string evidencePath = Path.Combine(root, "PowerForge.ScriptEvidence.json");
        string archiveRoot = Path.Combine(Path.GetDirectoryName(root)!, Guid.NewGuid().ToString("N"), "modules");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(resourcePath)!);
            File.WriteAllText(
                scriptPath,
                (leadingShebang ? "#!/usr/bin/env pwsh\n" : string.Empty) + "'complete layout'\n");
            File.WriteAllText(resourcePath, "{}");
            File.WriteAllText(evidencePath, "{}");
            var artefact = new ArtefactBuildResult(
                ArtefactType.Script,
                "release-script",
                root,
                Array.Empty<ArtefactModuleEntry>(),
                Array.Empty<ArtefactCopyEntry>(),
                new[] { evidencePath },
                "app/Invoke-Sample.ps1");
            var method = typeof(ModulePipelineRunner).GetMethod(
                "CollectModuleReleaseAssets",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

            Assert.NotNull(method);
            string[] assets = Assert.IsType<string[]>(method!.Invoke(
                null,
                new object?[] { new[] { artefact }, "release-script", archiveRoot }));

            string archivePath = Path.Combine(archiveRoot, "Invoke-Sample.zip");
            Assert.Equal(new[] { Path.GetFullPath(archivePath), Path.GetFullPath(evidencePath) }, assets);
            byte[] firstArchive = File.ReadAllBytes(archivePath);
            string[] repeatedAssets = Assert.IsType<string[]>(method.Invoke(
                null,
                new object?[] { new[] { artefact }, "release-script", archiveRoot }));
            Assert.Equal(assets, repeatedAssets);
            Assert.Equal(firstArchive, File.ReadAllBytes(archivePath));
            using var archive = ZipFile.OpenRead(archivePath);
            ZipArchiveEntry scriptEntry = Assert.Single(archive.Entries, entry => entry.FullName == "app/Invoke-Sample.ps1");
            Assert.Contains(archive.Entries, entry => entry.FullName == "app/Resources/data.json");
            int mode = (scriptEntry.ExternalAttributes >> 16) & 0x1FF;
            Assert.Equal(leadingShebang ? 0x49 : 0, mode & 0x49);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
            try { Directory.Delete(Path.GetDirectoryName(archiveRoot)!, recursive: true); } catch { }
        }
    }
}
