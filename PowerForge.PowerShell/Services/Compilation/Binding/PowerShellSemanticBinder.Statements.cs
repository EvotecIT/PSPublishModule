using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    private PowerShellBoundStatement? BindStatement(
        ParsedSourceDocument document,
        StatementAst statement,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        bool isTerminal,
        string? targetFramework,
        PowerShellCompilationCapability capabilities,
        bool allowNonTerminalSuccessOutput = false,
        Type? nonTerminalSuccessOutputType = null)
    {
        var bound = BindStatementCore(document, statement, symbols, functions, diagnostics, isTerminal,
            targetFramework, capabilities, allowNonTerminalSuccessOutput, nonTerminalSuccessOutputType);
        if (bound is null || !bound.Capabilities.HasFlag(PowerShellRequiredCapability.PowerShellStatementErrors) &&
            !(bound is PowerShellBoundThrowStatement && capabilities.HasFlag(PowerShellCompilationCapability.PowerShellStatementErrors))) return bound;
        var sourceLines = document.Text.Replace("\r\n", "\n").Split('\n');
        var sourceText = string.Join("\n", sourceLines.Skip(statement.Extent.StartLineNumber - 1)
            .Take(statement.Extent.EndLineNumber - statement.Extent.StartLineNumber + 1));
        if (bound is PowerShellBoundThrowStatement thrown)
            bound = new PowerShellBoundThrowStatement(thrown.Span, thrown.Expression, true, document.Path, sourceText);
        return new PowerShellBoundStatementErrorBoundary(new PowerShellBoundBlock(bound.Span, new[] { bound }), document.Path, sourceText);
    }

    private PowerShellBoundStatement? BindStatementCore(
        ParsedSourceDocument document,
        StatementAst statement,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        bool isTerminal,
        string? targetFramework,
        PowerShellCompilationCapability capabilities,
        bool allowNonTerminalSuccessOutput = false,
        Type? nonTerminalSuccessOutputType = null)
    {
        if (statement is AssignmentStatementAst assignment)
        {
            if (PowerShellRuntimeStateIntrinsicPolicy.TryGetModuleVariableAssignmentName(
                    assignment,
                    capabilities,
                    out var moduleVariableName))
            {
                var moduleValue = BindExpression(
                    document,
                    assignment.Right,
                    symbols,
                    functions,
                    diagnostics,
                    targetFramework: targetFramework,
                    capabilities: capabilities);
                if (moduleValue is null) return null;
                if (moduleValue.Type.ClrType == typeof(void))
                {
                    diagnostics.Add(new PowerShellSemanticDiagnostic(
                        PowerShellCompilationFeatureIds.RuntimeScope,
                        $"Assignment to live module variable '$script:{moduleVariableName}' requires a value-producing typed expression.",
                        PowerShellSourceParser.GetSpan(document, assignment.Right.Extent)));
                    return null;
                }
                return new PowerShellBoundModuleVariableAssignmentStatement(
                    PowerShellSourceParser.GetSpan(document, assignment.Extent),
                    moduleVariableName,
                    moduleValue);
            }
            if (PowerShellAssignmentTargetPolicy.FindDirectVariable(assignment.Left) is { } scopedTarget &&
                IsRuntimeOwnedScope(scopedTarget.VariablePath.UserPath))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic(
                    PowerShellCompilationFeatureIds.RuntimeScope,
                    $"Assignment to runtime-owned scope '${scopedTarget.VariablePath.UserPath}' is outside the bounded runtime-state contract.",
                    PowerShellSourceParser.GetSpan(document, assignment.Extent)));
                return null;
            }
            if (PowerShellAssignmentTargetPolicy.FindDirectVariable(assignment.Left) is { } automatic &&
                PowerShellAssignmentTargetPolicy.IsReadOnlyAutomaticVariable(automatic.VariablePath.UserPath))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic(
                    PowerShellCompilationFeatureIds.AutomaticVariableAssignment,
                    $"Assignment to read-only automatic variable '${automatic.VariablePath.UserPath}' cannot be preserved by a typed artifact.",
                    PowerShellSourceParser.GetSpan(document, assignment.Extent)));
                return null;
            }
            if (PowerShellAssignmentTargetPolicy.FindDirectVariable(assignment.Left) is { } discarded &&
                discarded.VariablePath.UserPath.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                if (assignment.Operator.ToString() != "Equals")
                {
                    diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2405", "The $null discard target supports simple '=' assignment only.", PowerShellSourceParser.GetSpan(document, assignment.Extent)));
                    return null;
                }
                var discardedValue = BindExpression(document, assignment.Right, symbols, functions, diagnostics, targetFramework: targetFramework, capabilities: capabilities);
                return discardedValue is null
                    ? null
                    : new PowerShellBoundExpressionStatement(PowerShellSourceParser.GetSpan(document, assignment.Extent), discardedValue, emitsOutput: false);
            }
            if (assignment.Left is IndexExpressionAst index)
            {
                return PowerShellDictionarySemanticBinder.BindAssignment(
                    document,
                    assignment,
                    index,
                    (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                    capabilities,
                    diagnostics);
            }
            if (assignment.Left is MemberExpressionAst member)
            {
                return PowerShellClrMemberSemanticBinder.BindAssignment(
                    document,
                    assignment,
                    member,
                    (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                    targetFramework,
                    capabilities,
                    diagnostics);
            }
            var mutation = PowerShellMutationSemanticBinder.BindAssignment(
                document,
                assignment,
                symbols,
                (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                diagnostics);
            return mutation is null
                ? null
                : new PowerShellBoundAssignmentStatement(
                    mutation.Span,
                    mutation.Target,
                    mutation.Value!,
                    mutation.Operation,
                    mutation.NormalizeNullString,
                    mutation.IntegralSemantics);
        }
        if (statement is ReturnStatementAst returnStatement)
        {
            var expression = returnStatement.Pipeline is null
                ? null
                : BindExpression(document, returnStatement.Pipeline, symbols, functions, diagnostics, targetFramework: targetFramework, capabilities: capabilities);
            return returnStatement.Pipeline is null || expression is not null
                ? new PowerShellBoundReturnStatement(
                    PowerShellSourceParser.GetSpan(document, returnStatement.Extent),
                    expression,
                    expression is not PowerShellBoundMutationExpression && expression?.Type.ClrType != typeof(void))
                : null;
        }
        if (statement is IfStatementAst ifStatement)
            return BindIfStatement(document, ifStatement, symbols, functions, diagnostics, targetFramework, capabilities, allowNonTerminalSuccessOutput, nonTerminalSuccessOutputType);
        if (statement is WhileStatementAst whileStatement)
            return BindWhileStatement(document, whileStatement, PowerShellBoundLoopKind.While, symbols, functions, diagnostics, targetFramework, capabilities);
        if (statement is DoWhileStatementAst doWhileStatement)
            return BindWhileStatement(document, doWhileStatement, PowerShellBoundLoopKind.DoWhile, symbols, functions, diagnostics, targetFramework, capabilities);
        if (statement is DoUntilStatementAst doUntilStatement)
            return BindWhileStatement(document, doUntilStatement, PowerShellBoundLoopKind.DoUntil, symbols, functions, diagnostics, targetFramework, capabilities);
        if (statement is ForStatementAst forStatement)
            return BindForStatement(document, forStatement, symbols, functions, diagnostics, targetFramework, capabilities);
        if (statement is ForEachStatementAst forEachStatement)
            return BindForEachStatement(document, forEachStatement, symbols, functions, diagnostics, targetFramework, capabilities);
        if (statement is SwitchStatementAst switchStatement)
            return BindSwitchStatement(document, switchStatement, symbols, functions, diagnostics, targetFramework, capabilities);
        if (statement is ThrowStatementAst throwStatement)
        {
            if (throwStatement.IsRethrow)
            {
                if (!PowerShellControlFlowBindingPolicy.HasAncestor<CatchClauseAst>(throwStatement))
                {
                    diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2307", "A bare typed rethrow is valid only inside a catch clause.", PowerShellSourceParser.GetSpan(document, throwStatement.Extent)));
                    return null;
                }
                return new PowerShellBoundThrowStatement(PowerShellSourceParser.GetSpan(document, statement.Extent), null);
            }
            if (throwStatement.Pipeline is null) return null;
            var expression = BindExpression(document, throwStatement.Pipeline, symbols, functions, diagnostics, targetFramework: targetFramework, capabilities: capabilities);
            if (expression is null) return null;
            if (!typeof(Exception).IsAssignableFrom(expression.Type.ClrType))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2308", $"Typed throw requires a CLR exception expression; resolved type was '{expression.Type.ClrType.FullName}'.", expression.Span));
                return null;
            }
            return new PowerShellBoundThrowStatement(PowerShellSourceParser.GetSpan(document, statement.Extent), expression);
        }
        if (statement is TryStatementAst tryStatement)
        {
            if (tryStatement.Finally?.FindAll(static node => node is ReturnStatementAst or BreakStatementAst or ContinueStatementAst, searchNestedScriptBlocks: true).Any() == true)
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2309", "Typed finally blocks cannot alter enclosing return, break, or continue control flow.", PowerShellSourceParser.GetSpan(document, tryStatement.Finally.Extent)));
                return null;
            }
            var baselineSymbols = CloneSymbols(symbols);
            var trySymbols = CloneSymbols(baselineSymbols);
            var body = BindBlock(document, tryStatement.Body, trySymbols, functions, diagnostics, targetFramework, capabilities, terminalOutputReturns: isTerminal);
            if (body is null) return null;
            var catches = new List<PowerShellBoundCatchClause>();
            var pathSymbols = new List<IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding>> { trySymbols };
            foreach (var clause in tryStatement.CatchClauses)
            {
                var types = new List<Type>();
                foreach (var constraint in clause.CatchTypes)
                {
                    var type = constraint.TypeName.GetReflectionType();
                    var supportedPowerShellRuntimeException = type is not null &&
                                                               (type == typeof(System.Management.Automation.RuntimeException) ||
                                                                type == typeof(System.Management.Automation.SessionStateUnauthorizedAccessException)) &&
                                                               capabilities.HasFlag(PowerShellCompilationCapability.PowerShellObjects);
                    if (type is null || !typeof(Exception).IsAssignableFrom(type) ||
                        !supportedPowerShellRuntimeException && !PowerShellGeneratedTypePolicy.IsSupported(type, targetFramework))
                    {
                        diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2310", $"Typed catch '{constraint.TypeName.FullName}' is outside the generated project reference set.", PowerShellSourceParser.GetSpan(document, constraint.Extent)));
                        return null;
                    }
                    types.Add(type);
                }
                var catchSymbols = CloneSymbols(baselineSymbols);
                ForgetTryMutationsOnCatchEntry(catchSymbols, tryStatement.Body);
                var catchBody = BindBlock(document, clause.Body, catchSymbols, functions, diagnostics, targetFramework, capabilities, terminalOutputReturns: isTerminal);
                if (catchBody is null) return null;
                catches.Add(new PowerShellBoundCatchClause(types.ToArray(), catchBody));
                pathSymbols.Add(catchSymbols);
            }
            var catchAll = catches.FindIndex(static clause => clause.ExceptionTypes.Length == 0);
            if (catchAll >= 0 && catchAll != catches.Count - 1)
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2311", "A catch-all clause must follow all typed catches on the conservative typed path.", PowerShellSourceParser.GetSpan(document, tryStatement.CatchClauses[catchAll].Extent)));
                return null;
            }
            var flattened = catches.SelectMany((clause, clauseIndex) =>
                clause.ExceptionTypes.Select(type => new { ClauseIndex = clauseIndex, Type = type })).ToArray();
            for (var index = 0; index < flattened.Length; index++)
            {
                if (flattened.Take(index).Any(previous => previous.Type.IsAssignableFrom(flattened[index].Type)))
                {
                    diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2312", $"Typed catch '{flattened[index].Type.FullName}' is unreachable after a broader earlier catch.", PowerShellSourceParser.GetSpan(document, tryStatement.CatchClauses[flattened[index].ClauseIndex].Extent)));
                    return null;
                }
            }
            var joinedSymbols = CloneSymbols(baselineSymbols);
            MergeSymbolValueStates(joinedSymbols, pathSymbols.ToArray());
            PowerShellBoundBlock? finallyBlock = null;
            if (tryStatement.Finally is not null)
            {
                finallyBlock = BindBlock(document, tryStatement.Finally, joinedSymbols, functions, diagnostics, targetFramework, capabilities);
                if (finallyBlock is null) return null;
            }
            MergeSymbolValueStates(symbols, joinedSymbols);
            return new PowerShellBoundTryStatement(PowerShellSourceParser.GetSpan(document, statement.Extent), body, catches.ToArray(), finallyBlock);
        }
        if (statement is BreakStatementAst { Label: null } breakStatement && PowerShellControlFlowBindingPolicy.HasBreakableAncestor(breakStatement))
            return new PowerShellBoundBreakStatement(PowerShellSourceParser.GetSpan(document, statement.Extent));
        if (statement is ContinueStatementAst { Label: null } continueStatement && PowerShellControlFlowBindingPolicy.HasContinuableAncestor(continueStatement))
            return new PowerShellBoundContinueStatement(PowerShellSourceParser.GetSpan(document, statement.Extent));
        if (statement is BreakStatementAst labeledBreak && labeledBreak.Label is not null)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2313",
                "Labeled break is not supported by the typed compiler.",
                PowerShellSourceParser.GetSpan(document, labeledBreak.Extent)));
            return null;
        }
        if (statement is BreakStatementAst invalidBreak)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2314",
                "break must be inside a supported loop or scalar switch.",
                PowerShellSourceParser.GetSpan(document, invalidBreak.Extent)));
            return null;
        }
        if (statement is ContinueStatementAst labeledContinue && labeledContinue.Label is not null)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2315",
                "Labeled continue is not supported by the typed compiler.",
                PowerShellSourceParser.GetSpan(document, labeledContinue.Extent)));
            return null;
        }
        if (statement is ContinueStatementAst invalidContinue)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2316",
                "continue must be inside a supported loop.",
                PowerShellSourceParser.GetSpan(document, invalidContinue.Extent)));
            return null;
        }
        if (TryBindStatementDiscard(document, statement, symbols, functions, diagnostics, targetFramework, capabilities, out var discard))
            return discard;
        if (statement is PipelineAst { PipelineElements.Count: 1 } streamPipeline &&
            streamPipeline.PipelineElements[0] is CommandAst streamCommand &&
            PowerShellCommandIslandPolicy.TryGetTargetStreamCommand(
                streamCommand,
                capabilities,
                out var streamKind,
                out var messageSyntax,
                out var streamProvider,
                _commandResolver,
                functions.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase)))
        {
            var expectedType = streamKind == PowerShellStreamCommandKind.Success ? null : typeof(string);
            var message = BindExpression(document, messageSyntax, symbols, functions, diagnostics, expectedType, targetFramework, capabilities);
            return message is null
                ? null
                : new PowerShellBoundStreamWriteStatement(PowerShellSourceParser.GetSpan(document, statement.Extent), streamKind, streamProvider!, message);
        }
        if (statement is PipelineAst mappingPipeline &&
            TryBindRuntimeFreePipelineEnumeration(
                document,
                mappingPipeline,
                symbols,
                functions,
                diagnostics,
                targetFramework,
                capabilities,
                out var enumeration))
            return enumeration;
        if (statement is PipelineAst lifecyclePipeline &&
            IsRuntimeFreePipelineLifecycleInvocation(lifecyclePipeline, functions))
        {
            var invocation = BindRuntimeFreePipelineLifecycleInvocation(
                document,
                lifecyclePipeline,
                symbols,
                functions,
                diagnostics,
                targetFramework,
                capabilities);
            if (invocation is null) return null;
            if (!isTerminal)
            {
                if (allowNonTerminalSuccessOutput)
                    return new PowerShellBoundExpressionStatement(
                        PowerShellSourceParser.GetSpan(document, lifecyclePipeline.Extent),
                        invocation,
                        emitsOutput: invocation.Type.ClrType != typeof(void));
                diagnostics.Add(new PowerShellSemanticDiagnostic(
                    "PSB2924",
                    "Runtime-free lifecycle success output must be the terminal result of its enclosing typed function.",
                    PowerShellSourceParser.GetSpan(document, lifecyclePipeline.Extent)));
                return null;
            }
            return new PowerShellBoundReturnStatement(
                PowerShellSourceParser.GetSpan(document, lifecyclePipeline.Extent),
                invocation,
                emitsValue: invocation.Type.ClrType != typeof(void));
        }
        if (statement is PipelineAst pipeline)
        {
            var expression = BindExpression(
                document,
                pipeline,
                symbols,
                functions,
                diagnostics,
                allowNonTerminalSuccessOutput ? nonTerminalSuccessOutputType : null,
                targetFramework,
                capabilities);
            if (expression is null) return null;
            var emitsOutput = expression is not PowerShellBoundMutationExpression && expression.Type.ClrType != typeof(void);
            if (!isTerminal && !allowNonTerminalSuccessOutput && emitsOutput &&
                capabilities.HasFlag(PowerShellCompilationCapability.PowerShellStreams) &&
                capabilities.HasFlag(PowerShellCompilationCapability.PipelineParameterBinding) &&
                expression is PowerShellBoundLiteralExpression or PowerShellBoundVariableExpression or
                    PowerShellBoundClrInvocationExpression { PreserveStatementErrors: true } &&
                PowerShellStableScalarTypePolicy.IsSupported(expression.Type.ClrType))
                return new PowerShellBoundStreamWriteStatement(
                    PowerShellSourceParser.GetSpan(document, statement.Extent),
                    PowerShellStreamCommandKind.Success, provider: null, expression);
            if (!isTerminal && emitsOutput && !IsLocalFunctionPipeline(pipeline, functions, capabilities) && !allowNonTerminalSuccessOutput) return null;
            if (isTerminal && IsLocalFunctionPipeline(pipeline, functions, capabilities))
                return new PowerShellBoundReturnStatement(PowerShellSourceParser.GetSpan(document, statement.Extent), expression, emitsOutput);
            return expression is null
                ? null
                : new PowerShellBoundExpressionStatement(
                    PowerShellSourceParser.GetSpan(document, statement.Extent), expression, emitsOutput,
                    requiresOutputContinuation: emitsOutput && !isTerminal && !allowNonTerminalSuccessOutput);
        }
        return null;
    }

    private PowerShellBoundBlock? BindBlock(
        ParsedSourceDocument document,
        StatementBlockAst syntax,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        string? targetFramework,
        PowerShellCompilationCapability capabilities,
        bool terminalOutputReturns = false,
        bool allowNonTerminalSuccessOutput = false,
        Type? nonTerminalSuccessOutputType = null)
    {
        var statements = new List<PowerShellBoundStatement>();
        for (var index = 0; index < syntax.Statements.Count; index++)
        {
            var statement = syntax.Statements[index];
            var diagnosticCount = diagnostics.Count;
            var bound = BindStatement(
                document,
                statement,
                symbols,
                functions,
                diagnostics,
                isTerminal: terminalOutputReturns && index == syntax.Statements.Count - 1,
                targetFramework,
                capabilities,
                allowNonTerminalSuccessOutput,
                nonTerminalSuccessOutputType);
            if (bound is null)
            {
                if (diagnostics.Count == diagnosticCount)
                    diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2001", $"Statement '{statement.GetType().Name}' is not yet represented by the bound pipeline.", PowerShellSourceParser.GetSpan(document, statement.Extent)));
                return null;
            }
            statements.Add(bound);
        }
        return new PowerShellBoundBlock(PowerShellSourceParser.GetSpan(document, syntax.Extent), statements.ToArray());
    }

}
