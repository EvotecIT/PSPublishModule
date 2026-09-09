namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Management.Automation.Language;

    public sealed partial class PowerShellStatementErrorContext
    {
        private PowerShellStatementErrorContext(PowerShellStatementErrorContext parent, string sourceName, IScriptExtent? invocationExtent)
        {
            _contract = parent._contract;
            _context = parent._context;
            _runtime = parent._runtime;
            _outputPipe = parent._outputPipe;
            _errorPipe = parent._errorPipe;
            _mergeErrorToOutput = parent._mergeErrorToOutput;
            _redirectError = parent._redirectError;
            _sourceName = sourceName ?? throw new ArgumentNullException(nameof(sourceName));
            _invocationExtent = invocationExtent;
            _handlerDepth = parent._handlerDepth;
            _preferenceTuple = _contract.CreateTuple("ErrorActionPreference",
                NativeContract.Invoke(_contract.GetTupleValue, parent._preferenceTuple, 0));
        }

        /// <summary>Creates the nested command's preference and identity scope without changing its caller's stream registrations.</summary>
        internal PowerShellStatementErrorContext EnterFunction(string sourceName)
        {
            ThrowIfDisposed();
            return new PowerShellStatementErrorContext(this, sourceName, null);
        }

        internal PowerShellStatementErrorContext EnterFunction(string sourceName, string file, int line, int column,
            int endLine, int endColumn, string text)
        {
            ThrowIfDisposed();
            return new PowerShellStatementErrorContext(this, sourceName, CreateExtent(file, line, column, endLine, endColumn, text));
        }
    }
}
