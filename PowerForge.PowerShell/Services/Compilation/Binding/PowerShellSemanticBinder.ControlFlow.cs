using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    private PowerShellBoundStatement? BindIfStatement(
        ParsedSourceDocument document,
        IfStatementAst statement,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        string? targetFramework,
        PowerShellCompilationCapability capabilities,
        bool allowNonTerminalSuccessOutput,
        Type? nonTerminalSuccessOutputType)
    {
        var clauses = new List<PowerShellBoundConditionalClause>();
        var baselineSymbols = CloneSymbols(symbols);
        var pathSymbols = new List<IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding>>();
        foreach (var clause in statement.Clauses)
        {
            var clauseSymbols = CloneSymbols(baselineSymbols);
            var condition = BindExpression(document, clause.Item1, clauseSymbols, functions, diagnostics, typeof(bool), targetFramework, capabilities);
            if (condition is null) return null;
            condition = BindConditionTruthiness(condition, capabilities, diagnostics);
            if (condition is null) return null;
            var body = BindBlock(
                document,
                clause.Item2,
                clauseSymbols,
                functions,
                diagnostics,
                targetFramework,
                capabilities,
                allowNonTerminalSuccessOutput: allowNonTerminalSuccessOutput,
                nonTerminalSuccessOutputType: nonTerminalSuccessOutputType);
            if (body is null) return null;
            clauses.Add(new PowerShellBoundConditionalClause(condition, body));
            pathSymbols.Add(clauseSymbols);
        }
        PowerShellBoundBlock? elseBlock = null;
        if (statement.ElseClause is null)
        {
            pathSymbols.Add(baselineSymbols);
        }
        else
        {
            var elseSymbols = CloneSymbols(baselineSymbols);
            elseBlock = BindBlock(
                document,
                statement.ElseClause,
                elseSymbols,
                functions,
                diagnostics,
                targetFramework,
                capabilities,
                allowNonTerminalSuccessOutput: allowNonTerminalSuccessOutput,
                nonTerminalSuccessOutputType: nonTerminalSuccessOutputType);
            if (elseBlock is null) return null;
            pathSymbols.Add(elseSymbols);
        }
        MergeSymbolValueStates(symbols, pathSymbols.ToArray());
        return new PowerShellBoundIfStatement(PowerShellSourceParser.GetSpan(document, statement.Extent), clauses.ToArray(), elseBlock);
    }

    private PowerShellBoundStatement? BindWhileStatement(
        ParsedSourceDocument document,
        LoopStatementAst statement,
        PowerShellBoundLoopKind kind,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        string? targetFramework,
        PowerShellCompilationCapability capabilities)
    {
        var baselineSymbols = CloneSymbols(symbols);
        var loopSymbols = CloneSymbols(baselineSymbols);
        PowerShellBoundExpression? condition;
        PowerShellBoundBlock? body;
        if (kind == PowerShellBoundLoopKind.While)
        {
            condition = BindExpression(document, statement.Condition, loopSymbols, functions, diagnostics, typeof(bool), targetFramework, capabilities);
            if (condition is null) return null;
            condition = BindConditionTruthiness(condition, capabilities, diagnostics);
            if (condition is null) return null;
            body = BindBlock(document, statement.Body, loopSymbols, functions, diagnostics, targetFramework, capabilities);
        }
        else
        {
            body = BindBlock(document, statement.Body, loopSymbols, functions, diagnostics, targetFramework, capabilities);
            if (body is null) return null;
            var conditionSymbols = CloneSymbols(loopSymbols);
            if (HasPostTestFlowTransfer(statement.Body))
                MergeSymbolValueStates(conditionSymbols, baselineSymbols, loopSymbols);
            condition = BindExpression(document, statement.Condition, conditionSymbols, functions, diagnostics, typeof(bool), targetFramework, capabilities);
            if (condition is null) return null;
            condition = BindConditionTruthiness(condition, capabilities, diagnostics);
        }
        if (condition is null) return null;
        if (body is null) return null;
        if (kind == PowerShellBoundLoopKind.While || HasPostTestFlowTransfer(statement.Body))
            MergeSymbolValueStates(symbols, baselineSymbols, loopSymbols);
        else
            MergeSymbolValueStates(symbols, loopSymbols);
        return new PowerShellBoundWhileStatement(PowerShellSourceParser.GetSpan(document, statement.Extent), kind, condition, body);
    }

    private static bool HasPostTestFlowTransfer(StatementBlockAst body)
        => body.FindAll(
                static node => node is BreakStatementAst or ContinueStatementAst or ReturnStatementAst or ThrowStatementAst,
                searchNestedScriptBlocks: false)
            .Any();

    private PowerShellBoundStatement? BindForStatement(
        ParsedSourceDocument document,
        ForStatementAst statement,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        string? targetFramework,
        PowerShellCompilationCapability capabilities)
    {
        var initializer = statement.Initializer is null
            ? null
            : BindExpression(document, statement.Initializer, symbols, functions, diagnostics, targetFramework: targetFramework, capabilities: capabilities) as PowerShellBoundMutationExpression;
        if (statement.Initializer is not null && initializer is null) return null;
        var baselineSymbols = CloneSymbols(symbols);
        var loopSymbols = CloneSymbols(baselineSymbols);
        var condition = statement.Condition is null
            ? null
            : BindExpression(document, statement.Condition, loopSymbols, functions, diagnostics, typeof(bool), targetFramework, capabilities);
        if (condition is not null) condition = BindConditionTruthiness(condition, capabilities, diagnostics);
        if (statement.Condition is not null && condition is null) return null;
        var body = BindBlock(document, statement.Body, loopSymbols, functions, diagnostics, targetFramework, capabilities);
        if (body is null) return null;
        var iterator = statement.Iterator is null
            ? null
            : BindExpression(document, statement.Iterator, loopSymbols, functions, diagnostics, targetFramework: targetFramework, capabilities: capabilities) as PowerShellBoundMutationExpression;
        if (statement.Iterator is not null && iterator is null) return null;
        MergeSymbolValueStates(symbols, baselineSymbols, loopSymbols);
        return new PowerShellBoundForStatement(PowerShellSourceParser.GetSpan(document, statement.Extent), initializer, condition, iterator, body);
    }

    private PowerShellBoundStatement? BindForEachStatement(
        ParsedSourceDocument document,
        ForEachStatementAst statement,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        string? targetFramework,
        PowerShellCompilationCapability capabilities)
    {
        var variableSpan = PowerShellSourceParser.GetSpan(document, statement.Variable.Extent);
        if (!symbols.TryGetValue(statement.Variable.VariablePath.UserPath, out var target))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2302", $"foreach variable '${statement.Variable.VariablePath.UserPath}' has no function-scope semantic symbol.", variableSpan));
            return null;
        }
        var collection = BindExpression(document, statement.Condition, symbols, functions, diagnostics, targetFramework: targetFramework, capabilities: capabilities);
        if (collection is null) return null;
        if (PowerShellModuleStateOriginPolicy.IsDerived(collection))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2304",
                "foreach cannot carry a value derived from live Hybrid module state into a typed loop variable.",
                collection.Span));
            return null;
        }
        var collectionType = collection.Type.ClrType;
        var scalarString = collectionType == typeof(string) && collection.Type.Provenance is PowerShellTypeFactProvenance.Explicit or PowerShellTypeFactProvenance.Literal;
        var systemArray = collectionType == typeof(Array) &&
                          PowerShellCompilationParameterTypePolicy.CanUseUntypedObject(capabilities);
        var elementType = collectionType.IsArray && collectionType.GetArrayRank() == 1
            ? collectionType.GetElementType()
            : scalarString
                ? typeof(string)
                : systemArray ? typeof(object) : null;
        if (elementType is null || elementType != target.Type.ClrType)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2303",
                "foreach collection enumeration requires a statically typed one-dimensional array, an explicitly typed scalar string, or a generated PowerShell host that preserves System.Array items as objects.",
                collection.Span));
            return null;
        }
        var baselineSymbols = CloneSymbols(symbols);
        var loopSymbols = CloneSymbols(baselineSymbols);
        var body = BindBlock(document, statement.Body, loopSymbols, functions, diagnostics, targetFramework, capabilities);
        if (body is null) return null;
        MergeSymbolValueStates(symbols, baselineSymbols, loopSymbols);
        return new PowerShellBoundForEachStatement(
            PowerShellSourceParser.GetSpan(document, statement.Extent),
            target.Symbol,
            elementType,
            collection,
            scalarString,
            body,
            systemArray: systemArray);
    }
}
