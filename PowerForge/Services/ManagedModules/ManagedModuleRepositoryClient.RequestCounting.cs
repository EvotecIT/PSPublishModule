namespace PowerForge;

public sealed partial class ManagedModuleRepositoryClient
{
    private readonly System.Threading.AsyncLocal<RepositoryRequestScope?> _requestScope = new();

    internal RepositoryRequestScope BeginRequestScope()
    {
        var parent = _requestScope.Value;
        var scope = new RepositoryRequestScope(this, parent);
        _requestScope.Value = scope;
        return scope;
    }

    private void RecordRequestAttempt()
    {
        System.Threading.Interlocked.Increment(ref _requestCount);
        for (var scope = _requestScope.Value; scope is not null; scope = scope.Parent)
            scope.Increment();
    }

    private void RecordRedirectFollowed()
    {
        for (var scope = _requestScope.Value; scope is not null; scope = scope.Parent)
            scope.IncrementRedirect();
    }

    internal sealed class RepositoryRequestScope : IDisposable
    {
        private readonly ManagedModuleRepositoryClient _owner;
        private readonly RepositoryRequestScope? _parent;
        private long _count;
        private long _redirectCount;
        private bool _disposed;

        internal RepositoryRequestScope(ManagedModuleRepositoryClient owner, RepositoryRequestScope? parent)
        {
            _owner = owner;
            _parent = parent;
        }

        internal RepositoryRequestScope? Parent => _parent;

        internal long Count => System.Threading.Interlocked.Read(ref _count);

        internal long RedirectCount => System.Threading.Interlocked.Read(ref _redirectCount);

        internal void Increment()
            => System.Threading.Interlocked.Increment(ref _count);

        internal void IncrementRedirect()
            => System.Threading.Interlocked.Increment(ref _redirectCount);

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (ReferenceEquals(_owner._requestScope.Value, this))
                _owner._requestScope.Value = _parent;
        }
    }
}
