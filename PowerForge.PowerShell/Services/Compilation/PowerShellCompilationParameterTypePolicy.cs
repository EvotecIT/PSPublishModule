using System.Management.Automation;
using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Classifies parameter types by the generated surface that can preserve them.</summary>
internal static class PowerShellCompilationParameterTypePolicy
{
    internal static TypeConstraintAst? FindUnresolvedAuthoredType(ParameterAst parameter)
        => parameter.Attributes.OfType<TypeConstraintAst>()
            .FirstOrDefault(static constraint => constraint.TypeName.GetReflectionType() is null);

    internal static bool IsHostProvidedParameterType(string name)
        // Microsoft.PowerShell.Commands.Utility supplies this public type on both
        // supported PowerShell hosts even when the compiler process has not loaded it.
        => name.Equals("Microsoft.PowerShell.Commands.WebRequestSession", StringComparison.OrdinalIgnoreCase);

    private static readonly HashSet<string> PowerShellHostTypeNames = new(StringComparer.Ordinal)
    {
        typeof(PSCredential).FullName!,
        typeof(PSObject).FullName!,
        typeof(PSCustomObject).FullName!,
        typeof(ScriptBlock).FullName!,
        typeof(SwitchParameter).FullName!
    };

    // These SDK data contracts exist on both supported hosts. They require the
    // artifact's PowerShell reference, not a runtime-free CLR target. Match the
    // actual SDK Type identities rather than admitting arbitrary same-name types.
    private static readonly HashSet<Type> PowerShellHostDataTypes = new()
    {
        typeof(PSSerializer),
        typeof(ErrorRecord),
        typeof(ActionPreferenceStopException),
        typeof(ErrorCategory),
        typeof(PSMemberTypes),
        typeof(CommandInfo)
    };

    internal static bool IsQualifiedHostDataType(Type type, string? targetFramework = null)
        => type.IsArray && type.GetArrayRank() == 1
            ? IsQualifiedHostDataType(type.GetElementType()!, targetFramework)
            : PowerShellHostDataTypes.Contains(type) ||
              type.IsConstructedGenericType && Classify(type, targetFramework)
                  .HasFlag(PowerShellCompilationParameterTypeCapability.PowerShellHost);

    internal static PowerShellCompilationParameterTypeCapability Classify(Type type, string? targetFramework)
    {
        if (type.IsArray)
        {
            if (type.GetArrayRank() != 1 || !PowerShellGeneratedElementTypePolicy.IsRepresentable(type.GetElementType()!, targetFramework))
                return PowerShellCompilationParameterTypeCapability.None;
            var element = Classify(type.GetElementType()!, targetFramework);
            if (!element.HasFlag(PowerShellCompilationParameterTypeCapability.ClrMethod))
                return PowerShellCompilationParameterTypeCapability.None;
            return PowerShellTypedExecutableParameterPolicy.IsSupported(type)
                ? element
                : element & ~PowerShellCompilationParameterTypeCapability.ProcessArgument;
        }

        if (type.IsGenericType)
        {
            if (type.ContainsGenericParameters ||
                !PowerShellGeneratedTypePolicy.IsSupportedContainerDefinition(type.GetGenericTypeDefinition(), targetFramework) ||
                !type.GetGenericArguments().All(argument => PowerShellGeneratedElementTypePolicy.IsRepresentable(argument, targetFramework)))
                return PowerShellCompilationParameterTypeCapability.None;
            var arguments = type.GetGenericArguments().Select(argument => Classify(argument, targetFramework)).ToArray();
            if (arguments.Any(argument => !argument.HasFlag(PowerShellCompilationParameterTypeCapability.ClrMethod)))
                return PowerShellCompilationParameterTypeCapability.None;
            var container = PowerShellCompilationParameterTypeCapability.ClrMethod;
            if (arguments.Any(argument => argument.HasFlag(PowerShellCompilationParameterTypeCapability.PowerShellHost)))
                container |= PowerShellCompilationParameterTypeCapability.PowerShellHost;
            if (PowerShellTypedExecutableParameterPolicy.IsSupported(type))
                container |= PowerShellCompilationParameterTypeCapability.ProcessArgument;
            return container;
        }

        var result = PowerShellCompilationParameterTypeCapability.None;
        if (PowerShellGeneratedTypePolicy.IsSupported(type, targetFramework))
            result |= PowerShellCompilationParameterTypeCapability.ClrMethod;
        else if (PowerShellHostDataTypes.Contains(type) ||
                 type.FullName is { } fullName && PowerShellHostTypeNames.Contains(fullName))
            result |= PowerShellCompilationParameterTypeCapability.ClrMethod |
                      PowerShellCompilationParameterTypeCapability.PowerShellHost;

        if (PowerShellTypedExecutableParameterPolicy.IsSupported(type))
            result |= PowerShellCompilationParameterTypeCapability.ProcessArgument;
        return result;
    }

    internal static PowerShellCompilationParameterTypeCapability ClassifyUntyped(
        PowerShellCompilationCapability capabilities)
        => CanUseUntypedObject(capabilities)
            ? PowerShellCompilationParameterTypeCapability.ClrMethod |
              PowerShellCompilationParameterTypeCapability.PowerShellHost
            : PowerShellCompilationParameterTypeCapability.None;

    internal static bool CanUseUntypedObject(PowerShellCompilationCapability capabilities)
        => capabilities.HasFlag(PowerShellCompilationCapability.UntypedObjectParameters);

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
