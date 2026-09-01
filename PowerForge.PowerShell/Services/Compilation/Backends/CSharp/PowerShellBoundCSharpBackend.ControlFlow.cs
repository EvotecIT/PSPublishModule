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

    private static void EmitForEach(
        StringBuilder builder,
        PowerShellLoweredForEachStatement loop,
        int indent,
        Func<string, string> getTemporaryIdentifier,
        ICollection<PowerShellCompilationSourceMapEntry> sourceMap)
    {
        var prefix = new string(' ', indent * 4);
        var collection = EmitExpression(loop.Collection);
        if (!loop.ScalarString && loop.Collection.ClrType.IsArray)
        {
            EmitArrayForEach(builder, loop, collection, indent, getTemporaryIdentifier, sourceMap);
            return;
        }

        var elementTypeName = PowerShellCSharpSymbolRenderer.TypeName(loop.ElementType);
        var enumerable = loop.ScalarString
            ? $"new[] {{ {collection} }}"
            : $"({collection} ?? global::System.Array.Empty<{elementTypeName}>())";
        var iterationVariable = getTemporaryIdentifier("foreachItem");
        builder.Append(prefix).Append("foreach (").Append(elementTypeName).Append(' ')
            .Append(iterationVariable).Append(" in ").Append(enumerable).AppendLine(")");
        builder.Append(prefix).AppendLine("{");
        EmitForEachBody(builder, loop, iterationVariable, indent, getTemporaryIdentifier, sourceMap);
        builder.Append(prefix).AppendLine("}");
    }

    private static void EmitArrayForEach(
        StringBuilder builder,
        PowerShellLoweredForEachStatement loop,
        string collection,
        int indent,
        Func<string, string> getTemporaryIdentifier,
        ICollection<PowerShellCompilationSourceMapEntry> sourceMap)
    {
        var prefix = new string(' ', indent * 4);
        var arrayIdentifier = getTemporaryIdentifier("foreachArray");
        var elementTypeName = PowerShellCSharpSymbolRenderer.TypeName(loop.ElementType);
        builder.Append(prefix).Append(elementTypeName).Append("[] ").Append(arrayIdentifier)
            .Append(" = ").Append(collection);
        if (loop.NullCollectionElement is null)
        {
            builder.Append(" ?? global::System.Array.Empty<").Append(elementTypeName).AppendLine(">();");
        }
        else
        {
            var nullElement = loop.NullCollectionElement is PowerShellLoweredLiteralExpression { Value: null } &&
                              !loop.ElementType.IsValueType
                ? "default!"
                : EmitExpression(loop.NullCollectionElement);
            builder.Append(" ?? new ").Append(elementTypeName).Append("[] { ")
                .Append(nullElement).AppendLine(" };");
        }

        var indexIdentifier = getTemporaryIdentifier("foreachIndex");
        var itemIdentifier = getTemporaryIdentifier("foreachItem");
        builder.Append(prefix).Append("for (int ").Append(indexIdentifier).Append(" = 0; ")
            .Append(indexIdentifier).Append(" < ").Append(arrayIdentifier).Append(".Length; ")
            .Append(indexIdentifier).AppendLine("++)");
        builder.Append(prefix).AppendLine("{");
        builder.Append(prefix).Append("    ").Append(elementTypeName).Append(' ').Append(itemIdentifier)
            .Append(" = ").Append(arrayIdentifier).Append('[').Append(indexIdentifier).AppendLine("];");
        EmitForEachBody(builder, loop, itemIdentifier, indent, getTemporaryIdentifier, sourceMap);
        builder.Append(prefix).AppendLine("}");
    }

    private static void EmitForEachBody(
        StringBuilder builder,
        PowerShellLoweredForEachStatement loop,
        string itemIdentifier,
        int indent,
        Func<string, string> getTemporaryIdentifier,
        ICollection<PowerShellCompilationSourceMapEntry> sourceMap)
    {
        var prefix = new string(' ', (indent + 1) * 4);
        var elementTypeName = PowerShellCSharpSymbolRenderer.TypeName(loop.ElementType);
        builder.Append(prefix)
            .Append(loop.DeclareVariable ? elementTypeName + " " : string.Empty)
            .Append(PowerShellCSharpSymbolRenderer.Identifier(loop.Variable.Name)).Append(" = ")
            .Append(itemIdentifier).AppendLine(";");
        foreach (var nested in loop.Statements)
        {
            EmitStatement(builder, nested, indent + 1, getTemporaryIdentifier, sourceMap);
        }
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
            var comparison = statement.MatchMode switch
            {
                PowerShellBoundSwitchMatchMode.Regex =>
                    $"global::System.Text.RegularExpressions.Regex.IsMatch(({valueIdentifier} ?? string.Empty), ({clauseSource} ?? string.Empty), global::System.Text.RegularExpressions.RegexOptions.{(statement.CaseSensitive ? "None" : "IgnoreCase")})",
                _ when statement.Value.ClrType == typeof(string) =>
                    $"global::System.String.Equals({valueIdentifier}, {clauseSource}, global::System.StringComparison.{(statement.CaseSensitive ? "InvariantCulture" : "InvariantCultureIgnoreCase")})",
                _ => $"{valueIdentifier} == {clauseSource}"
            };
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
