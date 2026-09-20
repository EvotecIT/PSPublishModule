using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Orchestrator.Explorer;

namespace PowerForgeStudio.Tests;

public sealed class FileExplorerOperationsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "studio-files-" + Guid.NewGuid().ToString("N"));
    private readonly FileExplorerService _service = new();
    public FileExplorerOperationsTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task CreateCopyRenameAndMovePreserveContent()
    {
        await Execute(WorkspaceFileOperation.CreateDirectory, null, "Build");
        await Execute(WorkspaceFileOperation.CreateFile, null, "Build/source.ps1");
        await File.WriteAllTextAsync(Path.Combine(_root, "Build", "source.ps1"), "# Readable content");
        await Execute(WorkspaceFileOperation.Copy, "Build", "BuildCopy");
        Assert.Equal("# Readable content", await File.ReadAllTextAsync(Path.Combine(_root, "BuildCopy", "source.ps1")));
        await Execute(WorkspaceFileOperation.Rename, "BuildCopy/source.ps1", "BuildCopy/renamed.ps1");
        await Execute(WorkspaceFileOperation.Move, "BuildCopy/renamed.ps1", "renamed.ps1");
        Assert.False(File.Exists(Path.Combine(_root, "BuildCopy", "renamed.ps1")));
        Assert.Equal("# Readable content", await File.ReadAllTextAsync(Path.Combine(_root, "renamed.ps1")));
        Assert.True(File.Exists(Path.Combine(_root, "Build", "source.ps1")));
        Assert.Empty(Directory.EnumerateDirectories(_root, ".powerforge-copy-*"));
    }

    [Theory]
    [InlineData(WorkspaceFileOperation.CreateFile)]
    [InlineData(WorkspaceFileOperation.Copy)]
    [InlineData(WorkspaceFileOperation.Move)]
    [InlineData(WorkspaceFileOperation.Rename)]
    public async Task ExistingDestinationIsNeverOverwritten(WorkspaceFileOperation operation)
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "source.txt"), "source");
        await File.WriteAllTextAsync(Path.Combine(_root, "target.txt"), "original target");
        await Assert.ThrowsAsync<IOException>(() => Execute(operation, "source.txt", "target.txt"));
        Assert.Equal("original target", await File.ReadAllTextAsync(Path.Combine(_root, "target.txt")));
        Assert.Equal("source", await File.ReadAllTextAsync(Path.Combine(_root, "source.txt")));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData(".git/config")]
    [InlineData(".")]
    public async Task RootEscapeAndGitMetadataAreRejected(string target)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Execute(WorkspaceFileOperation.CreateFile, null, target));
    }

    [Fact]
    public async Task DirectoryMoveCannotContainRepositoryOrRecurseIntoItself()
    {
        Directory.CreateDirectory(Path.Combine(_root, "nested", ".git"));
        await Assert.ThrowsAsync<IOException>(() => Execute(WorkspaceFileOperation.Move, "nested", "renamed"));
        await Assert.ThrowsAsync<IOException>(() => Execute(WorkspaceFileOperation.Copy, "nested", "nested/copy"));
        Assert.True(Directory.Exists(Path.Combine(_root, "nested", ".git")));
        Assert.False(Directory.Exists(Path.Combine(_root, "renamed")));
    }

    [Fact]
    public async Task CancelledOperationDoesNotCreateDestination()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "source.txt"), "source");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.ExecuteAsync(
            new WorkspaceFileOperationRequest(WorkspaceFileOperation.Copy, _root, "source.txt", "target.txt"), cancellation.Token));
        Assert.False(File.Exists(Path.Combine(_root, "target.txt")));
        Assert.True(File.Exists(Path.Combine(_root, "source.txt")));
    }

    private Task Execute(WorkspaceFileOperation operation, string? source, string destination) =>
        _service.ExecuteAsync(new WorkspaceFileOperationRequest(operation, _root, source, destination));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDuringTransferRemovesOnlyPartialDestination(bool directory)
    {
        Directory.CreateDirectory(Path.Combine(_root, "source"));
        var file = Path.Combine(_root, "source", "payload.bin");
        await using (var stream = File.Create(file)) stream.SetLength(2 * 1024 * 1024);
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress(_ => cancellation.Cancel());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.ExecuteAsync(
            new WorkspaceFileOperationRequest(WorkspaceFileOperation.Copy, _root,
                directory ? "source" : "source/payload.bin", "target"), cancellation.Token, progress));
        Assert.True(File.Exists(file));
        Assert.Equal(2 * 1024 * 1024, new FileInfo(file).Length);
        Assert.False(File.Exists(Path.Combine(_root, "target")));
        Assert.False(Directory.Exists(Path.Combine(_root, "target")));
        Assert.Empty(Directory.EnumerateDirectories(_root, ".powerforge-copy-*"));
    }

    [Fact]
    public async Task LinkedSourceAndDestinationAreNotFollowed()
    {
        var directory = Path.Combine(_root, "original");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "file.txt"), "keep");
        Directory.CreateSymbolicLink(Path.Combine(_root, "link"), directory);
        await Assert.ThrowsAsync<IOException>(() => Execute(WorkspaceFileOperation.Copy, "link/file.txt", "copy.txt"));
        await Assert.ThrowsAsync<IOException>(() => Execute(WorkspaceFileOperation.CreateFile, null, "link/new.txt"));
        Assert.False(File.Exists(Path.Combine(directory, "new.txt")));
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(directory, "file.txt")));
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("LPT1.txt")]
    [InlineData("file.")]
    [InlineData("file.txt:stream")]
    public async Task InvalidWindowsDestinationsAreRejected(string destination)
    {
        if (!OperatingSystem.IsWindows()) return;
        await Assert.ThrowsAsync<ArgumentException>(() => Execute(WorkspaceFileOperation.CreateFile, null, destination));
    }

    [Theory]
    [InlineData(493)] // 0755, executable
    [InlineData(384)] // 0600, private
    public async Task UnixCopiesPreserveSourceMode(int mode)
    {
        if (OperatingSystem.IsWindows()) return;
        var source = Path.Combine(_root, "script.sh");
        await File.WriteAllTextAsync(source, "#!/bin/sh\nexit 0\n");
        File.SetUnixFileMode(source, (UnixFileMode)mode);
        await Execute(WorkspaceFileOperation.Copy, "script.sh", "copy.sh");
        Assert.Equal((UnixFileMode)mode, File.GetUnixFileMode(Path.Combine(_root, "copy.sh")));
        Directory.CreateDirectory(Path.Combine(_root, "scripts"));
        File.Move(source, Path.Combine(_root, "scripts", "script.sh"));
        await Execute(WorkspaceFileOperation.Copy, "scripts", "scripts-copy");
        Assert.Equal((UnixFileMode)mode, File.GetUnixFileMode(Path.Combine(_root, "scripts-copy", "script.sh")));
    }

    private sealed class InlineProgress(Action<FileTransferProgress> report) : IProgress<FileTransferProgress>
    {
        public void Report(FileTransferProgress value) => report(value);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
