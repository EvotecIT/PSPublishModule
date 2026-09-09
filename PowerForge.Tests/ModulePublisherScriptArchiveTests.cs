using System.IO.Compression;

namespace PowerForge.Tests;

public sealed class ModulePublisherScriptArchiveTests
{
    [Theory]
    [InlineData("../escape/")]
    [InlineData("assets/data:cache.json")]
    public void ValidateDirectGitHubArtefactAssets_RejectsUnsafeScriptPackedArchive(
        string unsafeEntryName)
    {
        string root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string archivePath = Path.Combine(root, "Script.zip");
            using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("Script.ps1").Open()))
                    writer.Write("Get-Date");
                archive.CreateEntry(unsafeEntryName);
            }
            var artefact = new ArtefactBuildResult(
                ArtefactType.ScriptPacked,
                "release",
                archivePath,
                Array.Empty<ArtefactModuleEntry>(),
                Array.Empty<ArtefactCopyEntry>(),
                Array.Empty<string>(),
                "Script.ps1");

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
                ModulePublisher.ValidateDirectGitHubArtefactAssets(new[] { artefact }));

            Assert.Contains("not safe for direct GitHub publication", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
