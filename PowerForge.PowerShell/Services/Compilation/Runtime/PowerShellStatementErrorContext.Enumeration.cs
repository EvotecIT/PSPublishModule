namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Collections;
    using System.Management.Automation;

    public sealed partial class PowerShellStatementErrorContext
    {
        /// <summary>Captures only the retained invocation's execution context for compiled loop boundaries.</summary>
        public static Action CreateLoopInterrupt(EngineIntrinsics engine)
        {
            if (engine is null) throw new ArgumentNullException(nameof(engine));
            var contract = NativeContract.Shared;
            var state = contract.GetRequired(contract.NativeSessionState, engine.SessionState);
            return CreateLoopInterrupt(contract, contract.GetRequired(contract.SessionExecutionContext, state));
        }

        internal static Action CreateLoopInterrupt(PSCmdlet cmdlet)
        {
            if (cmdlet is null) throw new ArgumentNullException(nameof(cmdlet));
            var contract = NativeContract.Shared;
            return CreateLoopInterrupt(contract, contract.GetRequired(contract.CmdletContext, cmdlet));
        }

        private static Action CreateLoopInterrupt(NativeContract contract, object context)
            => () => NativeContract.Invoke(contract.CheckEnumerationInterrupts, null, context);

        /// <summary>Acquires the native enumerator, including scalar fallback and acquisition-error semantics.</summary>
        internal IEnumerator? GetEnumerator(object value)
        {
            ThrowIfDisposed();
            return LanguagePrimitives.GetEnumerator(value);
        }

        /// <summary>Advances through the loaded host's cooperative stopping and enumeration-error contract.</summary>
        internal bool MoveEnumerator(IEnumerator cursor)
        {
            ThrowIfDisposed();
            // Native foreach advances before the loop-body interrupt check. An
            // empty or completed enumerator never enters that body boundary.
            var hasCurrent = cursor.MoveNext();
            if (hasCurrent) CheckLoopInterrupts();
            return hasCurrent;
        }

        /// <summary>Observes the loaded host's stop request at an authored loop boundary.</summary>
        internal void CheckLoopInterrupts()
        {
            ThrowIfDisposed();
            NativeContract.Invoke(_contract.CheckEnumerationInterrupts, null, _context);
        }

        /// <summary>Reads the stored record without performing CLR argument or output conversion.</summary>
        internal object? ReadEnumeratorCurrent(IEnumerator cursor)
        {
            ThrowIfDisposed();
            return cursor.Current;
        }
    }
}
