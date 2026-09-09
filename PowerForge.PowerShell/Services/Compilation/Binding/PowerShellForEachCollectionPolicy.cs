namespace PowerForge;

/// <summary>Shares foreach element storage and host admission between local discovery and loop binding.</summary>
internal static class PowerShellForEachCollectionPolicy
{
    internal static Type? GetElementType(Type? collectionType, PowerShellCompilationCapability capabilities,
        out PowerShellForEachEnumerationKind kind)
    {
        if (capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding))
        {
            kind = PowerShellForEachEnumerationKind.NativeInvocation;
            return typeof(object);
        }
        kind = PowerShellForEachEnumerationKind.TypedArray;
        if (collectionType is null) return null;
        if (collectionType.IsArray && collectionType.GetArrayRank() == 1) return collectionType.GetElementType();
        if (collectionType == typeof(string))
        {
            kind = PowerShellForEachEnumerationKind.ScalarString;
            return typeof(string);
        }
        if (!PowerShellCompilationParameterTypePolicy.CanUseUntypedObject(capabilities)) return null;
        if (collectionType == typeof(Array))
        {
            kind = PowerShellForEachEnumerationKind.SystemArray;
            return typeof(object);
        }
        if (capabilities.HasFlag(PowerShellCompilationCapability.PowerShellStatementErrors) &&
            (typeof(System.Collections.IEnumerable).IsAssignableFrom(collectionType) ||
             typeof(System.Collections.IEnumerator).IsAssignableFrom(collectionType)))
        {
            kind = PowerShellForEachEnumerationKind.PowerShellEnumerable;
            return typeof(object);
        }
        return null;
    }
}
