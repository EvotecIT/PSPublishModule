using System.Management.Automation.Language;

namespace PowerForge;

public sealed partial class PowerShellCompilationAnalyzer
{
    private static bool IsSupportedMetadataAttribute(
        AttributeAst attribute,
        PowerShellCompilationCapability capabilities,
        string? targetFramework)
    {
        if (PowerShellParameterContractBinder.IsAttributeNamed(attribute, "CmdletBinding"))
            return attribute.PositionalArguments.Count == 0 && attribute.NamedArguments.All(argument =>
                IsSupportedCmdletBindingNamedArgument(argument, capabilities));
        if (PowerShellParameterContractBinder.IsAttributeNamed(attribute, "Parameter"))
            return attribute.PositionalArguments.Count == 0 && attribute.NamedArguments.All(argument =>
                IsSupportedParameterNamedArgument(argument, capabilities));
        if (PowerShellParameterContractBinder.IsAttributeNamed(attribute, "Alias"))
            return attribute.NamedArguments.Count == 0 && attribute.PositionalArguments.Count > 0 &&
                   attribute.PositionalArguments.All(static argument => argument is StringConstantExpressionAst { Value.Length: > 0 });
        if (PowerShellParameterContractBinder.IsAttributeNamed(attribute, "OutputType"))
            return attribute.NamedArguments.Count == 0 && attribute.PositionalArguments.Count == 1 &&
                   attribute.PositionalArguments[0] is TypeExpressionAst outputType &&
                   outputType.TypeName.GetReflectionType() is { } declaredOutputType &&
                   declaredOutputType != typeof(void) &&
                   PowerShellCompilationParameterTypePolicy.CanUseInMethod(declaredOutputType, targetFramework, capabilities);
        if (PowerShellParameterContractBinder.IsAttributeNamed(attribute, "AllowNull") ||
            PowerShellParameterContractBinder.IsAttributeNamed(attribute, "AllowEmptyString") ||
            PowerShellParameterContractBinder.IsAttributeNamed(attribute, "AllowEmptyCollection") ||
            PowerShellParameterContractBinder.IsAttributeNamed(attribute, "ValidateNotNull") ||
            PowerShellParameterContractBinder.IsAttributeNamed(attribute, "ValidateNotNullOrEmpty"))
            return attribute.PositionalArguments.Count == 0 && attribute.NamedArguments.Count == 0;
        if (PowerShellParameterContractBinder.IsAttributeNamed(attribute, "SupportsWildcards"))
            return capabilities.HasFlag(PowerShellCompilationCapability.PipelineParameterBinding) &&
                   attribute.PositionalArguments.Count == 0 && attribute.NamedArguments.Count == 0;
        if (PowerShellParameterContractBinder.IsAttributeNamed(attribute, "ValidateSet"))
            return attribute.NamedArguments.Count == 0 && attribute.PositionalArguments.Count > 0 &&
                   attribute.PositionalArguments.All(static argument => argument is StringConstantExpressionAst);
        if (PowerShellParameterContractBinder.IsAttributeNamed(attribute, "ValidatePattern"))
            return attribute.NamedArguments.Count == 0 && attribute.PositionalArguments.Count == 1 &&
                   attribute.PositionalArguments[0] is StringConstantExpressionAst;
        if (PowerShellParameterContractBinder.IsAttributeNamed(attribute, "ValidateRange"))
            return attribute.NamedArguments.Count == 0 && attribute.PositionalArguments.Count == 2 &&
                   attribute.PositionalArguments.All(static argument => PowerShellParameterContractBinder.TryGetInvariantNumericLiteral(argument, out _)) &&
                   attribute.Parent is ParameterAst parameter &&
                   IsSupportedValidateRangeType(parameter.StaticType);
        return false;
    }

    private static bool IsSupportedValidateRangeType(Type type)
    {
        var valueType = type.IsArray && type.GetArrayRank() == 1
            ? type.GetElementType()!
            : type;
        valueType = Nullable.GetUnderlyingType(valueType) ?? valueType;
        return valueType == typeof(byte) || valueType == typeof(sbyte) ||
               valueType == typeof(short) || valueType == typeof(ushort) ||
               valueType == typeof(int) || valueType == typeof(uint) ||
               valueType == typeof(long) || valueType == typeof(ulong) ||
               valueType == typeof(float) || valueType == typeof(double) ||
               valueType == typeof(decimal);
    }

    private static bool IsSupportedCmdletBindingNamedArgument(
        NamedAttributeArgumentAst argument,
        PowerShellCompilationCapability capabilities)
    {
        if (argument.ArgumentName.Equals("PositionalBinding", StringComparison.OrdinalIgnoreCase))
            return PowerShellParameterContractBinder.TryGetBooleanAttributeValue(argument, out _);
        if (!capabilities.HasFlag(PowerShellCompilationCapability.PipelineParameterBinding))
            return false;
        if (argument.ArgumentName.Equals("SupportsShouldProcess", StringComparison.OrdinalIgnoreCase))
            return PowerShellParameterContractBinder.TryGetBooleanAttributeValue(argument, out _);
        if (argument.ArgumentName.Equals("DefaultParameterSetName", StringComparison.OrdinalIgnoreCase))
            return PowerShellParameterContractBinder.TryGetStringAttributeValue(argument, out var defaultSet) && !string.IsNullOrWhiteSpace(defaultSet);
        if (!argument.ArgumentName.Equals("ConfirmImpact", StringComparison.OrdinalIgnoreCase) ||
            !PowerShellParameterContractBinder.TryGetStringAttributeValue(argument, out var impact))
            return false;
        return impact.Equals("None", StringComparison.OrdinalIgnoreCase) ||
               impact.Equals("Low", StringComparison.OrdinalIgnoreCase) ||
               impact.Equals("Medium", StringComparison.OrdinalIgnoreCase) ||
               impact.Equals("High", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedParameterNamedArgument(
        NamedAttributeArgumentAst argument,
        PowerShellCompilationCapability capabilities)
    {
        if (argument.ArgumentName.Equals("Mandatory", StringComparison.OrdinalIgnoreCase))
            return PowerShellParameterContractBinder.TryGetBooleanAttributeValue(argument, out _);
        if (argument.ArgumentName.Equals("ValueFromRemainingArguments", StringComparison.OrdinalIgnoreCase))
            return (capabilities.HasFlag(PowerShellCompilationCapability.PipelineParameterBinding) ||
                    capabilities.HasFlag(PowerShellCompilationCapability.ExecutableParameterBinding)) &&
                   PowerShellParameterContractBinder.TryGetBooleanAttributeValue(argument, out _);
        if (argument.ArgumentName.Equals("DontShow", StringComparison.OrdinalIgnoreCase) ||
            argument.ArgumentName.Equals("ValueFromPipeline", StringComparison.OrdinalIgnoreCase) ||
            argument.ArgumentName.Equals("ValueFromPipelineByPropertyName", StringComparison.OrdinalIgnoreCase))
            return capabilities.HasFlag(PowerShellCompilationCapability.PipelineParameterBinding) &&
            PowerShellParameterContractBinder.TryGetBooleanAttributeValue(argument, out _);
        if (argument.ArgumentName.Equals("ParameterSetName", StringComparison.OrdinalIgnoreCase))
            return capabilities.HasFlag(PowerShellCompilationCapability.PipelineParameterBinding) &&
                   PowerShellParameterContractBinder.TryGetStringAttributeValue(argument, out var setName) && !string.IsNullOrWhiteSpace(setName);
        if (argument.ArgumentName.Equals("HelpMessage", StringComparison.OrdinalIgnoreCase))
            return capabilities.HasFlag(PowerShellCompilationCapability.PipelineParameterBinding) &&
                   PowerShellParameterContractBinder.TryGetStringAttributeValue(argument, out _);
        return argument.ArgumentName.Equals("Position", StringComparison.OrdinalIgnoreCase) &&
               PowerShellParameterContractBinder.TryGetIntegerAttributeValue(argument, out var position) && position >= 0;
    }

}
