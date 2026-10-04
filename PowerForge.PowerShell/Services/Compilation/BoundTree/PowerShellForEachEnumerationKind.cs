namespace PowerForge;

/// <summary>Selects the enumeration and error contract before a foreach loop is lowered.</summary>
internal enum PowerShellForEachEnumerationKind
{
    TypedArray,
    StableScalar,
    ScalarString,
    SystemArray,
    PowerShellEnumerable,
    NativeInvocation
}
