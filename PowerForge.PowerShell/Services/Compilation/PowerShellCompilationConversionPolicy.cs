using System.Management.Automation.Language;

namespace PowerForge;

internal static class PowerShellCompilationConversionPolicy
{
    internal static bool CanLower(
        ConvertExpressionAst conversion,
        string? targetFramework,
        PowerShellCompilationCapability capabilities)
    {
        var targetType = conversion.StaticType;
        if (targetType == typeof(void) ||
            !PowerShellCompilationParameterTypePolicy.CanUseInMethod(targetType, targetFramework, capabilities))
            return false;

        return PowerShellCompilationLiteralPolicy.TryResolve(conversion, targetType, out _) ||
               capabilities.HasFlag(PowerShellCompilationCapability.PowerShellLanguageConversions);
    }
}
