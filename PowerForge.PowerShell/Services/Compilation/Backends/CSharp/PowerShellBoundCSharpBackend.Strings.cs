namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private string EmitInterpolatedString(PowerShellLoweredInterpolatedStringExpression interpolated)
    {
        var parts = interpolated.Parts.Select(part => part.Expression is null
            ? PowerShellCSharpLiteral.QuoteString(part.Text ?? string.Empty)
            : part.Expression.ClrType == typeof(string)
                ? $"({EmitExpression(part.Expression)} ?? string.Empty)"
                : interpolated.UsePowerShellRuntime
                    ? $"__statementErrors.InterpolateValue((object?)({EmitExpression(part.Expression)}))"
                    : EmitScalarInterpolation(part.Expression)).ToArray();
        return parts.Length switch
        {
            0 => "string.Empty",
            1 => parts[0],
            _ => $"global::System.String.Concat(new string[] {{ {string.Join(", ", parts)} }})"
        };
    }

    private string EmitScalarInterpolation(PowerShellLoweredExpression expression)
    {
        var nullableType = Nullable.GetUnderlyingType(expression.ClrType);
        var scalarType = nullableType ?? expression.ClrType;
        var value = EmitExpression(expression);
        const string provider = "global::System.Globalization.CultureInfo.InvariantCulture";
        if (scalarType == typeof(double) || scalarType == typeof(float))
        {
            var format = scalarType == typeof(double) ? "G15" : "G7";
            return nullableType is null
                ? $"({value}).ToString(\"{format}\", {provider})"
                : $"(({value})?.ToString(\"{format}\", {provider}) ?? string.Empty)";
        }
        return $"(global::System.Convert.ToString((object?)({value}), {provider}) ?? string.Empty)";
    }

}
