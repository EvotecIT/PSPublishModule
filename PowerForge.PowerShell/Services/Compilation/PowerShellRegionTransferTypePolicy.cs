namespace PowerForge;

/// <summary>
/// Defines CLR values that can cross a retained PowerShell and typed-region boundary without
/// changing their observable pipeline shape. Mutable reference values are admitted only where
/// the selector proves fresh initialization or read-only passthrough; this policy does not grant
/// mutation authority by itself.
/// </summary>
internal static class PowerShellRegionTransferTypePolicy
{
    internal static bool IsSupported(PowerShellTypeFact type)
        => type.Provenance == PowerShellTypeFactProvenance.Int32OrDouble || IsSupported(type.ClrType);

    internal static bool IsSupported(Type type)
        => PowerShellStableScalarTypePolicy.IsSupported(type) || IsAtomicDictionaryReference(type);

    internal static bool IsAtomicDictionaryReference(Type type)
        => type == typeof(System.Collections.Hashtable);
}
