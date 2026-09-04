using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    private static void ClearFunctionRegionEvidence(
        IDictionary<string, PowerShellBoundRegionCandidate>? candidates,
        IDictionary<string, PowerShellBoundRegionOpportunity>? opportunities,
        string sourcePath,
        string sourceName)
    {
        if (candidates is not null)
        {
            foreach (var key in candidates.Where(pair =>
                         PowerShellCompilationPathSafety.PathEquals(pair.Value.SourcePath, sourcePath) &&
                         pair.Value.SourceName.Equals(sourceName, StringComparison.OrdinalIgnoreCase))
                     .Select(static pair => pair.Key)
                     .ToArray())
                candidates.Remove(key);
        }

        if (opportunities is not null)
        {
            foreach (var key in opportunities.Where(pair =>
                         PowerShellCompilationPathSafety.PathEquals(pair.Value.SourcePath, sourcePath) &&
                         pair.Value.SourceName.Equals(sourceName, StringComparison.OrdinalIgnoreCase))
                     .Select(static pair => pair.Key)
                     .ToArray())
                opportunities.Remove(key);
        }
    }

    private static void AddRegionOpportunities(
        IDictionary<string, PowerShellBoundRegionOpportunity> target,
        ParsedSourceDocument document,
        FunctionDefinitionAst function,
        PowerShellSymbolId functionSymbol,
        IReadOnlyList<PowerShellBoundParameter> parameters,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyList<PowerShellBoundLocal> locals,
        IReadOnlyList<StatementAst> authoredStatements,
        IReadOnlyList<PowerShellBoundStatementBinding> statementBindings)
    {
        var refinedTypes = symbols.Values.ToDictionary(
            static binding => binding.Symbol.StableKey,
            static binding => binding.Type,
            StringComparer.Ordinal);
        var refinedLocals = locals.Select(local => new PowerShellBoundLocal(
                local.Symbol,
                refinedTypes.TryGetValue(local.Symbol.StableKey, out var refinedType)
                    ? refinedType
                    : local.Type))
            .ToArray();
        foreach (var opportunity in PowerShellBoundRegionOpportunitySelector.Discover(
                     document,
                     function,
                     functionSymbol,
                     parameters,
                     refinedLocals,
                     authoredStatements,
                     statementBindings))
            target[opportunity.OpportunityId] = opportunity;
    }
}
