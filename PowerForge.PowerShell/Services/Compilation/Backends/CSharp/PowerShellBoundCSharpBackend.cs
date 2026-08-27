using System.Globalization;
using System.Text;

namespace PowerForge;

/// <summary>
/// Renders already-lowered CLR operations as deterministic readable C#.
/// </summary>
internal sealed class PowerShellBoundCSharpBackend
{
    internal PowerShellBoundCSharpResult Emit(PowerShellLoweredProgram program)
    {
        if (program is null) throw new ArgumentNullException(nameof(program));
        var methods = program.Functions.Select(EmitFunction).ToArray();
        return new PowerShellBoundCSharpResult(methods, program.Diagnostics);
    }

    private static PowerShellCSharpMethodEmission EmitFunction(PowerShellLoweredFunction function)
    {
        var builder = new StringBuilder();
        var parameters = string.Join(", ", function.Parameters.Select(parameter =>
            $"{PowerShellCSharpMethodEmitter.GetTypeName(parameter.ClrType)} {PowerShellCSharpMethodEmitter.SanitizeIdentifier(parameter.Symbol.Name)}"));
        builder.Append("    public static ")
            .Append(PowerShellCSharpMethodEmitter.GetTypeName(function.ReturnType))
            .Append(' ')
            .Append(function.GeneratedName)
            .Append('(')
            .Append(parameters)
            .AppendLine(")")
            .AppendLine("    {")
            .AppendLine("        checked")
            .AppendLine("        {");

        foreach (var statement in function.Statements)
        {
            switch (statement)
            {
                case PowerShellLoweredAssignmentStatement assignment:
                    builder.Append("            ");
                    if (assignment.Declare)
                    {
                        builder.Append(PowerShellCSharpMethodEmitter.GetTypeName(assignment.ClrType)).Append(' ');
                    }
                    builder.Append(PowerShellCSharpMethodEmitter.SanitizeIdentifier(assignment.Target.Name))
                        .Append(" = ")
                        .Append(EmitExpression(assignment.Value))
                        .AppendLine(";");
                    break;
                case PowerShellLoweredReturnStatement { Expression: null }:
                    builder.AppendLine("            return;");
                    break;
                case PowerShellLoweredReturnStatement returned:
                    builder.Append("            return ").Append(EmitExpression(returned.Expression!)).AppendLine(";");
                    break;
                default:
                    throw new InvalidOperationException($"Lowered statement '{statement.GetType().Name}' has no C# rendering owner.");
            }
        }

        builder.AppendLine("        }").Append("    }");
        return new PowerShellCSharpMethodEmission(function.GeneratedName, function.ReturnType, builder.ToString(), help: function.Help?.ToPublicModel());
    }

    private static string EmitExpression(PowerShellLoweredExpression expression)
        => expression switch
        {
            PowerShellLoweredLiteralExpression literal => EmitLiteral(literal),
            PowerShellLoweredVariableExpression variable => PowerShellCSharpMethodEmitter.SanitizeIdentifier(variable.Symbol.Name),
            PowerShellLoweredConversionExpression conversion =>
                $"({PowerShellCSharpMethodEmitter.GetTypeName(conversion.ClrType)})({EmitExpression(conversion.Operand)})",
            PowerShellLoweredInvocationExpression invocation =>
                $"{PowerShellCSharpMethodEmitter.SanitizeIdentifier(invocation.Target.Name)}({string.Join(", ", invocation.Arguments.Select(EmitExpression))})",
            _ => throw new InvalidOperationException($"Lowered expression '{expression.GetType().Name}' has no C# rendering owner.")
        };

    private static string EmitLiteral(PowerShellLoweredLiteralExpression literal)
    {
        if (literal.Value is null) return "null";
        if (literal.Value is string text) return PowerShellCSharpLiteral.QuoteString(text);
        if (literal.Value is bool boolean) return boolean ? "true" : "false";
        if (literal.Value is char character) return $"'{character.ToString().Replace("'", "\\'")}'";
        if (literal.Value is float single) return single.ToString("R", CultureInfo.InvariantCulture) + "f";
        if (literal.Value is double doubleValue) return doubleValue.ToString("R", CultureInfo.InvariantCulture) + "d";
        if (literal.Value is decimal decimalValue) return decimalValue.ToString(CultureInfo.InvariantCulture) + "m";
        if (literal.Value is long longValue) return longValue.ToString(CultureInfo.InvariantCulture) + "L";
        if (literal.Value is ulong unsignedLong) return unsignedLong.ToString(CultureInfo.InvariantCulture) + "UL";
        if (literal.Value is uint unsignedInteger) return unsignedInteger.ToString(CultureInfo.InvariantCulture) + "U";
        if (literal.Value is System.Numerics.BigInteger bigInteger)
        {
            return $"global::System.Numerics.BigInteger.Parse({PowerShellCSharpLiteral.QuoteString(bigInteger.ToString(CultureInfo.InvariantCulture))}, global::System.Globalization.CultureInfo.InvariantCulture)";
        }
        return Convert.ToString(literal.Value, CultureInfo.InvariantCulture) ?? "null";
    }
}

internal sealed class PowerShellBoundCSharpResult
{
    internal PowerShellBoundCSharpResult(PowerShellCSharpMethodEmission[] methods, PowerShellSemanticDiagnostic[] diagnostics)
    {
        Methods = methods;
        Diagnostics = diagnostics;
    }

    internal PowerShellCSharpMethodEmission[] Methods { get; }
    internal PowerShellSemanticDiagnostic[] Diagnostics { get; }
    internal bool Success => Methods.Length > 0 && Diagnostics.Length == 0;
}
