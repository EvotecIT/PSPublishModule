namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Management.Automation;
    using System.Management.Automation.Language;

    public sealed partial class PowerShellStatementErrorContext
    {
        /// <summary>Uses the native function's scope, preferences, and stream registrations for compiled statements.</summary>
        internal static PowerShellStatementErrorContext CreateNativeFunction(object functionContext, string sourceName)
            => new PowerShellStatementErrorContext(functionContext, sourceName);

        private PowerShellStatementErrorContext(object functionContext, string sourceName)
        {
            if (functionContext is null) throw new ArgumentNullException(nameof(functionContext));
            _contract = NativeContract.Shared;
            _nativeFunction = functionContext;
            _sourceName = sourceName ?? throw new ArgumentNullException(nameof(sourceName));
            _context = _contract.FunctionExecutionContext.GetValue(functionContext)
                ?? throw new NotSupportedException("The native function execution context is unavailable.");
            var processor = _contract.GetRequired(_contract.CurrentCommandProcessor, _context);
            _runtime = _contract.GetRequired(_contract.ProcessorRuntime, processor);
            _outputPipe = _contract.FunctionOutputPipe.GetValue(functionContext)
                ?? throw new NotSupportedException("The native function output pipe is unavailable.");
            _errorPipe = _contract.GetRequired(_contract.ErrorOutputPipe, _runtime);
            _mergeErrorToOutput = Equals(_contract.ErrorMergeTo.GetValue(_runtime, null), _contract.MergeToOutput);
            _redirectError = (bool)_contract.GetRequired(_contract.IsRedirected, _errorPipe);
            _ownsVariableLists = true;
            // The native error path reads live preferences. This tuple is reserved for the existing direct-call adapter.
            _preferenceTuple = _contract.CreateTuple("ErrorActionPreference", ActionPreference.Continue);
        }

        private void HandleNativeFunction(Exception error, IScriptExtent extent)
        {
            var previousPoints = _contract.FunctionSequencePoints.GetValue(_nativeFunction);
            var previousIndex = _contract.FunctionCurrentSequencePointIndex.GetValue(_nativeFunction);
            var previousPropagation = _contract.GetRequired(_contract.PropagateExceptions, _context);
            try
            {
                _contract.FunctionSequencePoints.SetValue(_nativeFunction, new IScriptExtent[] { extent });
                _contract.FunctionCurrentSequencePointIndex.SetValue(_nativeFunction, 0);
                if (_handlerDepth != 0) _contract.PropagateExceptions.SetValue(_context, true, null);
                NativeContract.Invoke(_contract.CheckActionPreference, null, _nativeFunction, error);
            }
            finally
            {
                _contract.PropagateExceptions.SetValue(_context, previousPropagation, null);
                _contract.FunctionCurrentSequencePointIndex.SetValue(_nativeFunction, previousIndex);
                _contract.FunctionSequencePoints.SetValue(_nativeFunction, previousPoints);
            }
        }
    }
}
