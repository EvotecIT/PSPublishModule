using System.Management.Automation.Language;

namespace PowerForge;

internal static class PowerShellHostedStatementBinder
{
    internal static bool TryBind(
        ParsedSourceDocument document,
        StatementAst[] authoredStatements,
        ScriptBlockAst body,
        ISet<string> localFunctionNames,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellBoundParameter> parameters,
        int runtimeTailStart,
        PowerShellCommandSemanticResolver commandResolver,
        PowerShellCompilationCapability capabilities,
        ref int index,
        out PowerShellBoundStatement? bound)
    {
        bound = null;
        var statement = authoredStatements[index];
        // Keep cleanup visible to typed control-flow analysis. Hiding it inside a
        // hosted region bypasses the downstream-stop preference/capture contract.
        if (ContainsFinally(statement)) return false;
        var available = GetAvailableSymbols(symbols, statement.Extent.StartOffset);
        if (index == runtimeTailStart)
        {
            var tail = authoredStatements.Skip(index).ToArray();
            if (tail.Any(ContainsFinally)) return false;
            if (PowerShellModuleStateOriginPolicy.ReferencesDerivedModuleState(tail, available, capabilities))
                return false;
            bound = PowerShellCommandRegionSemanticBinder.BindRegion(
                document,
                tail,
                available,
                parameters,
                commandResolver,
                localFunctionNames,
                capabilities);
            index = authoredStatements.Length;
            return true;
        }
        var allowedNames = available.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (PowerShellCommandIslandPolicy.TryGetCapturedRuntimeAssignment(
                statement,
                body,
                localFunctionNames,
                allowedNames,
                capabilities,
                commandResolver,
                out var captured))
        {
            var capturedTarget = PowerShellAssignmentTargetPolicy.FindDirectVariable(captured.Left);
            if (capturedTarget is null || !symbols.TryGetValue(capturedTarget.VariablePath.UserPath, out var capturedSymbol) ||
                !PowerShellAssignmentTargetPolicy.PreservesConstraint(captured.Left, capturedSymbol.Type))
                return false;
            if (PowerShellModuleStateOriginPolicy.ReferencesDerivedModuleState(
                    new Ast[] { captured.Right },
                    available,
                    capabilities))
                return false;
            bound = PowerShellCommandRegionSemanticBinder.BindCapture(
                document,
                captured,
                GetCaptureSymbols(available, symbols, captured),
                parameters,
                commandResolver,
                localFunctionNames,
                capabilities);
            return true;
        }
        if (!PowerShellCommandIslandPolicy.IsRuntimeRegion(statement, body, localFunctionNames, allowedNames, capabilities, commandResolver))
            return false;

        var region = new List<StatementAst> { statement };
        var regionEnd = index;
        while (regionEnd + 1 < authoredStatements.Length &&
               !ContainsFinally(authoredStatements[regionEnd + 1]) &&
               PowerShellCommandIslandPolicy.IsRuntimeRegion(
                   authoredStatements[regionEnd + 1],
                   body,
                   localFunctionNames,
                   allowedNames,
                   capabilities,
                   commandResolver))
            region.Add(authoredStatements[++regionEnd]);
        if (PowerShellModuleStateOriginPolicy.ReferencesDerivedModuleState(region, available, capabilities))
            return false;
        index = regionEnd;
        bound = PowerShellCommandRegionSemanticBinder.BindRegion(
            document,
            region,
            available,
            parameters,
            commandResolver,
            localFunctionNames,
            capabilities);
        return true;
    }

    private static bool ContainsFinally(Ast syntax)
        => syntax.FindAll(static node => node is TryStatementAst { Finally: not null }, searchNestedScriptBlocks: false).Any();

    private static IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> GetAvailableSymbols(
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        int boundaryOffset)
        => symbols.Where(static pair => pair.Value.Symbol.Kind == PowerShellSymbolKind.Parameter)
            .Concat(symbols.Where(pair => pair.Value.Symbol.Kind == PowerShellSymbolKind.Local && pair.Value.Symbol.Declaration.StartOffset < boundaryOffset))
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> GetCaptureSymbols(
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> available,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> all,
        AssignmentStatementAst assignment)
    {
        var result = available.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var targetName = ((VariableExpressionAst)((ConvertExpressionAst)assignment.Left).Child).VariablePath.UserPath;
        result[targetName] = all[targetName];
        return result;
    }
}
