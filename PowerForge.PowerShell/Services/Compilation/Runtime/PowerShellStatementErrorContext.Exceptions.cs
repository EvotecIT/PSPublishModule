namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Linq;
    using System.Management.Automation;

    public sealed partial class PowerShellStatementErrorContext
    {
        private Exception? _caughtException;

        /// <summary>Wraps arithmetic failures after both operands have completed evaluation.</summary>
        internal TResult EvaluateArithmetic<TLeft, TRight, TResult>(TLeft left, TRight right, Func<TLeft, TRight, TResult> operation)
        {
            ThrowIfDisposed();
            try { return operation(left, right); }
            catch (DivideByZeroException error) { throw new RuntimeException(error.Message, error); }
            catch (OverflowException error) { throw new RuntimeException(error.Message, error); }
        }

        /// <summary>Formats evaluated operands with the loaded host's culture and native FormatError wrapping.</summary>
        internal string FormatScalar(string format, object? value)
        {
            ThrowIfDisposed();
            return (string)NativeContract.Invoke(_contract.FormatOperator, null, format, value)!;
        }

        /// <summary>Converts a stored PowerShell value to the base object seen by a CLR Object parameter.</summary>
        internal object? UnwrapObjectArgument(object? value)
        {
            ThrowIfDisposed();
            return NativeContract.Invoke(_contract.UnwrapObjectArgument, null, value);
        }

        internal IDisposable EnterCatch(Exception error, ErrorRecord? record = null)
        {
            ThrowIfDisposed();
            var scope = new CatchScope(this, _caughtException, record);
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

        internal T ConvertArgument<T>(object value, string parameterName, string memberName)
        {
            ThrowIfDisposed();
            try
            {
                return (T)LanguagePrimitives.ConvertTo(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception error) when (IsOperationFailure(error))
            {
                NativeContract.Invoke(_contract.ConvertToArgumentConversionException, null,
                    error, parameterName, value, memberName, typeof(T));
                throw;
            }
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
            var matchedRecord = handler < 0 ? null : (ErrorRecord)NativeContract.Invoke(_contract.GetTupleValue, tuple, 0)!;
            // Method invocation can carry a base record whose exception is only
            // ParentContainsErrorRecordException. Catch-all $_ must expose the
            // actual invocation wrapper, but other records (including remoting
            // records with origin metadata) retain the native matcher's identity.
            caughtRecord = handler >= 0 && exceptionTypes[handler] is null &&
                runtimeError is MethodException &&
                runtimeError.ErrorRecord.GetType() == typeof(ErrorRecord) &&
                runtimeError.ErrorRecord.Exception is ParentContainsErrorRecordException
                ? new ErrorRecord(runtimeError.ErrorRecord, runtimeError)
                : matchedRecord;
            return handler < 0 ? -1 : clauseIndices[handler];
        }

        internal RuntimeException PrepareThrow(object? error, string file, int line, int column, int endLine, int endColumn, string text, bool rethrow = false)
        {
            var extent = CreateExtent(file, line, column, endLine, endColumn, text);
            return (RuntimeException)(_contract.ThrowConversionTakesRethrow
                ? NativeContract.Invoke(_contract.ConvertToThrownException, null, error, extent, rethrow)
                : NativeContract.Invoke(_contract.ConvertToThrownException, null, error, extent))!;
        }

        internal static bool IsOperationFailure(Exception error)
            => error is not PipelineStoppedException && error is not FlowControlException &&
               error is not PowerShellCapturedReturnSignal &&
               error is not StackOverflowException && error is not AccessViolationException;

        private sealed class CatchScope : IDisposable
        {
            private PowerShellStatementErrorContext? _owner;
            private readonly Exception? _previous;
            private readonly SessionState? _nativeSession;
            private readonly object? _previousUnder;
            internal CatchScope(PowerShellStatementErrorContext owner, Exception? previous, ErrorRecord? record)
            {
                _owner = owner;
                _previous = previous;
                if (owner._nativeFunction is not null && record is not null)
                {
                    _nativeSession = (SessionState)owner._contract.GetRequired(owner._contract.ContextSessionState, owner._context);
                    _previousUnder = _nativeSession.PSVariable.GetValue("_");
                    _nativeSession.PSVariable.Set("_", record);
                }
            }
            public void Dispose()
            {
                var owner = _owner;
                if (owner is null) return;
                _owner = null;
                owner._caughtException = _previous;
                _nativeSession?.PSVariable.Set("_", _previousUnder);
            }
        }
    }

    /// <summary>Transfers an authored return through a generated capture callback to its enclosing function.</summary>
    internal sealed class PowerShellCapturedReturnSignal : Exception
    {
    }
}
