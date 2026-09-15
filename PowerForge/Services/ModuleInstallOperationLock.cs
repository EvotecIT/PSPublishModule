namespace PowerForge;

/// <summary>
/// Holds the per-root module install locks in a stable order so AutoRevision resolution,
/// manifest finalization, and delivery form one cross-process operation.
/// </summary>
internal sealed class ModuleInstallOperationLock : IDisposable
{
    private readonly ManagedModuleInstallLock[] _locks;

    private ModuleInstallOperationLock(
        ManagedModuleInstallLock[] locks,
        string[] lockedRoots,
        string[] failures)
    {
        _locks = locks;
        LockedRoots = lockedRoots;
        Failures = failures;
    }

    internal IReadOnlyList<string> LockedRoots { get; }

    internal IReadOnlyList<string> Failures { get; }

    internal static ModuleInstallOperationLock Acquire(
        IEnumerable<string>? roots,
        string moduleName,
        bool requireAllRoots)
    {
        var acquired = new List<ManagedModuleInstallLock>();
        var lockedRoots = new List<string>();
        var failures = new List<string>();
        try
        {
            foreach (var root in ModuleInstaller.ResolveDestinationRoots(roots)
                         .OrderBy(static path => path, FrameworkCompatibility.PathComparer))
            {
                try
                {
                    acquired.Add(ManagedModuleInstallLock.Acquire(root, moduleName, CancellationToken.None));
                    lockedRoots.Add(root);
                }
                catch (Exception ex) when (!requireAllRoots)
                {
                    failures.Add($"{root}: {ex.Message}");
                }
            }

            if (lockedRoots.Count == 0)
            {
                throw new UnauthorizedAccessException(
                    $"Failed to acquire an install lock for any module root. Errors: {string.Join("; ", failures)}");
            }

            return new ModuleInstallOperationLock(
                acquired.ToArray(),
                lockedRoots.ToArray(),
                failures.ToArray());
        }
        catch
        {
            for (var index = acquired.Count - 1; index >= 0; index--)
                acquired[index].Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        for (var index = _locks.Length - 1; index >= 0; index--)
            _locks[index].Dispose();
    }
}
