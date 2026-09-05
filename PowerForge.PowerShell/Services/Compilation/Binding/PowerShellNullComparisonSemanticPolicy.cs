using System.Collections;

namespace PowerForge;

/// <summary>Owns the scalar/null boundary shared by equality binding and short-circuit flow refinement.</summary>
internal static class PowerShellNullComparisonSemanticPolicy
{
    internal static bool IsScalar(Type comparedType, bool comparedValueIsLeft)
    {
        if (!comparedValueIsLeft) return true;
        var runtimeType = Nullable.GetUnderlyingType(comparedType) ?? comparedType;
        return runtimeType == typeof(string) ||
               comparedType.IsSealed && !typeof(IEnumerable).IsAssignableFrom(runtimeType);
    }
}
