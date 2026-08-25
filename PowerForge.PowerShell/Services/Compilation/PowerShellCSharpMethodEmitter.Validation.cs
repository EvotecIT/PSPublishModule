using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellCSharpMethodEmitter
{
    private void EmitParameterValidations(IReadOnlyList<ParameterAst> parameters)
    {
        for (var index = 0; index < parameters.Count; index++)
        {
            var parameter = parameters[index];
            var name = parameter.Name.VariablePath.UserPath;
            if (!_parameterMetadata.TryGetValue(name, out var metadata))
                continue;

            var parameterType = GetCompiledParameterType(parameter);
            var identifier = GetVariableIdentifier(name);
            if (metadata.IsMandatory && parameterType == typeof(string) && !metadata.AllowEmptyString)
            {
                var condition = metadata.AllowNull
                    ? $"{identifier} is not null && {identifier}.Length == 0"
                    : $"global::System.String.IsNullOrEmpty({identifier})";
                AppendLine($"if ({condition})");
                AppendLine($"    throw new global::System.ArgumentException({PowerShellCSharpLiteral.QuoteString($"Mandatory parameter '-{metadata.Name}' does not allow an empty string.")}, {PowerShellCSharpLiteral.QuoteString(metadata.Name)});");
            }
            else if (metadata.IsMandatory && parameterType.IsArray)
            {
                if (!metadata.AllowNull)
                {
                    AppendLine($"if ({identifier} is null)");
                    AppendLine($"    throw new global::System.ArgumentException({PowerShellCSharpLiteral.QuoteString($"Mandatory parameter '-{metadata.Name}' does not allow null values.")}, {PowerShellCSharpLiteral.QuoteString(metadata.Name)});");
                }
                if (!metadata.AllowEmptyCollection)
                {
                    AppendLine($"if ({identifier} is not null && {identifier}.Length == 0)");
                    AppendLine($"    throw new global::System.ArgumentException({PowerShellCSharpLiteral.QuoteString($"Mandatory parameter '-{metadata.Name}' does not allow an empty collection.")}, {PowerShellCSharpLiteral.QuoteString(metadata.Name)});");
                }
            }
            else if (metadata.IsMandatory && !metadata.AllowNull && !parameterType.IsValueType)
            {
                AppendLine($"if ({identifier} is null)");
                AppendLine($"    throw new global::System.ArgumentException({PowerShellCSharpLiteral.QuoteString($"Mandatory parameter '-{metadata.Name}' does not allow null values.")}, {PowerShellCSharpLiteral.QuoteString(metadata.Name)});");
            }
            if (metadata.Validations.Length == 0)
                continue;
            var skipWhenOmitted = !metadata.IsMandatory &&
                                  _capabilities.HasFlag(PowerShellCompilationCapability.BoundParameters);
            if (skipWhenOmitted)
            {
                AppendLine($"if (__boundParameters.Contains({PowerShellCSharpLiteral.QuoteString(metadata.Name)}))");
                AppendLine("{");
                _indent++;
            }
            if (parameterType.IsArray)
            {
                var elementType = parameterType.GetElementType()!;
                var item = $"__validationValue{index}";
                EmitArrayValidationRules(metadata, identifier);
                AppendLine($"foreach (var {item} in {identifier} ?? global::System.Array.Empty<{GetTypeName(elementType)}>())");
                AppendLine("{");
                _indent++;
                EmitValidationRules(metadata, item, elementType, validateCollectionElement: true);
                _indent--;
                AppendLine("}");
            }
            else
            {
                EmitValidationRules(metadata, identifier, parameterType);
            }
            if (skipWhenOmitted)
            {
                _indent--;
                AppendLine("}");
            }
        }
    }

    private void EmitArrayValidationRules(PowerShellCompilationParameter parameter, string value)
    {
        foreach (var validation in parameter.Validations)
        {
            var condition = validation.Kind == PowerShellCompilationValidationKind.NotNullOrEmpty
                ? $"{value} is null || {value}.Length == 0"
                : $"{value} is null";
            AppendLine($"if ({condition})");
            AppendLine($"    throw new global::System.ArgumentException({PowerShellCSharpLiteral.QuoteString(GetValidationMessage(parameter.Name, validation.Kind))}, {PowerShellCSharpLiteral.QuoteString(parameter.Name)});");
        }
    }

    private void EmitValidationRules(
        PowerShellCompilationParameter parameter,
        string value,
        Type valueType,
        bool validateCollectionElement = false)
    {
        foreach (var validation in parameter.Validations)
        {
            if (validateCollectionElement && validation.Kind == PowerShellCompilationValidationKind.NotNull)
                continue;
            var condition = validation.Kind switch
            {
                PowerShellCompilationValidationKind.NotNull when !valueType.IsValueType => $"{value} is null",
                PowerShellCompilationValidationKind.NotNull => null,
                PowerShellCompilationValidationKind.NotNullOrEmpty when valueType == typeof(string) => $"global::System.String.IsNullOrEmpty({value})",
                PowerShellCompilationValidationKind.NotNullOrEmpty when !valueType.IsValueType => $"{value} is null",
                PowerShellCompilationValidationKind.NotNullOrEmpty => null,
                PowerShellCompilationValidationKind.Set => EmitValidateSetFailure(value, validation.Arguments),
                PowerShellCompilationValidationKind.Range => EmitValidateRangeFailure(value, valueType, validation.Arguments),
                PowerShellCompilationValidationKind.Pattern => EmitValidatePatternFailure(value, validation.Arguments.Single()),
                _ => throw Error(_body, $"Validation metadata '{validation.Kind}' is not supported for typed method parameters.")
            };
            if (validateCollectionElement && !valueType.IsValueType && validation.Kind != PowerShellCompilationValidationKind.NotNullOrEmpty)
                condition = condition is null ? $"{value} is null" : $"{value} is null || {condition}";
            if (condition is null)
                continue;
            AppendLine($"if ({condition})");
            AppendLine($"    throw new global::System.ArgumentException({PowerShellCSharpLiteral.QuoteString(GetValidationMessage(parameter.Name, validation.Kind))}, {PowerShellCSharpLiteral.QuoteString(parameter.Name)});");
        }
    }

    private static string EmitValidateSetFailure(string value, IEnumerable<string> allowed)
    {
        var candidates = "new string[] { " + string.Join(", ", allowed.Select(PowerShellCSharpLiteral.QuoteString)) + " }";
        var actual = $"global::System.Convert.ToString({value}, global::System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty";
        return $"!global::System.Linq.Enumerable.Contains({candidates}, {actual}, global::System.StringComparer.OrdinalIgnoreCase)";
    }

    private static string EmitValidateRangeFailure(string value, Type valueType, IReadOnlyList<string> arguments)
    {
        if (arguments.Count != 2)
            throw new InvalidOperationException("ValidateRange requires exactly two invariant numeric bounds.");
        if (valueType == typeof(float) || valueType == typeof(double))
        {
            var actual = $"global::System.Convert.ToDouble({value}, global::System.Globalization.CultureInfo.InvariantCulture)";
            var minimum = $"global::System.Double.Parse({PowerShellCSharpLiteral.QuoteString(arguments[0])}, global::System.Globalization.NumberStyles.Float, global::System.Globalization.CultureInfo.InvariantCulture)";
            var maximum = $"global::System.Double.Parse({PowerShellCSharpLiteral.QuoteString(arguments[1])}, global::System.Globalization.NumberStyles.Float, global::System.Globalization.CultureInfo.InvariantCulture)";
            return $"global::System.Double.IsNaN({actual}) || {actual} < {minimum} || {actual} > {maximum}";
        }
        var decimalValue = $"global::System.Convert.ToDecimal({value}, global::System.Globalization.CultureInfo.InvariantCulture)";
        var decimalMinimum = $"global::System.Decimal.Parse({PowerShellCSharpLiteral.QuoteString(arguments[0])}, global::System.Globalization.NumberStyles.Float, global::System.Globalization.CultureInfo.InvariantCulture)";
        var decimalMaximum = $"global::System.Decimal.Parse({PowerShellCSharpLiteral.QuoteString(arguments[1])}, global::System.Globalization.NumberStyles.Float, global::System.Globalization.CultureInfo.InvariantCulture)";
        return $"{decimalValue} < {decimalMinimum} || {decimalValue} > {decimalMaximum}";
    }

    private static string EmitValidatePatternFailure(string value, string pattern)
    {
        var actual = $"global::System.Convert.ToString({value}, global::System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty";
        return $"!global::System.Text.RegularExpressions.Regex.IsMatch({actual}, {PowerShellCSharpLiteral.QuoteString(pattern)}, global::System.Text.RegularExpressions.RegexOptions.IgnoreCase)";
    }

    private static string GetValidationMessage(string parameterName, PowerShellCompilationValidationKind kind)
        => kind switch
        {
            PowerShellCompilationValidationKind.NotNull => $"Parameter '-{parameterName}' does not allow null values.",
            PowerShellCompilationValidationKind.NotNullOrEmpty => $"Parameter '-{parameterName}' does not allow null or empty values.",
            PowerShellCompilationValidationKind.Set => $"Parameter '-{parameterName}' contains a value outside its validation set.",
            PowerShellCompilationValidationKind.Range => $"Parameter '-{parameterName}' contains a value outside its validation range.",
            PowerShellCompilationValidationKind.Pattern => $"Parameter '-{parameterName}' contains a value that does not match its validation pattern.",
            _ => $"Parameter '-{parameterName}' failed validation."
        };
}
