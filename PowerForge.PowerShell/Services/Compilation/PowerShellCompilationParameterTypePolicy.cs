using System.Management.Automation;

namespace PowerForge;

/// <summary>Classifies parameter types by the generated surface that can preserve them.</summary>
internal static class PowerShellCompilationParameterTypePolicy
{
    private static readonly HashSet<string> PowerShellHostTypeNames = new(StringComparer.Ordinal)
    {
        typeof(PSCredential).FullName!,
        typeof(PSObject).FullName!,
        typeof(PSCustomObject).FullName!,
        typeof(ScriptBlock).FullName!,
        typeof(SwitchParameter).FullName!
    };

    internal static PowerShellCompilationParameterTypeCapability Classify(Type type, string? targetFramework)
    {
        if (type.IsArray)
        {
            if (type.GetArrayRank() != 1)
                return PowerShellCompilationParameterTypeCapability.None;
            var element = Classify(type.GetElementType()!, targetFramework);
            if (!element.HasFlag(PowerShellCompilationParameterTypeCapability.ClrMethod))
                return PowerShellCompilationParameterTypeCapability.None;
            return PowerShellTypedExecutableParameterPolicy.IsSupported(type)
                ? element
                : element & ~PowerShellCompilationParameterTypeCapability.ProcessArgument;
        }

        var result = PowerShellCompilationParameterTypeCapability.None;
        if (PowerShellGeneratedTypePolicy.IsSupported(type, targetFramework))
            result |= PowerShellCompilationParameterTypeCapability.ClrMethod;
        else if (type.FullName is { } fullName && PowerShellHostTypeNames.Contains(fullName))
            result |= PowerShellCompilationParameterTypeCapability.ClrMethod |
                      PowerShellCompilationParameterTypeCapability.PowerShellHost;

        if (PowerShellTypedExecutableParameterPolicy.IsSupported(type))
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
}
