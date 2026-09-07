namespace PowerForge.Generated.Runtime
{
    using System;

    public sealed partial class PowerShellStatementErrorContext
    {
        private PowerShellStatementErrorContext(PowerShellStatementErrorContext parent, string sourceName)
        {
            _contract = parent._contract;
            _context = parent._context;
            _runtime = parent._runtime;
            _outputPipe = parent._outputPipe;
            _errorPipe = parent._errorPipe;
            _mergeErrorToOutput = parent._mergeErrorToOutput;
            _redirectError = parent._redirectError;
            _sourceName = sourceName ?? throw new ArgumentNullException(nameof(sourceName));
            _handlerDepth = parent._handlerDepth;
            _preferenceTuple = _contract.CreateTuple("ErrorActionPreference",
                NativeContract.Invoke(_contract.GetTupleValue, parent._preferenceTuple, 0));
        }

        /// <summary>Creates the nested command's preference and identity scope without changing its caller's stream registrations.</summary>
        internal PowerShellStatementErrorContext EnterFunction(string sourceName)
        {
            ThrowIfDisposed();
            return new PowerShellStatementErrorContext(this, sourceName);
        }
    }
}
