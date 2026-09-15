namespace PowerForge;

/// <summary>
/// Holds the per-root module install locks in a stable order so AutoRevision resolution,
/// manifest finalization, and delivery form one cross-process operation.
/// </summary>
internal sealed class ModuleInstallOperationLock : IDisposable
{
    private readonly ManagedModuleInstallLock[] _locks;

    private ModuleInstallOperationLock(ManagedModuleInstallLock[] locks)
    {
        _locks = locks;
    }

    internal static ModuleInstallOperationLock Acquire(IEnumerable<string>? roots, string moduleName)
    {
        var acquired = new List<ManagedModuleInstallLock>();
        try
        {
            foreach (var root in ModuleInstaller.ResolveDestinationRoots(roots)
                         .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
            {
                acquired.Add(ManagedModuleInstallLock.Acquire(root, moduleName, CancellationToken.None));
            }

            return new ModuleInstallOperationLock(acquired.ToArray());
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
