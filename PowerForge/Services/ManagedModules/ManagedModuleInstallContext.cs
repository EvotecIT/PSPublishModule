using System.Collections.Concurrent;

namespace PowerForge;

internal sealed class ManagedModuleInstallContext
{
    private readonly HashSet<string> _active;
    private readonly HashSet<string> _ownedInstallKeys;
    private readonly ConcurrentDictionary<string, ManagedModuleInstallPending> _inFlightInstalls;
    private readonly ConcurrentDictionary<string, ManagedModuleInstallResult> _completedInstalls;
    private readonly ConcurrentDictionary<string, string[]> _installedVersions;

    public ManagedModuleInstallContext()
        : this(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new ConcurrentDictionary<string, ManagedModuleInstallPending>(StringComparer.OrdinalIgnoreCase),
            new ConcurrentDictionary<string, ManagedModuleInstallResult>(StringComparer.OrdinalIgnoreCase),
            new ConcurrentDictionary<string, string[]>(StringComparer.OrdinalIgnoreCase))
    {
    }

    private ManagedModuleInstallContext(
        HashSet<string> active,
        HashSet<string> ownedInstallKeys,
        ConcurrentDictionary<string, ManagedModuleInstallPending> inFlightInstalls,
        ConcurrentDictionary<string, ManagedModuleInstallResult> completedInstalls,
        ConcurrentDictionary<string, string[]> installedVersions)
    {
        _active = active;
        _ownedInstallKeys = ownedInstallKeys;
        _inFlightInstalls = inFlightInstalls;
        _completedInstalls = completedInstalls;
        _installedVersions = installedVersions;
    }

    public ManagedModuleInstallContext CreateBranch()
        => new(
            new HashSet<string>(_active, StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(_ownedInstallKeys, StringComparer.OrdinalIgnoreCase),
            _inFlightInstalls,
            _completedInstalls,
            _installedVersions);

    public IDisposable Enter(string moduleName)
    {
        var normalized = moduleName.Trim();
        if (!_active.Add(normalized))
            throw new InvalidOperationException($"Managed module dependency cycle detected for '{moduleName}'.");

        return new PopOnDispose(_active, normalized);
    }

    public bool TryBeginInstall(
        string key,
        out Task<ManagedModuleInstallResult> existingInstall,
        out ManagedModuleInstallPending pendingInstall,
        out bool runIndependently)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Install coalescing key is required.", nameof(key));

        runIndependently = false;
        while (true)
        {
            var pending = new ManagedModuleInstallPending(_inFlightInstalls, _ownedInstallKeys, key);
            if (_inFlightInstalls.TryAdd(key, pending))
            {
                _ownedInstallKeys.Add(key);
                existingInstall = pending.Task;
                pendingInstall = pending;
                return true;
            }

            if (_inFlightInstalls.TryGetValue(key, out var existing))
            {
                if (WouldCreateWaitCycle(existing))
                {
                    existingInstall = existing.Task;
                    pendingInstall = null!;
                    runIndependently = true;
                    return false;
                }

                existingInstall = existing.Task;
                pendingInstall = null!;
                return false;
            }
        }
    }

    public IDisposable EnterInstallWait(string key)
    {
        var ownedPending = GetOwnedPendingInstalls().ToArray();
        foreach (var pending in ownedPending)
            pending.SetWaitingFor(key);

        return new ClearWaitOnDispose(ownedPending);
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

    private static string CreateInstalledVersionKey(string moduleRoot, string moduleName)
        => string.Join("|", NormalizePath(moduleRoot), moduleName.Trim());

    private static string NormalizePath(string path)
        => Path.GetFullPath(path.Trim().Trim('"')).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    internal static string[] EnumerateInstalledVersions(string moduleRoot, string moduleName)
    {
        var moduleFolder = Path.Combine(moduleRoot, moduleName.Trim());
        if (!Directory.Exists(moduleFolder))
            return Array.Empty<string>();

        return Directory.EnumerateDirectories(moduleFolder)
            .Select(Path.GetFileName)
            .Where(static version => !string.IsNullOrWhiteSpace(version))
            .Select(static version => version!)
            .OrderBy(static version => version, ManagedModuleVersionComparer.Instance)
            .ToArray();
    }

    private bool WouldCreateWaitCycle(ManagedModuleInstallPending pending)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var waitingFor = pending.WaitingForKey;
        while (!string.IsNullOrWhiteSpace(waitingFor))
        {
            var currentKey = waitingFor!;
            if (_ownedInstallKeys.Contains(currentKey))
                return true;
            if (!visited.Add(currentKey))
                return true;
            if (!_inFlightInstalls.TryGetValue(currentKey, out var nextPending))
                return false;

            waitingFor = nextPending.WaitingForKey;
        }

        return false;
    }

    private IEnumerable<ManagedModuleInstallPending> GetOwnedPendingInstalls()
    {
        foreach (var ownedKey in _ownedInstallKeys)
        {
            if (_inFlightInstalls.TryGetValue(ownedKey, out var pending))
                yield return pending;
        }
    }

    private sealed class PopOnDispose : IDisposable
    {
        private readonly HashSet<string> _active;
        private readonly string _moduleName;

        public PopOnDispose(HashSet<string> active, string moduleName)
        {
            _active = active;
            _moduleName = moduleName;
        }

        public void Dispose()
        {
            _active.Remove(_moduleName);
        }
    }

    private sealed class ClearWaitOnDispose : IDisposable
    {
        private readonly IReadOnlyList<ManagedModuleInstallPending> _pendingInstalls;

        public ClearWaitOnDispose(IReadOnlyList<ManagedModuleInstallPending> pendingInstalls)
        {
            _pendingInstalls = pendingInstalls;
        }

        public void Dispose()
        {
            foreach (var pending in _pendingInstalls)
                pending.SetWaitingFor(null);
        }
    }
}

internal sealed class ManagedModuleInstallPending : IDisposable
{
    private readonly ConcurrentDictionary<string, ManagedModuleInstallPending> _owner;
    private readonly HashSet<string> _ownedInstallKeys;
    private readonly string _key;
    private readonly TaskCompletionSource<ManagedModuleInstallResult> _completion;
    private readonly object _syncRoot = new();
    private string? _waitingForKey;
    private bool _disposed;

    public ManagedModuleInstallPending(
        ConcurrentDictionary<string, ManagedModuleInstallPending> owner,
        HashSet<string> ownedInstallKeys,
        string key)
    {
        _owner = owner;
        _ownedInstallKeys = ownedInstallKeys;
        _key = key;
        _completion = new TaskCompletionSource<ManagedModuleInstallResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public Task<ManagedModuleInstallResult> Task => _completion.Task;

    public string? WaitingForKey
    {
        get
        {
            lock (_syncRoot)
            {
                return _waitingForKey;
            }
        }
    }

    public void SetWaitingFor(string? key)
    {
        lock (_syncRoot)
        {
            _waitingForKey = key;
        }
    }

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
        ((ICollection<KeyValuePair<string, ManagedModuleInstallPending>>)_owner)
            .Remove(new KeyValuePair<string, ManagedModuleInstallPending>(_key, this));
        _ownedInstallKeys.Remove(_key);
    }
}
