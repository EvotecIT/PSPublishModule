using System.Text;

namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private string EmitCommandRegionArguments(IEnumerable<PowerShellLoweredCommandRegionArgument> arguments)
    {
        var values = arguments.Select(argument => RenderStorage(argument.Symbol)).ToArray();
        return values.Length == 0
            ? "global::System.Array.Empty<object?>()"
            : "new object?[] { " + string.Join(", ", values) + " }";
    }

    private void EmitCommandCapture(
        StringBuilder builder,
        PowerShellLoweredCommandCaptureStatement capture,
        string prefix)
    {
        var targetType = PowerShellCSharpSymbolRenderer.TypeName(capture.TargetType);
        var invocation = $"__invokePowerShellCapture(__statementErrors, {PowerShellCSharpLiteral.QuoteString(capture.HostedFallbackSource)}, {EmitCommandRegionArguments(capture.Arguments)}, {EmitRegionSource(capture.SourceSelection)})";
        var converted = $"({targetType})global::System.Management.Automation.LanguagePrimitives.ConvertTo({invocation}, typeof({targetType}), global::System.Globalization.CultureInfo.InvariantCulture)!";
        if (capture.TargetType == typeof(string)) converted = $"({converted} ?? string.Empty)";
        builder.Append(prefix);
        if (capture.Declare) builder.Append(targetType).Append(' ');
        builder.Append(RenderStorage(capture.Target))
            .Append(" = ").Append(converted).AppendLine(";");
    }

    private void EmitBlock(
        StringBuilder builder,
        IEnumerable<PowerShellLoweredStatement> statements,
        int indent,
        Func<string, string> getTemporaryIdentifier,
        string? discardHelper,
        ICollection<PowerShellCompilationSourceMapEntry> sourceMap,
        bool checkHostInterrupts = false,
        string? successOutputSink = null,
        SourceSpan? continueTarget = null)
    {
        var prefix = new string(' ', indent * 4);
        builder.Append(prefix).AppendLine("{");
        if (checkHostInterrupts)
            builder.Append(prefix).AppendLine("    __checkLoopInterrupts();");
        foreach (var statement in statements)
            EmitStatement(builder, statement, indent + 1, getTemporaryIdentifier, discardHelper, sourceMap, successOutputSink);
        if (continueTarget is { } target) EmitLoopTransferTarget(builder, target, prefix + "    ", isContinue: true);
        builder.Append(prefix).AppendLine("}");
    }

    private void EmitForEach(
        StringBuilder builder,
        PowerShellLoweredForEachStatement loop,
        int indent,
        Func<string, string> getTemporaryIdentifier,
        string? discardHelper,
        ICollection<PowerShellCompilationSourceMapEntry> sourceMap,
        string? successOutputSink)
    {
        var prefix = new string(' ', indent * 4);
        var collection = EmitExpression(loop.Collection);
        if (loop.EnumerationKind == PowerShellForEachEnumerationKind.NativeInvocation)
        {
            EmitNativeForEach(builder, loop, collection, indent, getTemporaryIdentifier, discardHelper, sourceMap, successOutputSink);
            return;
        }
        if (loop.EnumerationKind == PowerShellForEachEnumerationKind.PowerShellEnumerable)
        {
            EmitPowerShellForEach(builder, loop, collection, indent, getTemporaryIdentifier, discardHelper, sourceMap, successOutputSink);
            return;
        }
        if (loop.EnumerationKind == PowerShellForEachEnumerationKind.SystemArray)
        {
            EmitSystemArrayForEach(builder, loop, collection, indent, getTemporaryIdentifier, discardHelper, sourceMap, successOutputSink);
            return;
        }
        if (loop.EnumerationKind == PowerShellForEachEnumerationKind.TypedArray)
        {
            EmitArrayForEach(builder, loop, collection, indent, getTemporaryIdentifier, discardHelper, sourceMap, successOutputSink);
            return;
        }

        var elementTypeName = PowerShellCSharpSymbolRenderer.TypeName(loop.ElementType);
        if (loop.EnumerationKind is not (PowerShellForEachEnumerationKind.ScalarString or PowerShellForEachEnumerationKind.StableScalar))
            throw new InvalidOperationException("Unsupported lowered foreach enumeration contract.");
        // Evaluate the collection once. A nullable/reference scalar has no iterations when null;
        // every non-null scalar, including an empty string, is one PowerShell foreach record.
        var scalarIdentifier = getTemporaryIdentifier("foreachScalar");
        builder.Append(prefix).Append("var ").Append(scalarIdentifier).Append(" = ").Append(collection).AppendLine(";");
        var scalarCanBeNull = !loop.ElementType.IsValueType || Nullable.GetUnderlyingType(loop.ElementType) is not null;
        var enumerable = scalarCanBeNull
            ? $"({scalarIdentifier} is null ? global::System.Array.Empty<{elementTypeName}>() : new[] {{ {scalarIdentifier} }})"
            : $"new[] {{ {scalarIdentifier} }}";
        var iterationVariable = getTemporaryIdentifier("foreachItem");
        builder.Append(prefix).Append("foreach (").Append(elementTypeName).Append(' ')
            .Append(iterationVariable).Append(" in ").Append(enumerable).AppendLine(")");
        builder.Append(prefix).AppendLine("{");
        EmitForEachBody(builder, loop, iterationVariable, indent, getTemporaryIdentifier, discardHelper, sourceMap, successOutputSink);
        builder.Append(prefix).AppendLine("}");
    }

    private void EmitSystemArrayForEach(
        StringBuilder builder,
        PowerShellLoweredForEachStatement loop,
        string collection,
        int indent,
        Func<string, string> getTemporaryIdentifier,
        string? discardHelper,
        ICollection<PowerShellCompilationSourceMapEntry> sourceMap,
        string? successOutputSink)
    {
        var prefix = new string(' ', indent * 4);
        var iterationVariable = getTemporaryIdentifier("foreachItem");
        builder.Append(prefix).Append("foreach (object? ").Append(iterationVariable)
            .Append(" in (").Append(collection).Append(" ?? global::System.Array.Empty<object>()))")
            .AppendLine();
        builder.Append(prefix).AppendLine("{");
        EmitForEachBody(builder, loop, iterationVariable + "!", indent, getTemporaryIdentifier, discardHelper, sourceMap, successOutputSink);
        builder.Append(prefix).AppendLine("}");
    }

    private void EmitArrayForEach(
        StringBuilder builder,
        PowerShellLoweredForEachStatement loop,
        string collection,
        int indent,
        Func<string, string> getTemporaryIdentifier,
        string? discardHelper,
        ICollection<PowerShellCompilationSourceMapEntry> sourceMap,
        string? successOutputSink)
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
        EmitForEachBody(builder, loop, itemIdentifier, indent, getTemporaryIdentifier, discardHelper, sourceMap, successOutputSink);
        builder.Append(prefix).AppendLine("}");
    }

    private void EmitForEachBody(
        StringBuilder builder,
        PowerShellLoweredForEachStatement loop,
        string itemIdentifier,
        int indent,
        Func<string, string> getTemporaryIdentifier,
        string? discardHelper,
        ICollection<PowerShellCompilationSourceMapEntry> sourceMap,
        string? successOutputSink)
    {
        var prefix = new string(' ', (indent + 1) * 4);
        var elementTypeName = PowerShellCSharpSymbolRenderer.TypeName(loop.ElementType);
        // Native foreach checks after successful advancement and before assigning
        // the authored loop variable. General enumerators do this in MoveEnumerator.
        if (loop.CheckHostInterrupts && loop.EnumerationKind != PowerShellForEachEnumerationKind.PowerShellEnumerable)
            builder.Append(prefix).AppendLine("__checkLoopInterrupts();");
        builder.Append(prefix)
            .Append(loop.DeclareVariable ? elementTypeName + " " : string.Empty)
            .Append(RenderStorage(loop.Variable)).Append(" = ")
            .Append(itemIdentifier).AppendLine(";");
        foreach (var nested in loop.Statements)
        {
            EmitStatement(builder, nested, indent + 1, getTemporaryIdentifier, discardHelper, sourceMap, successOutputSink);
        }
        EmitLoopTransferTarget(builder, loop.Span, prefix, isContinue: true);
    }

    private void EmitSwitch(
        StringBuilder builder,
        PowerShellLoweredSwitchStatement statement,
        int indent,
        Func<string, string> getTemporaryIdentifier,
        string? discardHelper,
        ICollection<PowerShellCompilationSourceMapEntry> sourceMap,
        string? successOutputSink)
    {
        var prefix = new string(' ', indent * 4);
        var valueIdentifier = getTemporaryIdentifier("switch_value");
        var matchedIdentifier = getTemporaryIdentifier("switch_matched");
        if (statement.InputKind != PowerShellBoundSwitchInputKind.Scalar)
        {
            var scope = getTemporaryIdentifier("nativeSwitch");
            var flow = getTemporaryIdentifier("switchTransfer");
            builder.Append(prefix).Append("using (var ").Append(scope).AppendLine(" = __nativeFunction.EnterSwitch())");
            builder.Append(prefix).AppendLine("{");
            if (statement.InputKind == PowerShellBoundSwitchInputKind.NativeCommandResults)
            {
                if (statement.Value is not PowerShellLoweredNativeCommandExpression command)
                    throw new InvalidOperationException("Native command-result switch requires its authored command capture.");
                var records = getTemporaryIdentifier("switch_records");
                builder.Append(prefix).Append("    var ").Append(records).AppendLine(" = new global::System.Collections.Generic.List<object?>();");
                builder.Append(prefix).Append("    ").Append(EmitNativeCommandRecords(command, records + ".Add")).AppendLine(";");
                builder.Append(prefix).Append("    ").Append(scope).Append(".Initialize(").Append(records).AppendLine(");");
            }
            else
                builder.Append(prefix).Append("    ").Append(scope).Append(".Initialize(").Append(EmitExpression(statement.Value)).AppendLine(");");
            builder.Append(prefix).Append("    while (").Append(scope).AppendLine(".Cursor.MoveNext())");
            builder.Append(prefix).AppendLine("    {");
            builder.Append(prefix).AppendLine("        try");
            builder.Append(prefix).AppendLine("        {");
            builder.Append(prefix).AppendLine("            __checkLoopInterrupts();");
            builder.Append(prefix).Append("            ").Append(scope).AppendLine(".SetCurrent();");
            builder.Append(prefix).Append("            bool ").Append(matchedIdentifier).AppendLine(" = false;");
            EmitSwitchClauses(builder, statement, indent + 2, valueIdentifier, matchedIdentifier,
                getTemporaryIdentifier, discardHelper, sourceMap, successOutputSink, scope);
            builder.Append(prefix).AppendLine("        }");
            builder.Append(prefix).Append("        catch (global::System.Management.Automation.FlowControlException ").Append(flow)
                .Append(") when (__nativeFunction.IsSwitchTransfer(").Append(flow).AppendLine(", false)) { continue; }");
            builder.Append(prefix).Append("        catch (global::System.Management.Automation.FlowControlException ").Append(flow)
                .Append(") when (__nativeFunction.IsSwitchTransfer(").Append(flow).AppendLine(", true)) { break; }");
            builder.Append(prefix).AppendLine("    }");
            builder.Append(prefix).AppendLine("}");
            return;
        }
        builder.Append(prefix).Append(PowerShellCSharpSymbolRenderer.TypeName(statement.Value.ClrType)).Append(' ')
            .Append(valueIdentifier).Append(" = ").Append(EmitExpression(statement.Value)).AppendLine(";");
        builder.Append(prefix).Append("bool ").Append(matchedIdentifier).AppendLine(" = false;");
        builder.Append(prefix).AppendLine("do");
        builder.Append(prefix).AppendLine("{");
        EmitSwitchClauses(builder, statement, indent, valueIdentifier, matchedIdentifier,
            getTemporaryIdentifier, discardHelper, sourceMap, successOutputSink);
        builder.Append(prefix).AppendLine("}");
        builder.Append(prefix).AppendLine("while (false);");
        if (SwitchAlwaysReturns(statement))
        {
            builder.Append(prefix).AppendLine(
                "throw new global::System.InvalidOperationException(\"An exhaustive PowerShell switch completed without returning.\");");
        }
    }

    private void EmitSwitchClauses(StringBuilder builder, PowerShellLoweredSwitchStatement statement, int indent,
        string valueIdentifier, string matchedIdentifier, Func<string, string> getTemporaryIdentifier,
        string? discardHelper, ICollection<PowerShellCompilationSourceMapEntry> sourceMap, string? successOutputSink,
        string? nativeSwitchScope = null)
    {
        var prefix = new string(' ', indent * 4);
        foreach (var clause in statement.Clauses)
        {
            var clauseSource = EmitExpression(clause.Value);
            var comparison = statement.MatchMode switch
            {
                _ when nativeSwitchScope is not null && clause.Value is PowerShellLoweredNativeScriptBlockExpression { IsSwitchPredicate: true } =>
                    $"{nativeSwitchScope}.MatchesPredicate({clauseSource})",
                _ when nativeSwitchScope is not null => $"{nativeSwitchScope}.Matches({clauseSource}, {(statement.CaseSensitive ? "true" : "false")})",
                PowerShellBoundSwitchMatchMode.Regex =>
                    $"global::System.Text.RegularExpressions.Regex.IsMatch(({valueIdentifier} ?? string.Empty), ({clauseSource} ?? string.Empty), global::System.Text.RegularExpressions.RegexOptions.{(statement.CaseSensitive ? "None" : "IgnoreCase")})",
                _ when statement.InputKind == PowerShellBoundSwitchInputKind.NativeCommandResults ||
                       statement.Value.ClrType == typeof(string) =>
                    $"global::System.String.Equals({valueIdentifier}, {clauseSource}, global::System.StringComparison.{(statement.CaseSensitive ? "InvariantCulture" : "InvariantCultureIgnoreCase")})",
                _ => $"{valueIdentifier} == {clauseSource}"
            };
            builder.Append(prefix).Append("    if (").Append(comparison).AppendLine(")");
            builder.Append(prefix).AppendLine("    {");
            builder.Append(prefix).Append("        ").Append(matchedIdentifier).AppendLine(" = true;");
            foreach (var nested in clause.Statements)
                EmitStatement(builder, nested, indent + 2, getTemporaryIdentifier, discardHelper, sourceMap, successOutputSink);
            builder.Append(prefix).AppendLine("    }");
        }
        if (statement.DefaultStatements is not null)
        {
            builder.Append(prefix).Append("    if (!").Append(matchedIdentifier).AppendLine(")");
            EmitBlock(builder, statement.DefaultStatements, indent + 1, getTemporaryIdentifier, discardHelper, sourceMap,
                successOutputSink: successOutputSink);
        }
    }

    private static bool SwitchAlwaysReturns(PowerShellLoweredSwitchStatement statement)
        => statement.InputKind == PowerShellBoundSwitchInputKind.Scalar && statement.DefaultStatements is not null &&
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
