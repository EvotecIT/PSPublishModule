using System.Globalization;
using System.Management.Automation.Language;

namespace PowerForge;

public sealed partial class PowerShellCompilationAnalyzer
{
    private static bool IsSupportedMetadataAttribute(
        AttributeAst attribute,
        PowerShellCompilationCapability capabilities,
        string? targetFramework)
    {
        if (IsAttributeNamed(attribute, "CmdletBinding"))
            return attribute.PositionalArguments.Count == 0 && attribute.NamedArguments.All(argument =>
                IsSupportedCmdletBindingNamedArgument(argument, capabilities));
        if (IsAttributeNamed(attribute, "Parameter"))
            return attribute.PositionalArguments.Count == 0 && attribute.NamedArguments.All(argument =>
                IsSupportedParameterNamedArgument(argument, capabilities));
        if (IsAttributeNamed(attribute, "Alias"))
            return attribute.NamedArguments.Count == 0 && attribute.PositionalArguments.Count > 0 &&
                   attribute.PositionalArguments.All(static argument => argument is StringConstantExpressionAst { Value.Length: > 0 });
        if (IsAttributeNamed(attribute, "OutputType"))
            return attribute.NamedArguments.Count == 0 && attribute.PositionalArguments.Count == 1 &&
                   attribute.PositionalArguments[0] is TypeExpressionAst outputType &&
                   outputType.TypeName.GetReflectionType() is { } declaredOutputType &&
                   declaredOutputType != typeof(void) &&
                   PowerShellCompilationParameterTypePolicy.CanUseInMethod(declaredOutputType, targetFramework, capabilities);
        if (IsAttributeNamed(attribute, "AllowNull") ||
            IsAttributeNamed(attribute, "AllowEmptyString") ||
            IsAttributeNamed(attribute, "AllowEmptyCollection") ||
            IsAttributeNamed(attribute, "ValidateNotNull") ||
            IsAttributeNamed(attribute, "ValidateNotNullOrEmpty"))
            return attribute.PositionalArguments.Count == 0 && attribute.NamedArguments.Count == 0;
        if (IsAttributeNamed(attribute, "SupportsWildcards"))
            return capabilities.HasFlag(PowerShellCompilationCapability.PipelineParameterBinding) &&
                   attribute.PositionalArguments.Count == 0 && attribute.NamedArguments.Count == 0;
        if (IsAttributeNamed(attribute, "ValidateSet"))
            return attribute.NamedArguments.Count == 0 && attribute.PositionalArguments.Count > 0 &&
                   attribute.PositionalArguments.All(static argument => argument is StringConstantExpressionAst);
        if (IsAttributeNamed(attribute, "ValidatePattern"))
            return attribute.NamedArguments.Count == 0 && attribute.PositionalArguments.Count == 1 &&
                   attribute.PositionalArguments[0] is StringConstantExpressionAst;
        if (IsAttributeNamed(attribute, "ValidateRange"))
            return attribute.NamedArguments.Count == 0 && attribute.PositionalArguments.Count == 2 &&
                   attribute.PositionalArguments.All(static argument => TryGetInvariantNumericLiteral(argument, out _));
        return false;
    }

    private static bool IsSupportedCmdletBindingNamedArgument(
        NamedAttributeArgumentAst argument,
        PowerShellCompilationCapability capabilities)
    {
        if (argument.ArgumentName.Equals("PositionalBinding", StringComparison.OrdinalIgnoreCase))
            return TryGetBooleanAttributeValue(argument, out _);
        if (!capabilities.HasFlag(PowerShellCompilationCapability.PipelineParameterBinding))
            return false;
        if (argument.ArgumentName.Equals("SupportsShouldProcess", StringComparison.OrdinalIgnoreCase))
            return TryGetBooleanAttributeValue(argument, out _);
        if (argument.ArgumentName.Equals("DefaultParameterSetName", StringComparison.OrdinalIgnoreCase))
            return TryGetStringAttributeValue(argument, out var defaultSet) && !string.IsNullOrWhiteSpace(defaultSet);
        if (!argument.ArgumentName.Equals("ConfirmImpact", StringComparison.OrdinalIgnoreCase) ||
            !TryGetStringAttributeValue(argument, out var impact))
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
            return TryGetBooleanAttributeValue(argument, out _);
        if (argument.ArgumentName.Equals("ValueFromRemainingArguments", StringComparison.OrdinalIgnoreCase))
            return (capabilities.HasFlag(PowerShellCompilationCapability.PipelineParameterBinding) ||
                    capabilities.HasFlag(PowerShellCompilationCapability.ExecutableParameterBinding)) &&
                   TryGetBooleanAttributeValue(argument, out _);
        if (argument.ArgumentName.Equals("DontShow", StringComparison.OrdinalIgnoreCase) ||
            argument.ArgumentName.Equals("ValueFromPipeline", StringComparison.OrdinalIgnoreCase) ||
            argument.ArgumentName.Equals("ValueFromPipelineByPropertyName", StringComparison.OrdinalIgnoreCase))
            return capabilities.HasFlag(PowerShellCompilationCapability.PipelineParameterBinding) &&
                   TryGetBooleanAttributeValue(argument, out _);
        if (argument.ArgumentName.Equals("ParameterSetName", StringComparison.OrdinalIgnoreCase))
            return capabilities.HasFlag(PowerShellCompilationCapability.PipelineParameterBinding) &&
                   TryGetStringAttributeValue(argument, out var setName) && !string.IsNullOrWhiteSpace(setName);
        if (argument.ArgumentName.Equals("HelpMessage", StringComparison.OrdinalIgnoreCase))
            return capabilities.HasFlag(PowerShellCompilationCapability.PipelineParameterBinding) &&
                   TryGetStringAttributeValue(argument, out _);
        return argument.ArgumentName.Equals("Position", StringComparison.OrdinalIgnoreCase) &&
               TryGetIntegerAttributeValue(argument, out var position) && position >= 0;
    }

    private static string[] GetAliases(ParameterAst parameter)
        => parameter.Attributes
            .OfType<AttributeAst>()
            .Where(static attribute => IsAttributeNamed(attribute, "Alias"))
            .SelectMany(static attribute => attribute.PositionalArguments.OfType<StringConstantExpressionAst>())
            .Select(static argument => argument.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool HasMetadataAttribute(ParameterAst parameter, string name)
        => parameter.Attributes.OfType<AttributeAst>().Any(attribute => IsAttributeNamed(attribute, name));

    private static PowerShellCompilationValidation[] GetValidations(ParameterAst parameter)
    {
        var validations = new List<PowerShellCompilationValidation>();
        foreach (var attribute in parameter.Attributes.OfType<AttributeAst>())
        {
            if (IsAttributeNamed(attribute, "ValidateNotNull"))
                validations.Add(new PowerShellCompilationValidation(PowerShellCompilationValidationKind.NotNull));
            else if (IsAttributeNamed(attribute, "ValidateNotNullOrEmpty"))
                validations.Add(new PowerShellCompilationValidation(PowerShellCompilationValidationKind.NotNullOrEmpty));
            else if (IsAttributeNamed(attribute, "ValidateSet"))
                validations.Add(new PowerShellCompilationValidation(
                    PowerShellCompilationValidationKind.Set,
                    attribute.PositionalArguments.OfType<StringConstantExpressionAst>().Select(static argument => argument.Value).ToArray()));
            else if (IsAttributeNamed(attribute, "ValidatePattern") &&
                     attribute.PositionalArguments.Count == 1 &&
                     attribute.PositionalArguments[0] is StringConstantExpressionAst pattern)
                validations.Add(new PowerShellCompilationValidation(PowerShellCompilationValidationKind.Pattern, new[] { pattern.Value }));
            else if (IsAttributeNamed(attribute, "ValidateRange") && attribute.PositionalArguments.Count == 2)
                validations.Add(new PowerShellCompilationValidation(
                    PowerShellCompilationValidationKind.Range,
                    attribute.PositionalArguments.Select(argument =>
                        TryGetInvariantNumericLiteral(argument, out var literal) ? literal : string.Empty).ToArray()));
        }
        return validations.ToArray();
    }

    private static bool TryGetInvariantNumericLiteral(ExpressionAst expression, out string literal)
    {
        object? value;
        try
        {
            value = expression.SafeGetValue();
        }
        catch (InvalidOperationException)
        {
            literal = string.Empty;
            return false;
        }
        if (value is not byte and not sbyte and not short and not ushort and not int and not uint and not long and not ulong and not float and not double and not decimal)
        {
            literal = string.Empty;
            return false;
        }
        literal = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return literal.Length > 0;
    }

    private static bool TryGetBooleanAttributeValue(NamedAttributeArgumentAst argument, out bool value)
    {
        try
        {
            if (argument.Argument.SafeGetValue() is bool resolved)
            {
                value = resolved;
                return true;
            }
        }
        catch (InvalidOperationException)
        {
            // Dynamic attribute arguments remain on the PowerShell runtime path.
        }
        value = false;
        return false;
    }

    private static bool TryGetStringAttributeValue(NamedAttributeArgumentAst argument, out string value)
    {
        try
        {
            if (argument.Argument.SafeGetValue() is string resolved)
            {
                value = resolved;
                return true;
            }
        }
        catch (InvalidOperationException)
        {
            // Dynamic attribute arguments remain on the PowerShell runtime path.
        }
        value = string.Empty;
        return false;
    }

    private static bool TryGetIntegerAttributeValue(NamedAttributeArgumentAst argument, out int value)
    {
        try
        {
            var resolved = argument.Argument.SafeGetValue();
            if (resolved is int direct)
            {
                value = direct;
                return true;
            }
            if (resolved is IConvertible convertible)
            {
                value = convertible.ToInt32(CultureInfo.InvariantCulture);
                return true;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException or OverflowException)
        {
            // Dynamic or out-of-range attribute arguments remain on the PowerShell runtime path.
        }
        value = 0;
        return false;
    }
}
