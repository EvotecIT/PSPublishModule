using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    private static IfStatementAst? FindTerminalBooleanConditional(PipelineAst pipeline)
    {
        // A sole Boolean record has the same output with or without a terminal subexpression.
        // Leave other shapes on the ordinary value path, where capture cardinality stays explicit.
        if (PowerShellCommandRegionSemanticBinder.IsBackground(pipeline) ||
            pipeline.PipelineElements.Count != 1 ||
            pipeline.PipelineElements[0] is not CommandExpressionAst
            {
                Redirections.Count: 0,
                Expression: SubExpressionAst { SubExpression.Statements.Count: 1 } subexpression
            } || subexpression.SubExpression.Traps?.Count > 0 ||
            subexpression.SubExpression.Statements[0] is not IfStatementAst { ElseClause: { } otherwise } conditional)
            return null;

        var branches = conditional.Clauses.Select(static clause => clause.Item2).Append(otherwise);
        return branches.All(IsSingleBooleanResult) &&
               branches.Any(static branch => branch.Statements[0] is ReturnStatementAst) ? conditional : null;
    }

    private static bool IsSingleBooleanResult(StatementBlockAst branch)
    {
        if (branch.Traps?.Count > 0 || branch.Statements.Count != 1) return false;
        var result = branch.Statements[0] switch
        {
            PipelineAst pipeline => pipeline.GetPureExpression(),
            ReturnStatementAst { Pipeline: PipelineAst pipeline } => pipeline.GetPureExpression(),
            _ => null
        };
        return result?.StaticType == typeof(bool) || result switch
        {
            VariableExpressionAst value => value.VariablePath.UserPath.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                                           value.VariablePath.UserPath.Equals("false", StringComparison.OrdinalIgnoreCase),
            BinaryExpressionAst
            {
                Operator: TokenKind.Igt or TokenKind.Cgt,
                Left: MemberExpressionAst
                {
                    Static: false, Expression: ArrayExpressionAst, Member: StringConstantExpressionAst member
                },
                Right: ConstantExpressionAst { Value: int }
            } => member.Value.Equals("Length", StringComparison.OrdinalIgnoreCase) ||
                 member.Value.Equals("Count", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool RequiresNativeStatementCapture(SubExpressionAst syntax)
        => syntax.SubExpression.Find(static node => node is SwitchStatementAst or TryStatementAst or LoopStatementAst,
            searchNestedScriptBlocks: false) is not null;

    private static bool IsWithinNativeStatementCapture(Ast syntax, Ast boundary)
    {
        for (var parent = syntax.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is SubExpressionAst collection && RequiresNativeStatementCapture(collection)) return true;
            if (ReferenceEquals(parent, boundary)) return false;
        }
        return false;
    }

    /// <summary>Collects bounded statement subexpressions through the existing success-stream and control-flow owner.</summary>
    private PowerShellBoundExpression? BindNativeCollectedStatements(ParsedSourceDocument document, SubExpressionAst syntax,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics, string? targetFramework,
        PowerShellCompilationCapability capabilities)
        => BindNativeCollectedStatements(document, syntax, syntax.SubExpression, symbols, functions, diagnostics,
            targetFramework, capabilities, preserveRecords: false);

    private PowerShellBoundExpression? BindNativeCollectedStatements(ParsedSourceDocument document, Ast syntax,
        StatementBlockAst statements, IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics, string? targetFramework,
        PowerShellCompilationCapability capabilities, bool preserveRecords)
    {
        var span = PowerShellSourceParser.GetSpan(document, syntax.Extent);
        if (!capabilities.HasFlag(PowerShellCompilationCapability.PowerShellStreams) ||
            !capabilities.HasFlag(PowerShellCompilationCapability.PipelineParameterBinding) ||
            syntax.FindAll(static node => node is ReturnStatementAst or TrapStatementAst, false).Any() ||
            PowerShellControlFlowBindingPolicy.HasLoopTransferLeavingCapture(syntax))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2506",
                "Native statement subexpressions require the success-stream host and a closed enclosing-transfer boundary without traps.", span));
            return null;
        }
        var body = BindBlock(document, statements, symbols, functions, diagnostics, targetFramework,
            capabilities, allowNonTerminalSuccessOutput: true);
        return body is null ? null : new PowerShellBoundNativeStatementValueExpression(span,
            PowerShellImplicitOutputPass.NormalizeNativeCapture(body, document.Path, document.Text), preserveRecords,
            shareEmptyResult: _semanticProfile.Family != PowerShellCompilationSemanticHostFamily.WindowsPowerShell51);
    }

    /// <summary>Binds an if-valued RHS without losing PowerShell's zero-versus-null success cardinality.</summary>
    private PowerShellBoundExpression? BindNativeConditionalValue(ParsedSourceDocument document, IfStatementAst syntax,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics, string? targetFramework,
        PowerShellCompilationCapability capabilities, bool preserveRecords)
    {
        var span = PowerShellSourceParser.GetSpan(document, syntax.Extent);
        if (syntax.FindAll(static node => node is ReturnStatementAst or BreakStatementAst or ContinueStatementAst or
                ThrowStatementAst or TrapStatementAst, searchNestedScriptBlocks: false).Any())
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2505",
                "Conditional values with escaping control flow or traps retain their PowerShell statement boundary.", span));
            return null;
        }

        var baseline = CloneSymbols(symbols);
        var paths = new List<IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding>>();
        var clauses = new List<PowerShellBoundNativeConditionalValueClause>();
        foreach (var clause in syntax.Clauses)
        {
            var branchSymbols = CloneSymbols(baseline);
            var condition = BindExpression(document, clause.Item1, branchSymbols, functions, diagnostics,
                typeof(bool), targetFramework, capabilities);
            if (condition is null) return null;
            condition = BindConditionTruthiness(condition, capabilities, diagnostics, document, clause.Item1);
            if (condition is null) return null;
            var value = PowerShellArraySemanticBinder.BindNativeStatementValue(document, clause.Item2,
                (item, itemType) => BindExpression(document, item, branchSymbols, functions, diagnostics,
                    itemType, targetFramework, capabilities), _semanticProfile, diagnostics, preserveRecords);
            if (value is null) return null;
            clauses.Add(new PowerShellBoundNativeConditionalValueClause(condition, value));
            paths.Add(branchSymbols);
        }

        PowerShellBoundExpression otherwise;
        if (syntax.ElseClause is null)
        {
            paths.Add(baseline);
            otherwise = new PowerShellBoundNativeCollectionExpression(span, document.Path,
                Array.Empty<PowerShellBoundNativeCollectionItem>(), false, collapseResult: !preserveRecords);
        }
        else
        {
            var elseSymbols = CloneSymbols(baseline);
            otherwise = PowerShellArraySemanticBinder.BindNativeStatementValue(document, syntax.ElseClause,
                (item, itemType) => BindExpression(document, item, elseSymbols, functions, diagnostics,
                    itemType, targetFramework, capabilities), _semanticProfile, diagnostics, preserveRecords)!;
            if (otherwise is null) return null;
            paths.Add(elseSymbols);
        }
        MergeSymbolValueStates(symbols, paths.ToArray());
        return new PowerShellBoundNativeConditionalValueExpression(span, clauses.ToArray(), otherwise, preserveRecords);
    }
    /// <summary>Binds a try-valued literal through the existing statement/error owner.</summary>
    private PowerShellBoundExpression? BindNativeStatementValue(ParsedSourceDocument document, TryStatementAst syntax,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics, string? targetFramework,
        PowerShellCompilationCapability capabilities)
    {
        var span = PowerShellSourceParser.GetSpan(document, syntax.Extent);
        if (!capabilities.HasFlag(PowerShellCompilationCapability.PowerShellStreams) ||
            !capabilities.HasFlag(PowerShellCompilationCapability.PipelineParameterBinding) ||
            syntax.FindAll(static node => node is ReturnStatementAst or TrapStatementAst, false).Any() ||
            PowerShellControlFlowBindingPolicy.HasLoopTransferLeavingCapture(syntax))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2506",
                "Native try values require the success-stream host and a closed enclosing-transfer boundary without traps.", span));
            return null;
        }
        var statement = BindStatementCore(document, syntax, symbols, functions, diagnostics, false,
            targetFramework, capabilities, allowNonTerminalSuccessOutput: true);
        return statement is null ? null : new PowerShellBoundNativeStatementValueExpression(span,
            PowerShellImplicitOutputPass.NormalizeNativeCapture(new PowerShellBoundBlock(span, new[] { statement }), document.Path, document.Text));
    }

}
