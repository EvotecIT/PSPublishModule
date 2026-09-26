using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    private PowerShellBoundStatement? BindSwitchStatement(
        ParsedSourceDocument document,
        SwitchStatementAst statement,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        string? targetFramework,
        PowerShellCompilationCapability capabilities)
    {
        if ((statement.Flags & (SwitchFlags.File | SwitchFlags.Wildcard | SwitchFlags.Parallel)) != 0)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2304",
                $"Switch flags '{statement.Flags}' require PowerShell runtime matching semantics.",
                PowerShellSourceParser.GetSpan(document, statement.Extent)));
            return null;
        }

        var matchMode = (statement.Flags & SwitchFlags.Regex) != 0
            ? PowerShellBoundSwitchMatchMode.Regex
            : PowerShellBoundSwitchMatchMode.Exact;
        if (PowerShellAutomaticVariableObservationPolicy.ObservesSwitchState(statement))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2304",
                "Scalar switch whose $_, $PSItem, or $switch automatic-variable state is observed requires PowerShell runtime semantics.",
                PowerShellSourceParser.GetSpan(document, statement.Extent)));
            return null;
        }
        if (matchMode == PowerShellBoundSwitchMatchMode.Regex &&
            PowerShellAutomaticVariableObservationPolicy.Observes(statement, "Matches"))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2304",
                "Regex switch whose $Matches automatic-variable state is observed requires PowerShell runtime semantics.",
                PowerShellSourceParser.GetSpan(document, statement.Extent)));
            return null;
        }

        var value = BindExpression(
            document,
            statement.Condition,
            symbols,
            functions,
            diagnostics,
            targetFramework: targetFramework,
            capabilities: capabilities);
        if (value is null) return null;
        var nativeCommandResults = value is PowerShellBoundNativeCommandExpression &&
            matchMode == PowerShellBoundSwitchMatchMode.Exact &&
            statement.Condition is PipelineAst commandPipeline &&
            PowerShellCommandRegionSemanticBinder.IsNativeLiteralCommandValue(commandPipeline, capabilities);
        if (nativeCommandResults &&
            (!PowerShellLoopInterruptContract.IsAvailable(capabilities) ||
             statement.Clauses.Any(clause => clause.Item1 is not StringConstantExpressionAst ||
                 !IsClosedNativeCommandSwitchClause(clause.Item2)) ||
             statement.Default is not null && !IsClosedNativeCommandSwitchClause(statement.Default)))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2305",
                "Native command-result switch requires exact literal-string clauses with closed output bodies and a cooperative host iterator.",
                PowerShellSourceParser.GetSpan(document, statement.Extent)));
            return null;
        }
        // Native invocation storage is intentionally object-valued. An authored
        // [string] parameter still has a PowerShell-owned conversion constraint,
        // so a read of that exact parameter can be used as a scalar string here.
        // Do not infer a CLR type for untyped or merely inferred native locals.
        if (matchMode == PowerShellBoundSwitchMatchMode.Exact &&
            value is PowerShellBoundNativeVariableExpression nativeValue &&
            UnwrapExpression(statement.Condition, preservePipeline: true) is VariableExpressionAst variable &&
            variable.VariablePath.IsUnqualified &&
            nativeValue.Name.Equals(variable.VariablePath.UserPath, StringComparison.OrdinalIgnoreCase) &&
            symbols.TryGetValue(nativeValue.Name, out var nativeSymbol) &&
            nativeSymbol.Symbol.Kind == PowerShellSymbolKind.Parameter &&
            CanRecoverNativeStringParameter(statement, nativeValue.Name, out var functionBody) &&
            PowerShellParameterSyntax.GetParameters(functionBody).Any(parameter =>
                parameter.Name.VariablePath.UserPath.Equals(nativeValue.Name, StringComparison.OrdinalIgnoreCase) &&
                parameter.StaticType == typeof(string) &&
                parameter.Attributes.OfType<TypeConstraintAst>().Any()))
            value = new PowerShellBoundConversionExpression(value.Span,
                new PowerShellTypeFact(typeof(string), PowerShellTypeFactProvenance.Explicit,
                    "The invocation-owned parameter retains its authored String constraint."), value);

        var valueType = nativeCommandResults ? typeof(string) : value.Type.ClrType;
        if (matchMode == PowerShellBoundSwitchMatchMode.Regex && valueType != typeof(string))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2305",
                $"Scalar regex switch requires a String condition; resolved type was '{valueType.FullName}'.",
                value.Span));
            return null;
        }
        if (valueType != typeof(bool) &&
            valueType != typeof(char) &&
            valueType != typeof(string) &&
            !PowerShellClrTypeSemantics.IsNumeric(valueType))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2305",
                $"Scalar switch requires a Boolean, character, string, or numeric condition; resolved type was '{valueType.FullName}'.",
                value.Span));
            return null;
        }

        var baselineSymbols = CloneSymbols(symbols);
        var pathSymbols = new List<IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding>>();
        var clauses = new List<PowerShellBoundSwitchClause>();
        foreach (var clause in statement.Clauses)
        {
            var clauseSymbols = CloneSymbols(baselineSymbols);
            var clauseValue = BindExpression(
                document,
                clause.Item1,
                clauseSymbols,
                functions,
                diagnostics,
                valueType,
                targetFramework,
                capabilities);
            if (clauseValue is null) return null;
            if (clauseValue.Type.ClrType != valueType)
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic(
                    "PSB2306",
                    $"Scalar switch clause type '{clauseValue.Type.ClrType.FullName}' must exactly match condition type '{valueType.FullName}' to avoid PowerShell coercion semantics.",
                    clauseValue.Span));
                return null;
            }
            if (matchMode == PowerShellBoundSwitchMatchMode.Regex &&
                !PowerShellSwitchRegexPatternPolicy.TryValidateLiteral(clauseValue, out var patternDiagnostic))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2306", patternDiagnostic, clauseValue.Span));
                return null;
            }

            var body = BindBlock(document, clause.Item2, clauseSymbols, functions, diagnostics, targetFramework, capabilities);
            if (body is null) return null;
            clauses.Add(new PowerShellBoundSwitchClause(clauseValue, body));
            pathSymbols.Add(clauseSymbols);
        }

        PowerShellBoundBlock? defaultBlock = null;
        if (statement.Default is null)
        {
            pathSymbols.Add(baselineSymbols);
        }
        else
        {
            var defaultSymbols = CloneSymbols(baselineSymbols);
            defaultBlock = BindBlock(document, statement.Default, defaultSymbols, functions, diagnostics, targetFramework, capabilities);
            if (defaultBlock is null) return null;
            pathSymbols.Add(defaultSymbols);
        }
        MergeSymbolValueStates(symbols, pathSymbols.ToArray());
        return new PowerShellBoundSwitchStatement(
            PowerShellSourceParser.GetSpan(document, statement.Extent),
            value,
            clauses.ToArray(),
            defaultBlock,
            matchMode,
            (statement.Flags & SwitchFlags.CaseSensitive) != 0,
            nativeCommandResults ? PowerShellBoundSwitchInputKind.NativeCommandResults : PowerShellBoundSwitchInputKind.Scalar);
    }

    private static bool IsClosedNativeCommandSwitchClause(StatementBlockAst body)
        => body.Traps is null or { Count: 0 } && body.Statements.All(statement =>
            statement is BreakStatementAst { Label: null } ||
            statement is PipelineAst { PipelineElements.Count: 1 } pipeline &&
            pipeline.PipelineElements[0] is CommandExpressionAst command &&
            (command.Expression is StringConstantExpressionAst ||
             command.Expression is BinaryExpressionAst
             {
                 Operator: TokenKind.Isplit or TokenKind.Csplit,
                 Left: StringConstantExpressionAst,
                 Right: StringConstantExpressionAst { Value: "," }
             }));

    private static bool CanRecoverNativeStringParameter(SwitchStatementAst statement, string name,
        out ScriptBlockAst functionBody)
    {
        functionBody = FindOwningFunctionBody(statement)!;
        if (functionBody is null || functionBody.BeginBlock is not null || functionBody.ProcessBlock is not null ||
            functionBody.GetType().GetProperty("CleanBlock")?.GetValue(functionBody) is not null ||
            functionBody.EndBlock is not { } endBlock || !endBlock.Statements.Contains(statement))
            return false;

        // A command, member call, or script block can remove and recreate even
        // a constrained parameter variable in the active scope. The constraint
        // then no longer proves that the switch input is scalar or a String.
        foreach (var preceding in endBlock.Statements.TakeWhile(item => !ReferenceEquals(item, statement)))
            if (preceding.Find(static node => node is CommandAst or InvokeMemberExpressionAst or
                    ScriptBlockExpressionAst or FunctionDefinitionAst,
                    searchNestedScriptBlocks: true) is not null ||
                preceding.Find(node => node is AssignmentStatementAst assignment &&
                    PowerShellAssignmentTargetPolicy.FindDirectVariable(assignment.Left) is { } target &&
                    target.VariablePath.UserPath.Equals(name, StringComparison.OrdinalIgnoreCase),
                    searchNestedScriptBlocks: true) is not null)
                return false;
        return true;
    }
}
