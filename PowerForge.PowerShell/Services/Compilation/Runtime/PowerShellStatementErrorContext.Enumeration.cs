namespace PowerForge.Generated.Runtime
{
    using System.Collections;
    using System.Management.Automation;

    public sealed partial class PowerShellStatementErrorContext
    {
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
            NativeContract.Invoke(_contract.CheckEnumerationInterrupts, null, _context);
            return cursor.MoveNext();
        }

        /// <summary>Reads the stored record without performing CLR argument or output conversion.</summary>
        internal object? ReadEnumeratorCurrent(IEnumerator cursor)
        {
            ThrowIfDisposed();
            return cursor.Current;
        }
    }
}
