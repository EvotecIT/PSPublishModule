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
        string failureInstruction = "Discard the archive and rebuild from a new snapshot.",
        bool enableImmediately = true) {
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
        _watcher.EnableRaisingEvents = enableImmediately;
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

        // Producer-output monitors are armed only at the process completion boundary.
        // No producer events are cleared: the first identity is captured while every
        // later write, rename, or metadata change remains observable.
        if (!_watcher.EnableRaisingEvents)
            _watcher.EnableRaisingEvents = true;
        if (!string.IsNullOrWhiteSpace(_firstMutation)) {
            throw new InvalidOperationException(
                $"The {_scopeDescription} changed before its {producerDescription} output could be bound. {_failureInstruction}");
        }
        var output = capture();
        Thread.Sleep(250);
        if (_watcherError is not null) {
            throw new InvalidOperationException(
                $"The {_scopeDescription} changed while its {producerDescription} output was being bound. {_failureInstruction}",
                _watcherError);
        }
        var currentOutput = capture();
        if (!EqualityComparer<T>.Default.Equals(output, currentOutput)) {
            throw new InvalidOperationException(
                $"The {_scopeDescription} changed after {producerDescription} completed. {_failureInstruction}");
        }

        if (!string.IsNullOrWhiteSpace(_firstMutation)) {
            throw new InvalidOperationException(
                $"The {_scopeDescription} changed after {producerDescription} completed. {_failureInstruction}");
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
