using System.Globalization;
using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Canonical parser-to-neutral binding for PowerShell parameter metadata.</summary>
internal static class PowerShellParameterContractBinder
{
    internal static PowerShellCompilationParameter Bind(
        ParameterAst parameter,
        string? targetFramework,
        PowerShellCompilationCapability capabilities = PowerShellCompilationCapability.None)
    {
        var type = parameter.StaticType;
        var isSwitch = type == typeof(System.Management.Automation.SwitchParameter);
        var clrType = isSwitch ? typeof(bool) : type;
        var bindings = GetBindings(parameter);
        PowerShellCompilationLiteral? defaultValue = null;
        if (parameter.DefaultValue is not null)
            PowerShellCompilationLiteralPolicy.TryResolve(parameter.DefaultValue, clrType, out defaultValue);
        return new PowerShellCompilationParameter(
            parameter.Name.VariablePath.UserPath,
            clrType.FullName ?? clrType.Name,
            parameter.DefaultValue is not null,
            bindings.Length > 0 && bindings.All(static binding => binding.Mandatory),
            isSwitch,
            GetAliases(parameter),
            HasMetadataAttribute(parameter, "AllowNull"),
            GetValidations(parameter),
            parameter.Attributes.OfType<TypeConstraintAst>().Any()
                ? PowerShellCompilationParameterTypePolicy.Classify(clrType, targetFramework)
                : PowerShellCompilationParameterTypePolicy.ClassifyUntyped(capabilities),
            bindings,
            HasMetadataAttribute(parameter, "AllowEmptyString"),
            HasMetadataAttribute(parameter, "AllowEmptyCollection"),
            HasMetadataAttribute(parameter, "SupportsWildcards"),
            defaultValue);
    }

    internal static PowerShellCompilationParameterBinding[] GetBindings(ParameterAst parameter)
    {
        var attributes = parameter.Attributes.OfType<AttributeAst>()
            .Where(static attribute => IsAttributeNamed(attribute, "Parameter"))
            .ToArray();
        if (attributes.Length == 0)
            return new[] { new PowerShellCompilationParameterBinding() };
        return attributes.Select(attribute =>
        {
            var setName = GetNamedString(attribute, "ParameterSetName");
            if (setName.Equals("__AllParameterSets", StringComparison.OrdinalIgnoreCase)) setName = string.Empty;
            return new PowerShellCompilationParameterBinding(
                setName,
                GetNamedBoolean(attribute, "Mandatory"),
                GetNamedInteger(attribute, "Position"),
                GetNamedBoolean(attribute, "ValueFromPipeline"),
                GetNamedBoolean(attribute, "ValueFromPipelineByPropertyName"),
                GetNamedBoolean(attribute, "ValueFromRemainingArguments"),
                GetNamedBoolean(attribute, "DontShow"),
                GetNamedString(attribute, "HelpMessage"));
        }).ToArray();
    }

    internal static string[] GetAliases(ParameterAst parameter)
        => parameter.Attributes.OfType<AttributeAst>()
            .Where(static attribute => IsAttributeNamed(attribute, "Alias"))
            .SelectMany(static attribute => attribute.PositionalArguments.OfType<StringConstantExpressionAst>())
            .Select(static argument => argument.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    internal static bool HasMetadataAttribute(ParameterAst parameter, string name)
        => parameter.Attributes.OfType<AttributeAst>().Any(attribute => IsAttributeNamed(attribute, name));

    internal static PowerShellCompilationValidation[] GetValidations(ParameterAst parameter)
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

    internal static bool IsAttributeNamed(AttributeAst attribute, string name)
        => attribute.TypeName.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
           attribute.TypeName.Name.Equals(name + "Attribute", StringComparison.OrdinalIgnoreCase);

    internal static bool TryGetInvariantNumericLiteral(ExpressionAst expression, out string literal)
    {
        object? value;
        try { value = expression.SafeGetValue(); }
        catch (InvalidOperationException) { literal = string.Empty; return false; }
        if (value is not byte and not sbyte and not short and not ushort and not int and not uint and not long and not ulong and not float and not double and not decimal)
        {
            literal = string.Empty;
            return false;
        }
        literal = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return literal.Length > 0;
    }

    internal static bool TryGetBooleanAttributeValue(NamedAttributeArgumentAst argument, out bool value)
    {
        try
        {
            if (argument.Argument.SafeGetValue() is bool resolved) { value = resolved; return true; }
        }
        catch (InvalidOperationException) { }
        value = false;
        return false;
    }

    internal static bool TryGetStringAttributeValue(NamedAttributeArgumentAst argument, out string value)
    {
        try
        {
            if (argument.Argument.SafeGetValue() is string resolved) { value = resolved; return true; }
        }
        catch (InvalidOperationException) { }
        value = string.Empty;
        return false;
    }

    internal static bool TryGetIntegerAttributeValue(NamedAttributeArgumentAst argument, out int value)
    {
        try
        {
            var resolved = argument.Argument.SafeGetValue();
            if (resolved is int direct) { value = direct; return true; }
            if (resolved is IConvertible convertible) { value = convertible.ToInt32(CultureInfo.InvariantCulture); return true; }
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException or OverflowException) { }
        value = 0;
        return false;
    }

    private static bool GetNamedBoolean(AttributeAst attribute, string name)
    {
        var argument = attribute.NamedArguments.FirstOrDefault(candidate => candidate.ArgumentName.Equals(name, StringComparison.OrdinalIgnoreCase));
        return argument is not null && TryGetBooleanAttributeValue(argument, out var value) && value;
    }

    private static int? GetNamedInteger(AttributeAst attribute, string name)
    {
        var argument = attribute.NamedArguments.FirstOrDefault(candidate => candidate.ArgumentName.Equals(name, StringComparison.OrdinalIgnoreCase));
        return argument is not null && TryGetIntegerAttributeValue(argument, out var value) ? value : null;
    }

    private static string GetNamedString(AttributeAst attribute, string name)
    {
        var argument = attribute.NamedArguments.FirstOrDefault(candidate => candidate.ArgumentName.Equals(name, StringComparison.OrdinalIgnoreCase));
        return argument is not null && TryGetStringAttributeValue(argument, out var value) ? value : string.Empty;
    }
}
