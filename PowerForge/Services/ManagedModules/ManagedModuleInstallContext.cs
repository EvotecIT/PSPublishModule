namespace PowerForge;

internal sealed class ManagedModuleInstallContext
{
    private readonly HashSet<string> _active;

    public ManagedModuleInstallContext()
        : this(new HashSet<string>(StringComparer.OrdinalIgnoreCase))
    {
    }

    private ManagedModuleInstallContext(HashSet<string> active)
    {
        _active = active;
    }

    public ManagedModuleInstallContext CreateBranch()
        => new(new HashSet<string>(_active, StringComparer.OrdinalIgnoreCase));

    public IDisposable Enter(string moduleName)
    {
        var normalized = moduleName.Trim();
        if (!_active.Add(normalized))
            throw new InvalidOperationException($"Managed module dependency cycle detected for '{moduleName}'.");

        return new PopOnDispose(_active, normalized);
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
}
