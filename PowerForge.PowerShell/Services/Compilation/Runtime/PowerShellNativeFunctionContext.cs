namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Management.Automation;

    /// <summary>Provides compiled clauses with the active native invocation's variable and output owners.</summary>
    public sealed class PowerShellNativeFunctionContext : IDisposable
    {
        private readonly SessionState _session;
        private readonly object _executionContext;
        private readonly PowerShellNativeFunctionHost.NativeContract _contract;
        private readonly object _pipe;
        private bool _disposed;

        internal PowerShellNativeFunctionContext(object functionContext)
        {
            _contract = PowerShellNativeFunctionHost.NativeContract.Shared;
            _executionContext = _contract.ExecutionContext.GetValue(functionContext)
                ?? throw new NotSupportedException("PowerShell's function execution context is unavailable.");
            _session = _contract.SessionState.GetValue(_executionContext, null) as SessionState
                ?? throw new NotSupportedException("PowerShell's native session is unavailable.");
            _pipe = _contract.OutputPipe.GetValue(functionContext)
                ?? throw new NotSupportedException("PowerShell's function output pipe is unavailable.");
        }

        /// <summary>Reads the actual variable value, including changes made by binding callbacks.</summary>
        public object? GetVariable(string name)
        {
            EnsureActive();
            return _session.PSVariable.GetValue(name);
        }

        /// <summary>Writes through the native variable owner and its current constraints.</summary>
        public void SetVariable(string name, object? value)
        {
            EnsureActive();
            _session.PSVariable.Set(name, value);
        }

        /// <summary>Writes one value to the active native output pipe without adding enumeration.</summary>
        public void WriteValue(object? value)
        {
            EnsureActive();
            PowerShellNativeFunctionHost.Invoke(_contract.AddOutput, _pipe, new object[] { value! });
        }

        /// <summary>Uses native stringification while callbacks observe the active function's variables.</summary>
        public string Stringify(object? value)
        {
            EnsureActive();
            return (string)PowerShellNativeFunctionHost.Invoke(_contract.Stringify, null, new object[] { _executionContext, value! })!;
        }

        /// <summary>Prevents a captured context from accessing a later invocation.</summary>
        public void Dispose() => _disposed = true;

        private void EnsureActive()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PowerShellNativeFunctionContext));
        }
    }
}
