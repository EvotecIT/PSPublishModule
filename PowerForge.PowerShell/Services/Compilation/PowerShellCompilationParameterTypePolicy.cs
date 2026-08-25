using System.Management.Automation;

namespace PowerForge;

/// <summary>Classifies parameter types by the generated surface that can preserve them.</summary>
internal static class PowerShellCompilationParameterTypePolicy
{
    private static readonly HashSet<Type> PowerShellHostTypes = new()
    {
        typeof(PSCredential),
        typeof(PSObject),
        typeof(PSCustomObject),
        typeof(ScriptBlock)
    };

    internal static PowerShellCompilationParameterTypeCapability Classify(Type type, string? targetFramework)
    {
        var compiledType = GetCompiledType(type);
        if (compiledType.IsArray)
        {
            if (compiledType.GetArrayRank() != 1)
                return PowerShellCompilationParameterTypeCapability.None;
            var element = Classify(compiledType.GetElementType()!, targetFramework);
            return element.HasFlag(PowerShellCompilationParameterTypeCapability.ClrMethod)
                ? element
                : PowerShellCompilationParameterTypeCapability.None;
        }

        var result = PowerShellCompilationParameterTypeCapability.None;
        if (PowerShellGeneratedTypePolicy.IsSupported(compiledType, targetFramework))
            result |= PowerShellCompilationParameterTypeCapability.ClrMethod;
        else if (PowerShellHostTypes.Contains(compiledType))
            result |= PowerShellCompilationParameterTypeCapability.ClrMethod |
                      PowerShellCompilationParameterTypeCapability.PowerShellHost;

        if (PowerShellTypedExecutableParameterPolicy.IsSupported(compiledType))
            result |= PowerShellCompilationParameterTypeCapability.ProcessArgument;
        return result;
    }

    internal static bool CanUseInMethod(
        Type type,
        string? targetFramework,
        PowerShellCompilationCapability capabilities)
    {
        var classified = Classify(type, targetFramework);
        if (!classified.HasFlag(PowerShellCompilationParameterTypeCapability.ClrMethod))
            return false;
        return !classified.HasFlag(PowerShellCompilationParameterTypeCapability.PowerShellHost) ||
               capabilities.HasFlag(PowerShellCompilationCapability.PowerShellHostTypes);
    }

    internal static Type GetCompiledType(Type type)
        => type == typeof(SwitchParameter) ? typeof(bool) : type;
}
