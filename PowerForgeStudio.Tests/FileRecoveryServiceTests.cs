using System.Text.Json;
using PowerForgeStudio.Orchestrator.Explorer;

namespace PowerForgeStudio.Tests;

public sealed class FileRecoveryServiceTests : IDisposable
{
    private readonly string _fixture = Path.Combine(
        Path.GetTempPath(),
        "studio-recovery-" + Guid.NewGuid().ToString("N"));
    private readonly string _workingCopy;
    private readonly string _recoveryRoot;
    private readonly FileRecoveryService _service;

    public FileRecoveryServiceTests()
    {
        _workingCopy = Directory.CreateDirectory(Path.Combine(_fixture, "repo")).FullName;
        _recoveryRoot = Path.Combine(_fixture, "recovery");
        _service = new FileRecoveryService(_recoveryRoot);
    }

    [Fact]
    public async Task ReviewedDirectoryMovesToRecoveryAndRestoresWithoutDataLoss()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_workingCopy, "Docs")).FullName;
        var file = Path.Combine(folder, "guide.md");
        await File.WriteAllTextAsync(file, "recovery contents");

        var preview = await _service.InspectDeleteAsync(_workingCopy, folder);
        Assert.True(preview.IsDirectory);
        Assert.Equal(2, preview.ItemCount);
        Assert.Equal("recovery contents".Length, preview.SizeBytes);

        var deleted = await _service.DeleteAsync(preview);
        Assert.False(Directory.Exists(folder));
        Assert.True(File.Exists(Path.Combine(deleted.RecoveryPath, "guide.md")));
        Assert.Equal(deleted, Assert.Single(await _service.ListAsync(_workingCopy)));

        await _service.RestoreAsync(deleted);
        Assert.Equal("recovery contents", await File.ReadAllTextAsync(file));
        Assert.Empty(await _service.ListAsync(_workingCopy));
    }

    [Fact]
    public async Task ChangedItemIsRejectedAndRemainsInWorkingCopy()
    {
        var file = Path.Combine(_workingCopy, "README.md");
        await File.WriteAllTextAsync(file, "before");
        var preview = await _service.InspectDeleteAsync(_workingCopy, file);
        await File.WriteAllTextAsync(file, "changed after review");

        var exception = await Assert.ThrowsAsync<IOException>(() => _service.DeleteAsync(preview));

        Assert.Contains("changed after review", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("changed after review", await File.ReadAllTextAsync(file));
        Assert.Empty(await _service.ListAsync(_workingCopy));
    }

    [Fact]
    public async Task RestoreRefusesToOverwriteReplacement()
    {
        var file = Path.Combine(_workingCopy, "settings.json");
        await File.WriteAllTextAsync(file, "original");
        var deleted = await _service.DeleteAsync(await _service.InspectDeleteAsync(_workingCopy, file));
        await File.WriteAllTextAsync(file, "replacement");

        await Assert.ThrowsAsync<IOException>(() => _service.RestoreAsync(deleted));

        Assert.Equal("replacement", await File.ReadAllTextAsync(file));
        Assert.Equal("original", await File.ReadAllTextAsync(deleted.RecoveryPath));
        Assert.Single(await _service.ListAsync(_workingCopy));
    }

    [Fact]
    public async Task InterruptedManifestWithPayloadIsRecoveredOnNextListing()
    {
        var file = Path.Combine(_workingCopy, "notes.txt");
        await File.WriteAllTextAsync(file, "notes");
        var deleted = await _service.DeleteAsync(await _service.InspectDeleteAsync(_workingCopy, file));
        var entryDirectory = Path.GetDirectoryName(deleted.RecoveryPath)!;
        var manifestPath = Path.Combine(entryDirectory, "manifest.json");
        var manifest = await File.ReadAllTextAsync(manifestPath);
        await File.WriteAllTextAsync(manifestPath, manifest.Replace("\"Available\"", "\"Prepared\""));

        Assert.Equal(deleted, Assert.Single(await _service.ListAsync(_workingCopy)));
        Assert.Contains("\"Available\"", await File.ReadAllTextAsync(manifestPath));
    }

    [Fact]
    public async Task PermanentDeleteRemovesOnlyTheValidatedRecoveryEntry()
    {
        var first = Path.Combine(_workingCopy, "first.txt");
        var second = Path.Combine(_workingCopy, "second.txt");
        await File.WriteAllTextAsync(first, "first");
        await File.WriteAllTextAsync(second, "second");
        var firstEntry = await _service.DeleteAsync(await _service.InspectDeleteAsync(_workingCopy, first));
        var secondEntry = await _service.DeleteAsync(await _service.InspectDeleteAsync(_workingCopy, second));

        await Assert.ThrowsAsync<IOException>(() => _service.DeletePermanentlyAsync(
            firstEntry with { OriginalPath = secondEntry.OriginalPath }));
        await _service.DeletePermanentlyAsync(firstEntry);

        Assert.False(File.Exists(firstEntry.RecoveryPath));
        Assert.True(File.Exists(secondEntry.RecoveryPath));
        Assert.Equal(secondEntry, Assert.Single(await _service.ListAsync(_workingCopy)));
    }

    [Fact]
    public async Task PermanentDeleteRefusesARecoveryPayloadThatGainedGitMetadata()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_workingCopy, "Docs")).FullName;
        await File.WriteAllTextAsync(Path.Combine(folder, "guide.md"), "guide");
        var entry = await _service.DeleteAsync(await _service.InspectDeleteAsync(_workingCopy, folder));
        Directory.CreateDirectory(Path.Combine(entry.RecoveryPath, ".git"));

        await Assert.ThrowsAsync<IOException>(() => _service.DeletePermanentlyAsync(entry));

        Assert.True(Directory.Exists(entry.RecoveryPath));
        Assert.Equal(entry, Assert.Single(await _service.ListAsync(_workingCopy)));
    }

    [Fact]
    public async Task MissingPayloadManifestIsRemovedOnNextListing()
    {
        var file = Path.Combine(_workingCopy, "orphan.txt");
        await File.WriteAllTextAsync(file, "orphan");
        var entry = await _service.DeleteAsync(await _service.InspectDeleteAsync(_workingCopy, file));
        var entryDirectory = Path.GetDirectoryName(entry.RecoveryPath)!;
        File.Delete(entry.RecoveryPath);

        Assert.Empty(await _service.ListAsync(_workingCopy));
        Assert.False(Directory.Exists(entryDirectory));
    }

    [Fact]
    public async Task RecoveryStoreInsideWorkingCopyIsRejectedBeforeSourceMoves()
    {
        var file = Path.Combine(_workingCopy, "safe.txt");
        await File.WriteAllTextAsync(file, "keep me");
        var service = new FileRecoveryService(Path.Combine(_workingCopy, ".studio-recovery"));
        var preview = await service.InspectDeleteAsync(_workingCopy, file);

        await Assert.ThrowsAsync<IOException>(() => service.DeleteAsync(preview));

        Assert.Equal("keep me", await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task TamperedManifestCannotExposeOrDeleteAnExternalPayload()
    {
        var file = Path.Combine(_workingCopy, "bounded.txt");
        var external = Path.Combine(_fixture, "external.txt");
        await File.WriteAllTextAsync(file, "recovered");
        await File.WriteAllTextAsync(external, "external");
        var entry = await _service.DeleteAsync(await _service.InspectDeleteAsync(_workingCopy, file));
        var manifestPath = Path.Combine(Path.GetDirectoryName(entry.RecoveryPath)!, "manifest.json");
        var manifest = await File.ReadAllTextAsync(manifestPath);
        await File.WriteAllTextAsync(
            manifestPath,
            manifest.Replace(
                JsonSerializer.Serialize(entry.RecoveryPath),
                JsonSerializer.Serialize(external),
                StringComparison.Ordinal));

        Assert.Empty(await _service.ListAsync(_workingCopy));
        await Assert.ThrowsAsync<IOException>(() => _service.DeletePermanentlyAsync(entry));
        Assert.Equal("external", await File.ReadAllTextAsync(external));
        Assert.Equal("recovered", await File.ReadAllTextAsync(entry.RecoveryPath));
    }

    [Fact]
    public async Task RootGitMetadataAndNestedRepositoryAreRejected()
    {
        Directory.CreateDirectory(Path.Combine(_workingCopy, ".git"));
        var nested = Directory.CreateDirectory(Path.Combine(_workingCopy, "nested")).FullName;
        Directory.CreateDirectory(Path.Combine(nested, ".git"));

        await Assert.ThrowsAsync<ArgumentException>(() => _service.InspectDeleteAsync(_workingCopy, _workingCopy));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.InspectDeleteAsync(_workingCopy, Path.Combine(_workingCopy, ".git")));
        await Assert.ThrowsAsync<IOException>(() => _service.InspectDeleteAsync(_workingCopy, nested));
        Assert.True(Directory.Exists(nested));
    }

    public void Dispose()
    {
        if (Directory.Exists(_fixture))
            Directory.Delete(_fixture, recursive: true);
    }
}
