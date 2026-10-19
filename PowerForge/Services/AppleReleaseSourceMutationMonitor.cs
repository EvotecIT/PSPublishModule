namespace PowerForge;

/// <summary>
/// Detects transient writes, renames, creations, deletions, or metadata changes inside an exact-source
/// build snapshot while xcodebuild is allowed to read it.
/// </summary>
internal sealed class AppleReleaseSourceMutationMonitor : IDisposable {
    private readonly IDisposable _observer;
    private readonly Action _stopObserving;
    private readonly Action _drainObserving;
    private readonly string _scopeDescription;
    private readonly string _readerDescription;
    private readonly string _failureInstruction;
    private readonly string? _exactPath;
    private readonly bool _includeExactPathDescendants;
    private readonly Func<FileSystemEventArgs, bool>? _ignoredMutation;
    private int _enforceMutations;
    private long _mutationSequence;
    private long _producerContinuitySequence;
    private string? _firstMutation;
    private Exception? _watcherError;
    private bool _disposed;

    internal AppleReleaseSourceMutationMonitor(
        string rootPath,
        string scopeDescription = "exact-source Apple build snapshot",
        string readerDescription = "xcodebuild",
        string failureInstruction = "Discard the archive and rebuild from a new snapshot.",
        bool enableImmediately = true,
        string? exactPath = null,
        bool includeExactPathDescendants = false,
        Func<FileSystemEventArgs, bool>? ignoredMutation = null,
        Func<string, uint, bool>? unchangedMacOSCloneSource = null) {
        _scopeDescription = scopeDescription;
        _readerDescription = readerDescription;
        _failureInstruction = failureInstruction;
        _exactPath = string.IsNullOrWhiteSpace(exactPath) ? null : Path.GetFullPath(exactPath);
        _includeExactPathDescendants = includeExactPathDescendants;
        _ignoredMutation = ignoredMutation;
        _enforceMutations = enableImmediately ? 1 : 0;
#if NET8_0_OR_GREATER
        if (OperatingSystem.IsMacOS() && unchangedMacOSCloneSource is not null)
        {
            var native = new AppleMacOSFileChangeObserver(rootPath,
                (path, flags) =>
                {
                    // APFS reports both clone destinations and unchanged clone sources as Created.
                    // Only pure clone flags (optionally Created) on a bound, still-identical input
                    // qualify as read activity. The package owner binds regular files and contained
                    // artifact links independently. Mixed flags and every other path still fail.
                    try
                    {
                        if (AppleMacOSFileChangeObserver.IsCloneSourceCandidate(flags) &&
                            unchangedMacOSCloneSource(path, flags))
                            return;
                        OnMutation(this, new AppleMacOSFileChangeObserver.ChangeEventArgs(rootPath, path, flags));
                    }
                    catch (Exception exception) { Interlocked.CompareExchange(ref _watcherError, exception, null); }
                }, OnNativeObserverError);
            _observer = native;
            _stopObserving = native.Stop;
            _drainObserving = native.Drain;
            return;
        }
#endif
        var watcher = new FileSystemWatcher(rootPath) {
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
        watcher.Changed += OnMutation;
        watcher.Created += OnMutation;
        watcher.Deleted += OnMutation;
        watcher.Renamed += OnMutation;
        watcher.Error += OnError;
        // Keep the watcher subscribed for the complete producer lifetime. Producer-owned writes are
        // tolerated until the process completion boundary, but the watcher itself is never started
        // late: the boundary transition therefore cannot create an unobserved activation window.
        _observer = watcher;
        _stopObserving = () => watcher.EnableRaisingEvents = false;
        _drainObserving = static () => { };
        watcher.EnableRaisingEvents = true;
    }

    internal void ValidateNoChanges(Action? finalValidation = null)
        => ValidateNoChangesTogether(new[] { this }, finalValidation);

    /// <summary>
    /// Closes several mutation observers around one final identity boundary so
    /// no observer is disabled while another protected scope is still being read.
    /// </summary>
    internal static void ValidateNoChangesTogether(
        IReadOnlyList<AppleReleaseSourceMutationMonitor> monitors,
        Action? finalValidation = null)
    {
        if (monitors is null)
            throw new ArgumentNullException(nameof(monitors));
        if (monitors.Any(static monitor => monitor is null))
            throw new ArgumentException("Mutation monitors must not contain null entries.", nameof(monitors));

        // Drain mutations from the completed reader first so established diagnostics retain the
        // observed event rather than being replaced by a later identity-comparison message.
        Thread.Sleep(250);
        foreach (var monitor in monitors)
            monitor.ThrowIfMutationObserved();

        // Keep every watcher active while the caller captures its final content and physical
        // identities. A transient create/delete during that traversal may leave no final hash
        // difference, so the watchers are the only evidence that a boundary changed.
        finalValidation?.Invoke();
        if (finalValidation is not null)
        {
            // macOS FSEvents delivery is asynchronous. Drain mutations that occurred during
            // final identity capture before closing all observation boundaries together.
            Thread.Sleep(250);
        }
        foreach (var monitor in monitors)
            monitor._stopObserving();

        foreach (var monitor in monitors)
            monitor.ThrowIfMutationObserved();
    }

    private void ThrowIfMutationObserved() {
        if (_watcherError is not null) {
            throw new InvalidOperationException(
                $"The {_scopeDescription} mutation monitor failed; its output cannot be trusted. " +
                $"{_watcherError.Message} {_failureInstruction}",
                _watcherError);
        }
        if (!string.IsNullOrWhiteSpace(_firstMutation)) {
            throw new InvalidOperationException(
                $"The {_scopeDescription} changed while {_readerDescription} was reading it: {_firstMutation}. " +
                _failureInstruction);
        }
    }

#if NET8_0_OR_GREATER
    private void OnNativeObserverError(Exception exception)
    {
        // The producer may write freely before any output identity is trusted. A known
        // buffering loss in that phase requires a fresh, uninterrupted identity capture;
        // it cannot poison the later consumer boundary or hide a different observer error.
        if (exception is AppleMacOSFileChangeObserver.ContinuityLossException { Flags: 0x3 or 0x5 or 0x7 } &&
            Volatile.Read(ref _enforceMutations) == 0)
        {
            Interlocked.Increment(ref _producerContinuitySequence);
            // Pair with the atomic arm and its sequence check. A callback racing the arm
            // must remain terminal even if it read the producer phase before that arm.
            if (Volatile.Read(ref _enforceMutations) == 0)
                return;
        }
        Interlocked.CompareExchange(ref _watcherError, exception, null);
    }
#endif

    private void RequireProducerContinuity(long expected, string producerDescription)
    {
        if (Interlocked.Read(ref _producerContinuitySequence) != expected)
            throw new InvalidOperationException(
                $"The {_scopeDescription} lost native event continuity while its {producerDescription} output was being bound. " +
                _failureInstruction);
        ThrowIfMutationObserved();
    }

    internal T CaptureExpectedProducerOutput<T>(Func<T> capture, string producerDescription) {
        if (capture is null)
            throw new ArgumentNullException(nameof(capture));
        if (_watcherError is not null) {
            throw new InvalidOperationException(
                $"The {_scopeDescription} mutation monitor failed at the {producerDescription} completion boundary. " +
                $"{_watcherError.Message} {_failureInstruction}",
                _watcherError);
        }

        // Producer-output monitors are armed only at the process completion boundary.
        // No producer events are cleared: the first identity is captured while every
        // later write, rename, or metadata change remains observable.
        T output;
        if (Volatile.Read(ref _enforceMutations) == 0)
        {
            // Finish delivery of untrusted producer activity before the first complete
            // identity capture. No errors or events are cleared, and observation stays live.
            _drainObserving();
            ThrowIfMutationObserved();
            var continuity = Interlocked.Read(ref _producerContinuitySequence);
            output = capture();
            _drainObserving();
            RequireProducerContinuity(continuity, producerDescription);
            // FileSystemWatcher delivery is asynchronous. The producer's own final writes may still
            // be queued when the process-exit callback runs, so let the already-active observer drain
            // to a quiet sequence before changing those events from producer activity to tampering.
            // The output identity is bound before this drain and must remain identical afterward, so
            // a persistent replacement during the drain cannot become the accepted producer output.
            var sequence = Interlocked.Read(ref _mutationSequence);
            var stablePasses = 0;
            for (var pass = 0; pass < 10 && stablePasses < 5; pass++)
            {
                Thread.Sleep(50);
                var current = Interlocked.Read(ref _mutationSequence);
                if (current == sequence)
                {
                    stablePasses++;
                }
                else
                {
                    sequence = current;
                    stablePasses = 0;
                }
            }
            if (stablePasses < 5)
            {
                throw new InvalidOperationException(
                    $"The {_scopeDescription} did not become quiet at the producer completion boundary. {_failureInstruction}");
            }
            RequireProducerContinuity(continuity, producerDescription);
            // Close the producer-to-consumer transition before the final comparison. A watcher
            // event that lands after the quiet drain but before arming increments the sequence
            // while enforcement is still disabled; comparing the sequence after the atomic arm
            // catches that window. Events that land after the arm are retained in _firstMutation.
            Interlocked.Exchange(ref _enforceMutations, 1);
            RequireProducerContinuity(continuity, producerDescription);
            if (Interlocked.Read(ref _mutationSequence) != sequence)
            {
                throw new InvalidOperationException(
                    $"The {_scopeDescription} changed while its {producerDescription} output was being bound. {_failureInstruction}");
            }
            var drainedOutput = capture();
            if (!EqualityComparer<T>.Default.Equals(output, drainedOutput))
            {
                throw new InvalidOperationException(
                    $"The {_scopeDescription} changed while its {producerDescription} output was being bound. {_failureInstruction}");
            }
        }
        else
        {
            output = capture();
        }
        if (!string.IsNullOrWhiteSpace(_firstMutation)) {
            throw new InvalidOperationException(
                $"The {_scopeDescription} changed before its {producerDescription} output could be bound. {_failureInstruction}");
        }
        Thread.Sleep(250);
        if (_watcherError is not null) {
            throw new InvalidOperationException(
                $"The {_scopeDescription} changed while its {producerDescription} output was being bound. " +
                $"{_watcherError.Message} {_failureInstruction}",
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
    {
        if (_exactPath is not null && !MutationTouchesExactPath(args, _exactPath, _includeExactPathDescendants))
            return;
        if (_ignoredMutation is not null)
        {
            try
            {
                if (_ignoredMutation(args))
                    return;
            }
            catch (Exception exception)
            {
                Interlocked.CompareExchange(ref _watcherError, exception, null);
                return;
            }
        }
        Interlocked.Increment(ref _mutationSequence);
        if (Volatile.Read(ref _enforceMutations) != 0)
        {
            var detail = $"{args.FullPath} ({args.ChangeType})";
#if NET8_0_OR_GREATER
            if (args is AppleMacOSFileChangeObserver.ChangeEventArgs native)
                detail = $"{args.FullPath} ({args.ChangeType}, native flags 0x{native.Flags:X})";
#endif
            Interlocked.CompareExchange(ref _firstMutation, detail, null);
        }
    }

    private static bool MutationTouchesExactPath(
        FileSystemEventArgs args,
        string exactPath,
        bool includeDescendants)
    {
        var comparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (PathMatches(Path.GetFullPath(args.FullPath), exactPath, includeDescendants, comparison))
            return true;
        return args is RenamedEventArgs renamed &&
               PathMatches(Path.GetFullPath(renamed.OldFullPath), exactPath, includeDescendants, comparison);
    }

    private static bool PathMatches(
        string candidate,
        string exactPath,
        bool includeDescendants,
        StringComparison comparison)
    {
        if (candidate.Equals(exactPath, comparison))
            return true;
        if (!includeDescendants)
            return false;
        var prefix = exactPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                     Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, comparison);
    }

    private void OnError(object sender, ErrorEventArgs args)
        => Interlocked.CompareExchange(ref _watcherError, args.GetException(), null);

    public void Dispose() {
        if (_disposed)
            return;
        _disposed = true;
        _observer.Dispose();
    }
}
