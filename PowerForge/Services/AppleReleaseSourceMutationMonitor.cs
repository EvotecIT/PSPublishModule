namespace PowerForge;

/// <summary>
/// Detects transient writes, renames, creations, deletions, or metadata changes inside an exact-source
/// build snapshot while xcodebuild is allowed to read it.
/// </summary>
internal sealed class AppleReleaseSourceMutationMonitor : IDisposable {
    private readonly FileSystemWatcher _watcher;
    private string? _firstMutation;
    private Exception? _watcherError;
    private bool _disposed;

    internal AppleReleaseSourceMutationMonitor(string rootPath) {
        _watcher = new FileSystemWatcher(rootPath) {
            IncludeSubdirectories = true,
            InternalBufferSize = 64 * 1024,
            NotifyFilter = NotifyFilters.FileName |
                           NotifyFilters.DirectoryName |
                           NotifyFilters.Attributes |
                           NotifyFilters.Size |
                           NotifyFilters.LastWrite |
                           NotifyFilters.CreationTime |
                           NotifyFilters.Security
        };
        _watcher.Changed += OnMutation;
        _watcher.Created += OnMutation;
        _watcher.Deleted += OnMutation;
        _watcher.Renamed += OnMutation;
        _watcher.Error += OnError;
        _watcher.EnableRaisingEvents = true;
    }

    internal void ValidateNoChanges() {
        // macOS FSEvents delivery is asynchronous. Give the already-completed archive operation's
        // notifications a short drain window before closing the monitor and accepting its output.
        Thread.Sleep(250);
        _watcher.EnableRaisingEvents = false;
        if (_watcherError is not null) {
            throw new InvalidOperationException(
                "The exact-source Apple build snapshot mutation monitor failed; the archive cannot be trusted.",
                _watcherError);
        }
        if (!string.IsNullOrWhiteSpace(_firstMutation)) {
            throw new InvalidOperationException(
                $"The exact-source Apple build snapshot changed while xcodebuild was reading it: {_firstMutation}. " +
                "Discard the archive and rebuild from a new snapshot.");
        }
    }

    private void OnMutation(object sender, FileSystemEventArgs args)
        => Interlocked.CompareExchange(ref _firstMutation, args.FullPath, null);

    private void OnError(object sender, ErrorEventArgs args)
        => Interlocked.CompareExchange(ref _watcherError, args.GetException(), null);

    public void Dispose() {
        if (_disposed)
            return;
        _disposed = true;
        _watcher.Dispose();
    }
}