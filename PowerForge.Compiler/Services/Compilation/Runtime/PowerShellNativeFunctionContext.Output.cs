namespace PowerForge.Generated.Runtime
{
    using System;

    public sealed partial class PowerShellNativeFunctionContext
    {
        /// <summary>Temporarily routes native pipeline records to the enclosing compiled success sink.</summary>
        public IDisposable RedirectOutput(Action<object?> sink)
        {
            EnsureActive();
            if (sink is null) throw new ArgumentNullException(nameof(sink));
            return new NativeOutputScope(this, sink);
        }

        private sealed class NativeOutputScope : IDisposable
        {
            private readonly PowerShellNativeFunctionContext _owner;
            private readonly object _previous;
            private readonly PowerShellNativeOutput.SuccessSinkWriter _writer;
            private bool _disposed;

            internal NativeOutputScope(PowerShellNativeFunctionContext owner, Action<object?> sink)
            {
                _owner = owner;
                _previous = owner._contract.OutputPipe.GetValue(owner.FunctionContext)!;
                _writer = new PowerShellNativeOutput.SuccessSinkWriter(sink);
                var contract = PowerShellNativeOutput.Shared.Value;
                var pipe = contract.CreatePipe();
                contract.ExternalWriter.SetValue(pipe, _writer, null);
                contract.SetTemporaryVariableLists(_previous, pipe);
                owner._contract.OutputPipe.SetValue(owner.FunctionContext, pipe);
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                try { _owner._contract.OutputPipe.SetValue(_owner.FunctionContext, _previous); }
                finally { _writer.Close(); }
            }
        }

        /// <summary>Applies the native single-expression array operator without a statement-output collector.</summary>
        public object?[] CollectValue(object? value)
        {
            EnsureActive();
            return PowerShellNativeOutput.Shared.Value.Collect(value);
        }

        /// <summary>Applies native implicit-output enumeration and copying to the current compiled success sink.</summary>
        public void WriteOutput(object? value, Action<object?> sink)
        {
            EnsureActive();
            PowerShellNativeOutput.Write(value, sink, _executionContext);
        }
    }
}
