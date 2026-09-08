namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Management.Automation;
    using System.Management.Automation.Language;

    /// <summary>Provides compiled clauses with the active native invocation's variable and output owners.</summary>
    public sealed partial class PowerShellNativeFunctionContext : IDisposable
    {
        private readonly SessionState _session;
        private readonly object _executionContext;
        private readonly PowerShellNativeFunctionHost.NativeContract _contract;
        private readonly object _pipe;
        private readonly ICommandRuntime2 _runtime;
        private bool _disposed;
        private readonly bool _optimized;
        internal object FunctionContext { get; }

        internal PowerShellNativeFunctionContext(object functionContext, bool optimized)
        {
            FunctionContext = functionContext;
            _optimized = optimized;
            _contract = PowerShellNativeFunctionHost.NativeContract.Shared;
            _executionContext = _contract.ExecutionContext.GetValue(functionContext)
                ?? throw new NotSupportedException("PowerShell's function execution context is unavailable.");
            _session = _contract.SessionState.GetValue(_executionContext, null) as SessionState
                ?? throw new NotSupportedException("PowerShell's native session is unavailable.");
            _pipe = _contract.OutputPipe.GetValue(functionContext)
                ?? throw new NotSupportedException("PowerShell's function output pipe is unavailable.");
            var processor = _contract.CurrentCommandProcessor.GetValue(_executionContext, null);
            _runtime = (processor is null ? null : _contract.ProcessorRuntime.GetValue(processor, null)) as ICommandRuntime2
                ?? throw new NotSupportedException("PowerShell's native command runtime is unavailable.");
        }

        /// <summary>Reads the actual variable value, including changes made by binding callbacks.</summary>
        public object? GetVariable(string name)
            => GetVariable(name, false, string.Empty, 1, 1, 1, name.Length + 2, "$" + name);

        /// <summary>Reads with native strict-mode rules and the authored variable's source and interpolation context.</summary>
        public object? GetVariable(string name, bool inExpandableString, string file, int line, int column,
            int endLine, int endColumn, string sourceText)
            => GetVariable(name, inExpandableString, file, line, column, endLine, endColumn, sourceText, false);

        /// <summary>Reads a native local slot directly only where native flow analysis selects optimized storage.</summary>
        public object? GetVariable(string name, bool inExpandableString, string file, int line, int column,
            int endLine, int endColumn, string sourceText, bool directLocal)
        {
            EnsureActive();
            if (directLocal && TryReadLocalValue(name, out var localValue)) return localValue;
            var extent = PowerShellSourceExtent.Create(file, line, column, endLine, endColumn, sourceText);
            var variable = new VariableExpressionAst(extent, name, splatted: false);
            if (inExpandableString)
                // This metadata-only parent preserves StrictMode 1's interpolation exception. It is never evaluated.
                _contract.InterpolationContext.Invoke(new object[] { extent, "$value", "{0}", StringConstantType.DoubleQuoted,
                    new ExpressionAst[] { variable } });
            return PowerShellNativeFunctionHost.Invoke(_contract.GetVariableValue, null,
                new object[] { variable.VariablePath, _executionContext, variable });
        }

        private bool TryReadLocalValue(string name, out object? value)
        {
            value = null;
            if (!_optimized) return false;
            var localName = name.StartsWith("local:", StringComparison.OrdinalIgnoreCase) ? name.Substring(6) : name;
            var tuple = _contract.LocalsTuple.GetValue(FunctionContext);
            var arguments = new object[] { localName, true, null! };
            if (!(bool)PowerShellNativeFunctionHost.Invoke(_contract.TryGetLocalVariable, tuple, arguments)!) return false;
            // The target tuple, including AllScope exceptions, takes precedence over build-host annotations.
            value = ((PSVariable)arguments[2]).Value;
            return true;
        }

        /// <summary>Writes through the native variable owner and its current constraints.</summary>
        public void SetVariable(string name, object? value)
        {
            EnsureActive();
            _session.PSVariable.Set(name, value);
        }

        /// <summary>Completes a compiled statement's explicit native execution-status transition.</summary>
        public void SetExecutionStatus(bool succeeded)
        {
            EnsureActive();
            _contract.ExecutionStatus.SetValue(_executionContext, succeeded, null);
        }

        /// <summary>Writes one value to the active native output pipe without adding enumeration.</summary>
        public void WriteValue(object? value)
        {
            EnsureActive();
            PowerShellNativeFunctionHost.Invoke(_contract.AddOutput, _pipe, new object[] { value! });
        }

        /// <summary>Writes a verbose record through the invocation's native command runtime.</summary>
        public void WriteVerbose(string message) { EnsureActive(); _runtime.WriteVerbose(message); }

        /// <summary>Writes a debug record through the invocation's native command runtime.</summary>
        public void WriteDebug(string message) { EnsureActive(); _runtime.WriteDebug(message); }

        /// <summary>Writes a warning record through the invocation's native command runtime.</summary>
        public void WriteWarning(string message) { EnsureActive(); _runtime.WriteWarning(message); }

        /// <summary>Writes an information record through the invocation's native command runtime.</summary>
        public void WriteInformation(string message)
        {
            EnsureActive();
            _runtime.WriteInformation(new InformationRecord(message, "PowerForge.Compiled"));
        }

        /// <summary>Writes a tagged host-information record through the native information stream.</summary>
        public void WriteHost(string message)
        {
            EnsureActive();
            var record = new InformationRecord(new HostInformationMessage { Message = message, NoNewLine = false }, "Write-Host");
            record.Tags.Add("PSHOST");
            _runtime.WriteInformation(record);
        }

        /// <summary>Writes an error record using the compiled stream operation's identity.</summary>
        public void WriteError(string message)
        {
            EnsureActive();
            _runtime.WriteError(new ErrorRecord(new InvalidOperationException(message),
                "PowerForge.CompiledCommandError", ErrorCategory.NotSpecified, null));
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
