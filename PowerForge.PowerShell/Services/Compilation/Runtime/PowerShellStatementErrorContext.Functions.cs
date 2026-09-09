namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Management.Automation.Language;

    public sealed partial class PowerShellStatementErrorContext
    {
        private PowerShellStatementErrorContext(PowerShellStatementErrorContext parent, string sourceName, IScriptExtent? invocationExtent,
            object? capturedOutputPipe = null)
        {
            _contract = parent._contract;
            _context = parent._context;
            _runtime = parent._runtime;
            _outputPipe = capturedOutputPipe ?? parent._outputPipe;
            // An outer 2>&1 targets the caller's pipe. Capturing this command's
            // success records must not move that inherited error destination.
            _errorPipe = capturedOutputPipe is not null && parent._mergeErrorToOutput ? parent._outputPipe : parent._errorPipe;
            _mergeErrorToOutput = capturedOutputPipe is null && parent._mergeErrorToOutput;
            _redirectError = parent._redirectError || capturedOutputPipe is not null && parent._mergeErrorToOutput;
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

        /// <summary>Captures a local command's success records while retaining its caller's error destination.</summary>
        internal object? CaptureFunction(string sourceName, string file, int line, int column,
            int endLine, int endColumn, string text, Action<PowerShellStatementErrorContext, Action<object?>> invoke)
        {
            ThrowIfDisposed();
            if (invoke is null) throw new ArgumentNullException(nameof(invoke));
            var records = new System.Collections.Generic.List<object?>();
            var writer = new PowerShellNativeOutput.SuccessSinkWriter(records.Add);
            var contract = PowerShellNativeOutput.Shared.Value;
            var pipe = contract.CreatePipe();
            contract.ExternalWriter.SetValue(pipe, writer, null);
            try
            {
                using var child = new PowerShellStatementErrorContext(this, sourceName,
                    CreateExtent(file, line, column, endLine, endColumn, text), pipe);
                try { invoke(child, records.Add); }
                catch (Exception error) when (IsOperationFailure(error)) { throw child.LeaveCommand(error); }
                return records.Count == 0 ? System.Management.Automation.Internal.AutomationNull.Value :
                    records.Count == 1 ? records[0] : records.ToArray();
            }
            finally { writer.Close(); }
        }
    }
}
