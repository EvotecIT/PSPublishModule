using System.Text;

namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private static string EmitCommandRegionArguments(IEnumerable<PowerShellLoweredCommandRegionArgument> arguments)
    {
        var values = arguments.Select(argument => PowerShellCSharpSymbolRenderer.Identifier(argument.Symbol.Name)).ToArray();
        return values.Length == 0
            ? "global::System.Array.Empty<object?>()"
            : "new object?[] { " + string.Join(", ", values) + " }";
    }

    private static void EmitCommandCapture(
        StringBuilder builder,
        PowerShellLoweredCommandCaptureStatement capture,
        string prefix)
    {
        var targetType = PowerShellCSharpSymbolRenderer.TypeName(capture.TargetType);
        var invocation = $"__invokePowerShellCapture({PowerShellCSharpLiteral.QuoteString(capture.HostedFallbackSource)}, {EmitCommandRegionArguments(capture.Arguments)})";
        var converted = $"({targetType})global::System.Management.Automation.LanguagePrimitives.ConvertTo({invocation}, typeof({targetType}), global::System.Globalization.CultureInfo.InvariantCulture)!";
        if (capture.TargetType == typeof(string)) converted = $"({converted} ?? string.Empty)";
        builder.Append(prefix);
        if (capture.Declare) builder.Append(targetType).Append(' ');
        builder.Append(PowerShellCSharpSymbolRenderer.Identifier(capture.Target.Name))
            .Append(" = ").Append(converted).AppendLine(";");
    }

    private static void EmitBlock(
        StringBuilder builder,
        IEnumerable<PowerShellLoweredStatement> statements,
        int indent,
        Func<string, string> getTemporaryIdentifier,
        ICollection<PowerShellCompilationSourceMapEntry> sourceMap)
    {
        var prefix = new string(' ', indent * 4);
        builder.Append(prefix).AppendLine("{");
        foreach (var statement in statements) EmitStatement(builder, statement, indent + 1, getTemporaryIdentifier, sourceMap);
        builder.Append(prefix).AppendLine("}");
    }

    private static void EmitSwitch(
        StringBuilder builder,
        PowerShellLoweredSwitchStatement statement,
        int indent,
        Func<string, string> getTemporaryIdentifier,
        ICollection<PowerShellCompilationSourceMapEntry> sourceMap)
    {
        var prefix = new string(' ', indent * 4);
        var valueIdentifier = getTemporaryIdentifier("switch_value");
        var matchedIdentifier = getTemporaryIdentifier("switch_matched");
        builder.Append(prefix).Append(PowerShellCSharpSymbolRenderer.TypeName(statement.Value.ClrType)).Append(' ')
            .Append(valueIdentifier).Append(" = ").Append(EmitExpression(statement.Value)).AppendLine(";");
        builder.Append(prefix).Append("bool ").Append(matchedIdentifier).AppendLine(" = false;");
        builder.Append(prefix).AppendLine("do");
        builder.Append(prefix).AppendLine("{");
        foreach (var clause in statement.Clauses)
        {
            var clauseSource = EmitExpression(clause.Value);
            var comparison = statement.Value.ClrType == typeof(string)
                ? $"global::System.String.Equals({valueIdentifier}, {clauseSource}, global::System.StringComparison.{(statement.CaseSensitive ? "InvariantCulture" : "InvariantCultureIgnoreCase")})"
                : $"{valueIdentifier} == {clauseSource}";
            builder.Append(prefix).Append("    if (").Append(comparison).AppendLine(")");
            builder.Append(prefix).AppendLine("    {");
            builder.Append(prefix).Append("        ").Append(matchedIdentifier).AppendLine(" = true;");
            foreach (var nested in clause.Statements) EmitStatement(builder, nested, indent + 2, getTemporaryIdentifier, sourceMap);
            builder.Append(prefix).AppendLine("    }");
        }
        if (statement.DefaultStatements is not null)
        {
            builder.Append(prefix).Append("    if (!").Append(matchedIdentifier).AppendLine(")");
            EmitBlock(builder, statement.DefaultStatements, indent + 1, getTemporaryIdentifier, sourceMap);
        }
        builder.Append(prefix).AppendLine("}");
        builder.Append(prefix).AppendLine("while (false);");
        if (SwitchAlwaysReturns(statement))
        {
            builder.Append(prefix).AppendLine(
                "throw new global::System.InvalidOperationException(\"An exhaustive PowerShell switch completed without returning.\");");
        }
    }

    private static bool SwitchAlwaysReturns(PowerShellLoweredSwitchStatement statement)
        => statement.DefaultStatements is not null &&
           statement.Clauses.All(static clause => StatementsAlwaysReturn(clause.Statements)) &&
           StatementsAlwaysReturn(statement.DefaultStatements);

    private static bool StatementsAlwaysReturn(IEnumerable<PowerShellLoweredStatement> statements)
        => statements.LastOrDefault() switch
        {
            PowerShellLoweredReturnStatement { EmitsValue: true } => true,
            PowerShellLoweredThrowStatement => true,
            PowerShellLoweredIfStatement conditional => conditional.ElseStatements is not null &&
                conditional.Clauses.All(static clause => StatementsAlwaysReturn(clause.Statements)) &&
                StatementsAlwaysReturn(conditional.ElseStatements),
            PowerShellLoweredSwitchStatement nested => SwitchAlwaysReturns(nested),
            PowerShellLoweredTryStatement guarded =>
                StatementsAlwaysReturn(guarded.Statements) &&
                guarded.Catches.All(static clause => StatementsAlwaysReturn(clause.Statements)),
            _ => false
        };
}
