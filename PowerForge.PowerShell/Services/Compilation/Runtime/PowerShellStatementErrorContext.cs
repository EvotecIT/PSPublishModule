namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Management.Automation;
    using System.Management.Automation.Language;

    /// <summary>
    /// Invocation-owned bridge from compiled statement errors to the qualified PowerShell host.
    /// Intended for embedding in generated command artifacts; runtime-independent targets do not use this type.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public sealed partial class PowerShellStatementErrorContext : IDisposable
    {
        private readonly NativeContract _contract;
        private readonly object _preferenceTuple;
        private readonly object _context;
        private readonly object _runtime;
        private readonly object _outputPipe;
        private readonly object _errorPipe;
        private readonly bool _mergeErrorToOutput;
        private readonly bool _redirectError;
        private readonly string _sourceName;
        private int _handlerDepth;
        private bool _disposed;

        internal PowerShellStatementErrorContext(PSCmdlet cmdlet, string sourceName)
        {
            _contract = NativeContract.Shared;
            if (cmdlet is null) throw new ArgumentNullException(nameof(cmdlet));
            _sourceName = sourceName ?? throw new ArgumentNullException(nameof(sourceName));
            _context = _contract.GetRequired(_contract.CmdletContext, cmdlet);
            _runtime = cmdlet.CommandRuntime;
            if (!_contract.CommandRuntimeType.IsInstanceOfType(_runtime))
                throw new NotSupportedException("Statement error continuation requires the native PowerShell command runtime.");
            _outputPipe = _contract.GetRequired(_contract.OutputPipe, _runtime);
            _errorPipe = _contract.GetRequired(_contract.ErrorOutputPipe, _runtime);
            _mergeErrorToOutput = Equals(_contract.ErrorMergeTo.GetValue(_runtime, null), _contract.MergeToOutput);
            _redirectError = (bool)_contract.GetRequired(_contract.IsRedirected, _errorPipe);
            // Preserve the raw inherited preference. In Windows PowerShell 5.1,
            // an inherited string and a bound ActionPreference can take different native paths.
            var preference = cmdlet.MyInvocation.BoundParameters.TryGetValue("ErrorAction", out var boundPreference)
                ? boundPreference
                : cmdlet.SessionState.PSVariable.GetValue("ErrorActionPreference", ActionPreference.Continue);
            _preferenceTuple = _contract.CreateTuple("ErrorActionPreference", preference);

            // A native cmdlet registers these lists during binding. A script function
            // owns them only while its clause runs, including cleanup during unwinding.
            NativeContract.Invoke(_contract.RemoveVariableLists, _runtime);
            NativeContract.Invoke(_contract.SetVariableLists, _runtime);
        }

        internal IDisposable EnterHandler()
        {
            ThrowIfDisposed();
            _handlerDepth++;
            return new HandlerScope(this);
        }

        internal void Handle(
            Exception error,
            string file,
            int line,
            int column,
            int endLine,
            int endColumn,
            string sourceText)
        {
            ThrowIfDisposed();
            var extent = CreateExtent(file, line, column, endLine, endColumn, sourceText);
            var function = NativeContract.Construct(_contract.FunctionContextConstructor);
            _contract.FunctionExecutionContext.SetValue(function, _context);
            _contract.FunctionOutputPipe.SetValue(function, _outputPipe);
            _contract.FunctionSequencePoints.SetValue(function, new IScriptExtent[] { extent });

            var sessionState = _contract.GetRequired(_contract.EngineSessionState, _context);
            var scope = NativeContract.Invoke(_contract.NewScope, sessionState, false)!;
            var previousErrorPipe = _contract.ShellErrorPipe.GetValue(_context, null);
            var previousPropagation = (bool)_contract.GetRequired(_contract.PropagateExceptions, _context);
            var scopeEntered = false;
            try
            {
                _contract.CurrentScope.SetValue(sessionState, scope, null);
                scopeEntered = true;
                _contract.ScopeLocalsTuple.SetValue(scope, _preferenceTuple, null);
                if (_mergeErrorToOutput)
                    _contract.ShellErrorPipe.SetValue(_context, _outputPipe, null);
                else if (_redirectError)
                    _contract.ShellErrorPipe.SetValue(_context, _errorPipe, null);
                if (_handlerDepth != 0)
                    _contract.PropagateExceptions.SetValue(_context, true, null);
                NativeContract.Invoke(_contract.CheckActionPreference, null, function, error);
            }
            finally
            {
                _contract.PropagateExceptions.SetValue(_context, previousPropagation, null);
                _contract.ShellErrorPipe.SetValue(_context, previousErrorPipe, null);
                if (scopeEntered) NativeContract.Invoke(_contract.RemoveScope, sessionState, scope);
            }
        }

        internal Exception LeaveCommand(RuntimeException error)
        {
            if (error.WasThrownFromThrowStatement) return error;
            var extent = error.ErrorRecord.InvocationInfo is null
                ? CreateExtent(string.Empty, 1, 1, 1, 1, string.Empty)
                : (IScriptExtent)_contract.GetRequired(_contract.InvocationScriptPosition, error.ErrorRecord.InvocationInfo);
            var function = NativeContract.Construct(_contract.FunctionInfoConstructor,
                _sourceName, ScriptBlock.Create(string.Empty), _context);
            var invocation = NativeContract.Construct(_contract.InvocationInfoConstructor, function, extent, _context);
            return (Exception)NativeContract.Construct(_contract.CommandExceptionConstructor, error, invocation);
        }

        private static IScriptExtent CreateExtent(string file, int line, int column, int endLine, int endColumn, string text)
        {
            var lines = (text ?? string.Empty).Replace("\r\n", "\n").Split('\n');
            return new ScriptExtent(
                new ScriptPosition(file, line, column, lines[0]),
                new ScriptPosition(file, endLine, endColumn, lines[lines.Length - 1]));
        }

        /// <summary>Releases only this invocation's stream-variable registrations.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            NativeContract.Invoke(_contract.RemoveVariableLists, _runtime);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PowerShellStatementErrorContext));
        }

        private sealed class HandlerScope : IDisposable
        {
            private PowerShellStatementErrorContext? _owner;
            internal HandlerScope(PowerShellStatementErrorContext owner) => _owner = owner;
            public void Dispose()
            {
                var owner = _owner;
                if (owner is null) return;
                _owner = null;
                owner._handlerDepth--;
            }
        }
    }
}
