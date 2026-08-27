using System.Text;

namespace PowerForge;

internal sealed class PowerShellParameterEmissionContract
{
    internal PowerShellParameterEmissionContract(string name, Type clrType, PowerShellCompilationParameter metadata)
    {
        Name = name;
        ClrType = clrType;
        Metadata = metadata;
    }

    internal string Name { get; }
    internal Type ClrType { get; }
    internal PowerShellCompilationParameter Metadata { get; }
}

/// <summary>Renders parameter defaults, normalization, and validation from neutral bound contracts.</summary>
internal sealed class PowerShellParameterPrologueRenderer
{
    private readonly PowerShellCompilationCapability _capabilities;
    private readonly Func<Type, string> _getTypeName;
    private readonly Func<string, string> _getTemporaryIdentifier;
    private readonly StringBuilder _builder = new();
    private int _indent;

    internal PowerShellParameterPrologueRenderer(
        PowerShellCompilationCapability capabilities,
        Func<Type, string> getTypeName,
        Func<string, string> getTemporaryIdentifier)
    {
        _capabilities = capabilities;
        _getTypeName = getTypeName;
        _getTemporaryIdentifier = getTemporaryIdentifier;
    }

    internal string Render(IReadOnlyList<PowerShellParameterEmissionContract> parameters)
    {
        RenderDefaults(parameters);
        RenderNormalization(parameters);
        RenderValidations(parameters);
        return _builder.ToString().TrimEnd();
    }

    private void RenderDefaults(IEnumerable<PowerShellParameterEmissionContract> parameters)
    {
        foreach (var parameter in parameters.Where(static parameter => parameter.Metadata.DefaultValue is not null))
        {
            AppendLine($"if (!__boundParameters.Contains({PowerShellCSharpLiteral.QuoteString(parameter.Metadata.Name)}))");
            AppendLine($"    {PowerShellClrSymbolMapper.MapIdentifier(parameter.Name)} = {PowerShellCSharpLiteral.Emit(parameter.Metadata.DefaultValue!, parameter.ClrType, _getTypeName)};");
        }
    }

    private void RenderNormalization(IEnumerable<PowerShellParameterEmissionContract> parameters)
    {
        foreach (var parameter in parameters.Where(static parameter => parameter.ClrType == typeof(string)))
        {
            var identifier = PowerShellClrSymbolMapper.MapIdentifier(parameter.Name);
            AppendLine($"{identifier} = {identifier} ?? string.Empty;");
        }
        foreach (var parameter in parameters.Where(static parameter => parameter.ClrType == typeof(string[])))
        {
            var identifier = PowerShellClrSymbolMapper.MapIdentifier(parameter.Name);
            AppendLine($"{identifier} = {identifier} is null ? null! : global::System.Linq.Enumerable.Select({identifier}, static value => value ?? string.Empty).ToArray();");
        }
    }

    private void RenderValidations(IReadOnlyList<PowerShellParameterEmissionContract> parameters)
    {
        foreach (var parameter in parameters)
        {
            var metadata = parameter.Metadata;
            var parameterType = parameter.ClrType;
            var identifier = PowerShellClrSymbolMapper.MapIdentifier(parameter.Name);
            if (metadata.IsMandatory && parameterType == typeof(string) && !metadata.AllowEmptyString)
            {
                var condition = metadata.AllowNull ? $"{identifier} is not null && {identifier}.Length == 0" : $"global::System.String.IsNullOrEmpty({identifier})";
                AppendFailure(condition, $"Mandatory parameter '-{metadata.Name}' does not allow an empty string.", metadata.Name);
            }
            else if (metadata.IsMandatory && parameterType.IsArray)
            {
                if (!metadata.AllowNull)
                    AppendFailure($"{identifier} is null", $"Mandatory parameter '-{metadata.Name}' does not allow null values.", metadata.Name);
                if (!metadata.AllowEmptyCollection)
                    AppendFailure($"{identifier} is not null && {identifier}.Length == 0", $"Mandatory parameter '-{metadata.Name}' does not allow an empty collection.", metadata.Name);
            }
            else if (metadata.IsMandatory && !metadata.AllowNull && !parameterType.IsValueType)
            {
                AppendFailure($"{identifier} is null", $"Mandatory parameter '-{metadata.Name}' does not allow null values.", metadata.Name);
            }
            if (metadata.Validations.Length == 0) continue;
            var skipWhenOmitted = !metadata.IsMandatory && metadata.DefaultValue is null &&
                                  _capabilities.HasFlag(PowerShellCompilationCapability.BoundParameters);
            if (skipWhenOmitted) BeginBlock($"if (__boundParameters.Contains({PowerShellCSharpLiteral.QuoteString(metadata.Name)}))");
            if (parameterType.IsArray)
            {
                var elementType = parameterType.GetElementType()!;
                RenderArrayValidationRules(metadata, identifier);
                var item = _getTemporaryIdentifier("validation_value");
                BeginBlock($"foreach (var {item} in {identifier} ?? global::System.Array.Empty<{_getTypeName(elementType)}>())");
                RenderValidationRules(metadata, item, elementType, validateCollectionElement: true);
                EndBlock();
            }
            else
            {
                RenderValidationRules(metadata, identifier, parameterType);
            }
            if (skipWhenOmitted) EndBlock();
        }
    }

    private void RenderArrayValidationRules(PowerShellCompilationParameter parameter, string value)
    {
        foreach (var validation in parameter.Validations)
        {
            var condition = validation.Kind == PowerShellCompilationValidationKind.NotNullOrEmpty
                ? $"{value} is null || {value}.Length == 0"
                : $"{value} is null";
            AppendFailure(condition, GetValidationMessage(parameter.Name, validation.Kind), parameter.Name);
        }
    }

    private void RenderValidationRules(PowerShellCompilationParameter parameter, string value, Type valueType, bool validateCollectionElement = false)
    {
        foreach (var validation in parameter.Validations)
        {
            var condition = validation.Kind switch
            {
                PowerShellCompilationValidationKind.NotNull when !valueType.IsValueType => $"{value} is null",
                PowerShellCompilationValidationKind.NotNull => null,
                PowerShellCompilationValidationKind.NotNullOrEmpty when valueType == typeof(string) => $"global::System.String.IsNullOrEmpty({value})",
                PowerShellCompilationValidationKind.NotNullOrEmpty when valueType == typeof(object) => $"{value} is null || ({value} is string && ((string){value}).Length == 0)",
                PowerShellCompilationValidationKind.NotNullOrEmpty when !valueType.IsValueType => $"{value} is null",
                PowerShellCompilationValidationKind.NotNullOrEmpty => null,
                PowerShellCompilationValidationKind.Set => EmitValidateSetFailure(value, validation.Arguments),
                PowerShellCompilationValidationKind.Range => EmitValidateRangeFailure(value, valueType, validation.Arguments),
                PowerShellCompilationValidationKind.Pattern => EmitValidatePatternFailure(value, validation.Arguments.Single()),
                _ => throw new InvalidOperationException($"Validation metadata '{validation.Kind}' is not supported for typed method parameters.")
            };
            if (validateCollectionElement && !valueType.IsValueType &&
                validation.Kind is not PowerShellCompilationValidationKind.NotNull and not PowerShellCompilationValidationKind.NotNullOrEmpty)
                condition = condition is null ? $"{value} is null" : $"{value} is null || {condition}";
            if (condition is not null) AppendFailure(condition, GetValidationMessage(parameter.Name, validation.Kind), parameter.Name);
        }
    }

    private void AppendFailure(string condition, string message, string parameterName)
    {
        AppendLine($"if ({condition})");
        AppendLine($"    throw {EmitParameterBindingValidationException(message, parameterName)};");
    }

    private string EmitParameterBindingValidationException(string message, string parameterName)
        => _capabilities.HasFlag(PowerShellCompilationCapability.PowerShellObjects)
            ? $"new global::System.Management.Automation.RuntimeException({PowerShellCSharpLiteral.QuoteString(message)})"
            : $"new global::System.ArgumentException({PowerShellCSharpLiteral.QuoteString(message)}, {PowerShellCSharpLiteral.QuoteString(parameterName)})";

    private static string EmitValidateSetFailure(string value, IEnumerable<string> allowed)
    {
        var candidates = "new string[] { " + string.Join(", ", allowed.Select(PowerShellCSharpLiteral.QuoteString)) + " }";
        var actual = $"global::System.Convert.ToString({value}, global::System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty";
        return $"!global::System.Linq.Enumerable.Contains({candidates}, {actual}, global::System.StringComparer.OrdinalIgnoreCase)";
    }

    private static string EmitValidateRangeFailure(string value, Type valueType, IReadOnlyList<string> arguments)
    {
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

    private void BeginBlock(string header) { AppendLine(header); AppendLine("{"); _indent++; }
    private void EndBlock() { _indent--; AppendLine("}"); }
    private void AppendLine(string line) => _builder.Append(' ', _indent * 4).AppendLine(line);
}
