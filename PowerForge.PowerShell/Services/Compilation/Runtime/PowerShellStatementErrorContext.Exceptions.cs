namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Linq;
    using System.Management.Automation;

    public sealed partial class PowerShellStatementErrorContext
    {
        private Exception? _caughtException;

        internal IDisposable EnterCatch(Exception error)
        {
            ThrowIfDisposed();
            var scope = new CatchScope(this, _caughtException);
            _caughtException = error;
            return scope;
        }

        internal RuntimeException PrepareRethrow(string file, int line, int column, int endLine, int endColumn, string text)
            => PrepareThrow(_caughtException ?? throw new InvalidOperationException("A rethrow requires an active catch."),
                file, line, column, endLine, endColumn, text, rethrow: true);

        internal RuntimeException NullInvocation()
            => (RuntimeException)NativeContract.Invoke(_contract.NewInterpreterException, null,
                null, typeof(RuntimeException), null, "InvokeMethodOnNull",
                _contract.GetRequired(_contract.NullInvocationResource, null), Array.Empty<object>())!;

        internal Exception WrapInvocation(Exception error, string memberName, int argumentCount)
        {
            try
            {
                NativeContract.Invoke(_contract.ConvertToMethodInvocationException, null,
                    error, typeof(MethodException), memberName, argumentCount, null);
            }
            catch (Exception converted) when (IsOperationFailure(converted))
            {
                return converted;
            }
            // Native conversion returns when the original exception must be preserved.
            return error;
        }

        internal int FindCatch(Exception error, Type?[] exceptionTypes, int[] clauseIndices, out ErrorRecord? caughtRecord)
        {
            if (exceptionTypes.Length != clauseIndices.Length)
                throw new ArgumentException("Every catch type must map to an authored clause.", nameof(clauseIndices));
            var runtimeError = error as RuntimeException ?? (RuntimeException)NativeContract.Invoke(
                _contract.ConvertToRuntimeException, null, error, CreateExtent(string.Empty, 1, 1, 1, 1, string.Empty))!;
            var nativeTypes = exceptionTypes.Select(type => type ?? _contract.CatchAllType).ToArray();
            var tuple = _contract.CreateTuple("_", null);
            var handler = (int)NativeContract.Invoke(_contract.FindMatchingHandler, null,
                tuple, runtimeError, nativeTypes, _context)!;
            caughtRecord = handler < 0 ? null : (ErrorRecord)NativeContract.Invoke(_contract.GetTupleValue, tuple, 0)!;
            return handler < 0 ? -1 : clauseIndices[handler];
        }

        internal RuntimeException PrepareThrow(Exception error, string file, int line, int column, int endLine, int endColumn, string text, bool rethrow = false)
        {
            var extent = CreateExtent(file, line, column, endLine, endColumn, text);
            return (RuntimeException)(_contract.ThrowConversionTakesRethrow
                ? NativeContract.Invoke(_contract.ConvertToThrownException, null, error, extent, rethrow)
                : NativeContract.Invoke(_contract.ConvertToThrownException, null, error, extent))!;
        }

        internal static bool IsOperationFailure(Exception error)
            => error is not PipelineStoppedException && error is not FlowControlException &&
               error is not StackOverflowException && error is not AccessViolationException;

        private sealed class CatchScope : IDisposable
        {
            private PowerShellStatementErrorContext? _owner;
            private readonly Exception? _previous;
            internal CatchScope(PowerShellStatementErrorContext owner, Exception? previous) { _owner = owner; _previous = previous; }
            public void Dispose()
            {
                var owner = _owner;
                if (owner is null) return;
                _owner = null;
                owner._caughtException = _previous;
            }
        }
    }
}
