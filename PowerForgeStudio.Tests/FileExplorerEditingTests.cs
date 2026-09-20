using PowerForgeStudio.Orchestrator.Explorer;

namespace PowerForgeStudio.Tests;

public sealed class FileExplorerEditingTests
{
    [Fact]
    public async Task EditingRejectsOutsideAndGitMetadataAndDoesNotRecreateDeletedFiles()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "studio-editor-paths-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(fixture, "Project");
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        var service = new FileExplorerService();
        try
        {
            var outside = Path.Combine(fixture, "outside.txt");
            var metadata = Path.Combine(root, ".git", "config");
            await File.WriteAllTextAsync(outside, "outside");
            await File.WriteAllTextAsync(metadata, "git config");
            await Assert.ThrowsAsync<ArgumentException>(() => service.OpenTextDocumentAsync(root, outside));
            await Assert.ThrowsAsync<ArgumentException>(() => service.OpenTextDocumentAsync(root, metadata));
            var documentPath = Path.Combine(root, "README.md");
            await File.WriteAllTextAsync(documentPath, "original");
            var original = await service.OpenTextDocumentAsync(root, documentPath);
            File.Delete(documentPath);
            await Assert.ThrowsAsync<FileNotFoundException>(() => service.SaveTextDocumentAsync(root, original, "draft"));
            Assert.False(File.Exists(documentPath));
            Assert.Equal("outside", await File.ReadAllTextAsync(outside));
            Assert.Equal("git config", await File.ReadAllTextAsync(metadata));
        }
        finally { Directory.Delete(fixture, recursive: true); }
    }
}
