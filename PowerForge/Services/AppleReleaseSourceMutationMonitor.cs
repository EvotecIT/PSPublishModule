namespace PowerForge;

/// <summary>
/// Detects transient writes, renames, creations, deletions, or metadata changes inside an exact-source
/// build snapshot while xcodebuild is allowed to read it.
/// </summary>
internal sealed class AppleReleaseSourceMutationMonitor : IDisposable {
    private readonly FileSystemWatcher _watcher;
    private readonly string _scopeDescription;
    private readonly string _readerDescription;
    private readonly string _failureInstruction;
    private string? _firstMutation;
    private Exception? _watcherError;
    private bool _disposed;

    internal AppleReleaseSourceMutationMonitor(
        string rootPath,
        string scopeDescription = "exact-source Apple build snapshot",
        string readerDescription = "xcodebuild",
        string failureInstruction = "Discard the archive and rebuild from a new snapshot.") {
        _scopeDescription = scopeDescription;
        _readerDescription = readerDescription;
        _failureInstruction = failureInstruction;
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
                $"The {_scopeDescription} mutation monitor failed; its output cannot be trusted. {_failureInstruction}",
                _watcherError);
        }
        if (!string.IsNullOrWhiteSpace(_firstMutation)) {
            throw new InvalidOperationException(
                $"The {_scopeDescription} changed while {_readerDescription} was reading it: {_firstMutation}. " +
                _failureInstruction);
        }
    }

    internal T CaptureExpectedProducerOutput<T>(Func<T> capture, string producerDescription) {
        if (capture is null)
            throw new ArgumentNullException(nameof(capture));
        if (_watcherError is not null) {
            throw new InvalidOperationException(
                $"The {_scopeDescription} mutation monitor failed at the {producerDescription} completion boundary. {_failureInstruction}",
                _watcherError);
        }

        // Events already delivered belong to the awaited producer. Establish the
        // boundary immediately, then bind its output before giving another process
        // a deterministic replacement window.
        Interlocked.Exchange(ref _firstMutation, null);
        var output = capture();
        Thread.Sleep(250);
        if (_watcherError is not null) {
            throw new InvalidOperationException(
                $"The {_scopeDescription} changed while its {producerDescription} output was being bound. {_failureInstruction}",
                _watcherError);
        }

        if (!string.IsNullOrWhiteSpace(_firstMutation)) {
            // FileSystemWatcher can deliver producer writes after process exit. A
            // delayed producer notification is harmless only when the exact bound
            // identity is unchanged. Re-establish the event fence, rebind, and then
            // require a quiet drain so a real post-exit replacement cannot be erased.
            Interlocked.Exchange(ref _firstMutation, null);
            var currentOutput = capture();
            if (!EqualityComparer<T>.Default.Equals(output, currentOutput)) {
                throw new InvalidOperationException(
                    $"The {_scopeDescription} changed after {producerDescription} completed. {_failureInstruction}");
            }
            Thread.Sleep(250);
            if (_watcherError is not null || !string.IsNullOrWhiteSpace(_firstMutation)) {
                throw new InvalidOperationException(
                    $"The {_scopeDescription} changed while its {producerDescription} output was being bound. {_failureInstruction}",
                    _watcherError);
            }
        }
        return output;
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
