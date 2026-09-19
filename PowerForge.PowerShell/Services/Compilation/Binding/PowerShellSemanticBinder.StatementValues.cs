using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
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
}
