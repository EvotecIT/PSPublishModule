using System.Collections.Concurrent;

namespace PowerForge;

internal sealed class ManagedModuleInstallContext : IDisposable
{
    private const string MissingDependencyVersionSelection = "\0";

    private readonly List<string> _active;
    private readonly HashSet<string> _ownedInstallKeys;
    private readonly ManagedModuleInstallWaitGraph _waitGraph;
    private readonly ConcurrentDictionary<string, ManagedModuleInstallPending> _inFlightInstalls;
    private readonly ConcurrentDictionary<string, ManagedModuleInstallResult> _completedInstalls;
    private readonly ConcurrentDictionary<string, string[]> _installedVersions;
    private readonly ConcurrentDictionary<string, Lazy<Task<ManagedModuleDependencyVersionSelection>>> _dependencyVersionSelections;
#if !NET472
    private readonly ConcurrentDictionary<string, Lazy<Task<ManagedModuleBufferedPackage>>> _bufferedPackagePrefetches;
    private readonly SemaphoreSlim _bufferedPackagePrefetchGate;
    private readonly CancellationTokenSource _bufferedPackagePrefetchCancellation;
    private bool _disposed;
#endif

    public ManagedModuleInstallContext()
        : this(
            new List<string>(),
            new ManagedModuleInstallWaitGraph(),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new ConcurrentDictionary<string, ManagedModuleInstallPending>(StringComparer.OrdinalIgnoreCase),
            new ConcurrentDictionary<string, ManagedModuleInstallResult>(StringComparer.OrdinalIgnoreCase),
            new ConcurrentDictionary<string, string[]>(StringComparer.OrdinalIgnoreCase),
            new ConcurrentDictionary<string, Lazy<Task<ManagedModuleDependencyVersionSelection>>>(StringComparer.OrdinalIgnoreCase)
#if !NET472
            ,
            new ConcurrentDictionary<string, Lazy<Task<ManagedModuleBufferedPackage>>>(StringComparer.OrdinalIgnoreCase),
            new SemaphoreSlim(8, 8),
            new CancellationTokenSource()
#endif
            )
    {
    }

    private ManagedModuleInstallContext(
        List<string> active,
        ManagedModuleInstallWaitGraph waitGraph,
        HashSet<string> ownedInstallKeys,
        ConcurrentDictionary<string, ManagedModuleInstallPending> inFlightInstalls,
        ConcurrentDictionary<string, ManagedModuleInstallResult> completedInstalls,
        ConcurrentDictionary<string, string[]> installedVersions,
        ConcurrentDictionary<string, Lazy<Task<ManagedModuleDependencyVersionSelection>>> dependencyVersionSelections
#if !NET472
        ,
        ConcurrentDictionary<string, Lazy<Task<ManagedModuleBufferedPackage>>> bufferedPackagePrefetches,
        SemaphoreSlim bufferedPackagePrefetchGate,
        CancellationTokenSource bufferedPackagePrefetchCancellation
#endif
        )
    {
        _active = active;
        _waitGraph = waitGraph;
        _ownedInstallKeys = ownedInstallKeys;
        _inFlightInstalls = inFlightInstalls;
        _completedInstalls = completedInstalls;
        _installedVersions = installedVersions;
        _dependencyVersionSelections = dependencyVersionSelections;
#if !NET472
        _bufferedPackagePrefetches = bufferedPackagePrefetches;
        _bufferedPackagePrefetchGate = bufferedPackagePrefetchGate;
        _bufferedPackagePrefetchCancellation = bufferedPackagePrefetchCancellation;
#endif
    }

    public ManagedModuleInstallContext CreateBranch()
        => new(
            new List<string>(_active),
            _waitGraph,
            new HashSet<string>(_ownedInstallKeys, StringComparer.OrdinalIgnoreCase),
            _inFlightInstalls,
            _completedInstalls,
            _installedVersions,
            _dependencyVersionSelections
#if !NET472
            ,
            _bufferedPackagePrefetches,
            _bufferedPackagePrefetchGate,
            _bufferedPackagePrefetchCancellation
#endif
            );

    public IDisposable Enter(string moduleName)
    {
        var normalized = moduleName.Trim();
        var cycleStart = _active.FindIndex(name => name.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        if (cycleStart >= 0)
            throw ManagedModuleInstallWaitGraph.CreateCycleException(_active.Skip(cycleStart).Concat(new[] { _active[cycleStart] }));

        _active.Add(normalized);
        return new PopOnDispose(_active, normalized);
    }

    public bool IsActive(string moduleName)
        => _active.Contains(moduleName.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets whether the current branch is installing below the root request and may run alongside sibling branches.
    /// </summary>
    public bool IsDependencyInstall
        => _active.Count > 1;

    /// <summary>Owns a target or returns its shared task, recording an owned child before it can be awaited.</summary>
    public bool TryBeginInstall(
        string key,
        out Task<ManagedModuleInstallResult> existingInstall,
        out ManagedModuleInstallPending pendingInstall)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Install coalescing key is required.", nameof(key));

        while (true)
        {
            var pending = new ManagedModuleInstallPending(_inFlightInstalls, _ownedInstallKeys, key, _active.LastOrDefault() ?? key);
            if (_inFlightInstalls.TryAdd(key, pending))
            {
                try
                {
                    pending.ParentWait = _waitGraph.EnterWait(GetImmediateOwnedInstall(), pending);
                }
                catch (Exception exception)
                {
                    pending.Fail(exception);
                    pending.Dispose();
                    throw;
                }
                _ownedInstallKeys.Add(key);
                existingInstall = pending.Task;
                pendingInstall = pending;
                return true;
            }

            if (_inFlightInstalls.TryGetValue(key, out var existing))
            {
                existingInstall = existing.Task;
                pendingInstall = null!;
                return false;
            }
        }
    }

    /// <summary>Atomically checks and records a coalesced wait; the caller must keep the lease until its await completes.</summary>
    public IDisposable EnterInstallWait(string key)
    {
        _inFlightInstalls.TryGetValue(key, out var target);
        return _waitGraph.EnterWait(GetImmediateOwnedInstall(), target);
    }

    public bool TryGetCompletedInstall(string key, out ManagedModuleInstallResult result)
        => _completedInstalls.TryGetValue(key, out result!);

    public void RecordCompletedInstall(string key, ManagedModuleInstallResult result)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Install coalescing key is required.", nameof(key));
        if (result is null)
            throw new ArgumentNullException(nameof(result));

        _completedInstalls[key] = result;
    }

    public IReadOnlyList<string> GetInstalledVersions(string moduleRoot, string moduleName)
    {
        var key = CreateInstalledVersionKey(moduleRoot, moduleName);
        if (_installedVersions.TryGetValue(key, out var cached))
            return cached;

        var versions = EnumerateInstalledVersions(moduleRoot, moduleName);
        if (versions.Length > 0)
            _installedVersions.TryAdd(key, versions);

        return versions;
    }

    public void RecordInstalledVersion(string moduleRoot, string moduleName, string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return;

        var key = CreateInstalledVersionKey(moduleRoot, moduleName);
        var normalizedVersion = version.Trim();
        _installedVersions.AddOrUpdate(
            key,
            _ => new[] { normalizedVersion },
            (_, existing) =>
            {
                if (existing.Any(item => item.Equals(normalizedVersion, StringComparison.OrdinalIgnoreCase)))
                    return existing;

                return existing
                    .Concat(new[] { normalizedVersion })
                    .OrderBy(static item => item, ManagedModuleVersionComparer.Instance)
                    .ToArray();
            });
    }

    public Task<ManagedModuleDependencyVersionSelection> GetOrAddDependencyVersionSelection(string key, Func<Task<string>> factory)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Dependency version selection key is required.", nameof(key));
        if (factory is null)
            throw new ArgumentNullException(nameof(factory));

        var newLazy = new Lazy<Task<ManagedModuleDependencyVersionSelection>>(
            async () =>
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var version = await factory().ConfigureAwait(false);
                stopwatch.Stop();
                return new ManagedModuleDependencyVersionSelection(version, shared: false, stopwatch.Elapsed);
            },
            LazyThreadSafetyMode.ExecutionAndPublication);
        var lazy = _dependencyVersionSelections.GetOrAdd(
            key,
            _ => newLazy);

        return CompleteDependencyVersionSelectionAsync(key, lazy, !ReferenceEquals(lazy, newLazy));
    }

    public async Task<ManagedModuleDependencyVersionSelection?> GetOrAddOptionalDependencyVersionSelection(string key, Func<Task<string?>> factory)
    {
        if (factory is null)
            throw new ArgumentNullException(nameof(factory));

        var selection = await GetOrAddDependencyVersionSelection(
            key,
            async () => await factory().ConfigureAwait(false) ?? MissingDependencyVersionSelection).ConfigureAwait(false);
        return string.Equals(selection.Version, MissingDependencyVersionSelection, StringComparison.Ordinal)
            ? null
            : selection;
    }

#if !NET472
    public void StartBufferedPackagePrefetch(
        string key,
        Func<CancellationToken, Task<ManagedModuleBufferedPackage>> factory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Buffered package prefetch key is required.", nameof(key));
        if (factory is null)
            throw new ArgumentNullException(nameof(factory));
        if (_disposed || cancellationToken.IsCancellationRequested)
            return;

        var newLazy = new Lazy<Task<ManagedModuleBufferedPackage>>(
            async () =>
            {
                using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    _bufferedPackagePrefetchCancellation.Token);
                var prefetchCancellationToken = linkedCancellation.Token;
                await _bufferedPackagePrefetchGate.WaitAsync(prefetchCancellationToken).ConfigureAwait(false);
                try
                {
                    return await factory(prefetchCancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _bufferedPackagePrefetchGate.Release();
                }
            },
            LazyThreadSafetyMode.ExecutionAndPublication);
        var lazy = _bufferedPackagePrefetches.GetOrAdd(key, _ => newLazy);
        if (!ReferenceEquals(lazy, newLazy))
            return;

        _ = CompleteBufferedPackagePrefetchAsync(key, lazy);
    }

    public async Task<ManagedModuleBufferedPackage?> TryTakeBufferedPackagePrefetchAsync(
        string key,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        if (!_bufferedPackagePrefetches.TryRemove(key, out var lazy))
            return null;

        try
        {
            return await lazy.Value.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (ManagedModuleBufferedPackageTooLargeException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }
#endif

    public void Dispose()
    {
#if !NET472
        if (_disposed)
            return;

        _disposed = true;
        _bufferedPackagePrefetchCancellation.Cancel();
        foreach (var item in _bufferedPackagePrefetches.ToArray())
        {
            if (!_bufferedPackagePrefetches.TryRemove(item.Key, out var lazy) || !lazy.IsValueCreated)
                continue;

            DisposeBufferedPackageWhenComplete(lazy.Value);
        }
#endif
    }

#if !NET472
    private async Task CompleteBufferedPackagePrefetchAsync(
        string key,
        Lazy<Task<ManagedModuleBufferedPackage>> lazy)
    {
        try
        {
            var package = await lazy.Value.ConfigureAwait(false);
            if (_disposed)
            {
                package.Dispose();
            }
        }
        catch
        {
            ((ICollection<KeyValuePair<string, Lazy<Task<ManagedModuleBufferedPackage>>>>)_bufferedPackagePrefetches)
                .Remove(new KeyValuePair<string, Lazy<Task<ManagedModuleBufferedPackage>>>(key, lazy));
        }
    }

    private static void DisposeBufferedPackageWhenComplete(Task<ManagedModuleBufferedPackage> task)
    {
        if (task.IsCompletedSuccessfully)
        {
            task.Result.Dispose();
            return;
        }

        _ = task.ContinueWith(
            static completed =>
            {
                if (completed.Status == TaskStatus.RanToCompletion)
                    completed.Result.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
#endif

    private static string CreateInstalledVersionKey(string moduleRoot, string moduleName)
        => string.Join("|", NormalizePath(moduleRoot), moduleName.Trim());

    private async Task<ManagedModuleDependencyVersionSelection> CompleteDependencyVersionSelectionAsync(
        string key,
        Lazy<Task<ManagedModuleDependencyVersionSelection>> lazy,
        bool shared)
    {
        try
        {
            var selection = await lazy.Value.ConfigureAwait(false);
            return new ManagedModuleDependencyVersionSelection(selection.Version, shared, selection.Elapsed);
        }
        catch
        {
            ((ICollection<KeyValuePair<string, Lazy<Task<ManagedModuleDependencyVersionSelection>>>>)_dependencyVersionSelections)
                .Remove(new KeyValuePair<string, Lazy<Task<ManagedModuleDependencyVersionSelection>>>(key, lazy));
            throw;
        }
    }

    private static string NormalizePath(string path)
        => Path.GetFullPath(path.Trim().Trim('"')).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    internal static string[] EnumerateInstalledVersions(string moduleRoot, string moduleName)
    {
        var moduleFolder = Path.Combine(moduleRoot, moduleName.Trim());
        if (!Directory.Exists(moduleFolder))
            return Array.Empty<string>();

        try
        {
            var versions = Directory.GetDirectories(moduleFolder)
                .Select(path => ReadInstalledVersionDirectory(moduleName, path))
                .Where(static version => !string.IsNullOrWhiteSpace(version))
                .Select(static version => version!)
                .OrderBy(static version => version, ManagedModuleVersionComparer.Instance)
                .ToArray();
            if (versions.Length > 0)
                return versions;

            var manifestPath = Path.Combine(moduleFolder, moduleName.Trim() + ".psd1");
            if (!File.Exists(manifestPath))
                manifestPath = Directory.EnumerateFiles(moduleFolder, "*.psd1", SearchOption.TopDirectoryOnly).FirstOrDefault() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(manifestPath))
                return Array.Empty<string>();

            var manifest = new ModuleManifestMetadataReader().Read(manifestPath);
            var manifestVersion = string.IsNullOrWhiteSpace(manifest.PreRelease)
                ? manifest.ModuleVersion
                : manifest.ModuleVersion.Trim() + "-" + manifest.PreRelease!.Trim().TrimStart('-');
            return IsInstalledVersionDirectory(manifestVersion)
                ? new[] { manifestVersion!.Trim() }
                : Array.Empty<string>();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    internal static bool IsManagedStageDirectory(string directoryName)
        => directoryName.StartsWith(".pfmm-stage-", StringComparison.OrdinalIgnoreCase);

    private static string? ReadInstalledVersionDirectory(string moduleName, string directoryPath)
    {
        var directoryName = Path.GetFileName(directoryPath);
        if (string.IsNullOrWhiteSpace(directoryName) ||
            IsManagedStageDirectory(directoryName!) ||
            !IsInstalledVersionDirectory(directoryName))
        {
            return null;
        }

        var manifestPath = Path.Combine(directoryPath, moduleName.Trim() + ".psd1");
        if (!File.Exists(manifestPath))
            manifestPath = Directory.EnumerateFiles(directoryPath, "*.psd1", SearchOption.TopDirectoryOnly).FirstOrDefault() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(manifestPath))
            return directoryName!.Trim();

        try
        {
            var manifest = new ModuleManifestMetadataReader().Read(manifestPath);
            if (string.IsNullOrWhiteSpace(manifest.ModuleVersion))
                return directoryName!.Trim();

            return string.IsNullOrWhiteSpace(manifest.PreRelease)
                ? manifest.ModuleVersion.Trim()
                : manifest.ModuleVersion.Trim() + "-" + manifest.PreRelease!.Trim().TrimStart('-');
        }
        catch
        {
            return directoryName!.Trim();
        }
    }

    private static bool IsInstalledVersionDirectory(string? directoryName)
    {
        if (string.IsNullOrWhiteSpace(directoryName))
            return false;

        var value = directoryName!.Trim();
        var plusIndex = value.IndexOf('+');
        if (plusIndex >= 0)
            value = value.Substring(0, plusIndex);

        var dashIndex = value.IndexOf('-');
        var numeric = dashIndex >= 0 ? value.Substring(0, dashIndex) : value;
        if (numeric.Length == 0)
            return false;

        var parts = numeric.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 &&
               parts.All(static part => part.Length > 0 && part.All(static character => character >= '0' && character <= '9'));
    }

    private IEnumerable<ManagedModuleInstallPending> GetImmediateOwnedInstall()
    {
        // The nearest owned request is the real parent; ancestor shortcuts would omit
        // intermediate modules from cycle diagnostics.
        for (var index = _active.Count - 1; index >= 0; index--)
        {
            foreach (var key in _ownedInstallKeys)
            {
                if (_inFlightInstalls.TryGetValue(key, out var pending) &&
                    pending.Name.Equals(_active[index], StringComparison.OrdinalIgnoreCase))
                {
                    yield return pending;
                    yield break;
                }
            }
        }

    }

    private sealed class PopOnDispose : IDisposable
    {
        private readonly List<string> _active;
        private readonly string _moduleName;

        public PopOnDispose(List<string> active, string moduleName)
        {
            _active = active;
            _moduleName = moduleName;
        }

        public void Dispose()
        {
            _active.Remove(_moduleName);
        }
    }
}

internal readonly struct ManagedModuleDependencyVersionSelection
{
    public ManagedModuleDependencyVersionSelection(string version, bool shared, TimeSpan elapsed = default)
    {
        Version = version;
        Shared = shared;
        Elapsed = elapsed;
    }

    public string Version { get; }

    public bool Shared { get; }

    public TimeSpan Elapsed { get; }
}

internal sealed class ManagedModuleInstallPending : IDisposable
{
    private readonly ConcurrentDictionary<string, ManagedModuleInstallPending> _owner;
    private readonly HashSet<string> _ownedInstallKeys;
    private readonly string _key;
    private readonly TaskCompletionSource<ManagedModuleInstallResult> _completion;
    private bool _disposed;

    public ManagedModuleInstallPending(
        ConcurrentDictionary<string, ManagedModuleInstallPending> owner,
        HashSet<string> ownedInstallKeys,
        string key,
        string name)
    {
        Name = name;
        _owner = owner;
        _ownedInstallKeys = ownedInstallKeys;
        _key = key;
        _completion = new TaskCompletionSource<ManagedModuleInstallResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public Task<ManagedModuleInstallResult> Task => _completion.Task;

    public string Name { get; }

    public IDisposable? ParentWait { get; set; }

    public void Complete(ManagedModuleInstallResult result)
        => _completion.TrySetResult(result);

    public void Fail(Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            _completion.TrySetCanceled();
            return;
        }

        _completion.TrySetException(exception);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        ParentWait?.Dispose();
        ((ICollection<KeyValuePair<string, ManagedModuleInstallPending>>)_owner)
            .Remove(new KeyValuePair<string, ManagedModuleInstallPending>(_key, this));
        _ownedInstallKeys.Remove(_key);
    }
}
