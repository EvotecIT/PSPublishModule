namespace PowerForge.Generated.Runtime
{
    using System;

    public sealed partial class PowerShellStatementErrorContext
    {
        /// <summary>Suspends and restores native pipeline stopping around authored finally code.</summary>
        internal IDisposable EnterFinally()
        {
            ThrowIfDisposed();
            return new FinallyScope(this);
        }

        private sealed class FinallyScope : IDisposable
        {
            private PowerShellStatementErrorContext? _owner;
            private readonly bool _wasStopping;

            internal FinallyScope(PowerShellStatementErrorContext owner)
            {
                _owner = owner;
                _wasStopping = (bool)NativeContract.Invoke(owner._contract.SuspendStoppingPipeline, null, owner._context)!;
            }

            public void Dispose()
            {
                var owner = _owner;
                if (owner is null) return;
                _owner = null;
                NativeContract.Invoke(owner._contract.RestoreStoppingPipeline, null, owner._context, _wasStopping);
            }
        }
    }
}
