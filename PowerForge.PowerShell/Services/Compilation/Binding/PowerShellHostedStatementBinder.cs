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
        var available = GetAvailableSymbols(symbols, statement.Extent.StartOffset);
        if (index == runtimeTailStart)
        {
            bound = PowerShellCommandRegionSemanticBinder.BindRegion(
                document,
                authoredStatements.Skip(index).ToArray(),
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
        while (index + 1 < authoredStatements.Length &&
               PowerShellCommandIslandPolicy.IsRuntimeRegion(
                   authoredStatements[index + 1],
                   body,
                   localFunctionNames,
                   allowedNames,
                   capabilities,
                   commandResolver))
            region.Add(authoredStatements[++index]);
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
