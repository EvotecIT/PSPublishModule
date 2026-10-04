using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    /// <summary>Preserves closed region opportunities when a fully bound function is retained after lowering.</summary>
    internal PowerShellBoundRegionCandidate[] RecoverRetainedRegionCandidates(
        IReadOnlyList<ParsedSourceDocument> documents, PowerShellBoundProgram bound, PowerShellLoweredProgram lowered,
        IReadOnlyList<PowerShellBoundRegionCandidate> candidates, IReadOnlyList<PowerShellBoundRegionOpportunity> opportunities)
    {
        if (!bound.TargetCapabilities.HasFlag(PowerShellCompilationCapability.HybridTypedRegions)) return candidates.ToArray();
        var result = candidates.ToDictionary(static candidate => candidate.RegionId, StringComparer.Ordinal);
        var emitted = lowered.Functions.Select(static function => function.Symbol.StableKey).ToHashSet(StringComparer.Ordinal);
        foreach (var function in bound.Functions.Where(function => function.Symbol.Kind == PowerShellSymbolKind.Function &&
                     !emitted.Contains(function.Symbol.StableKey)))
        {
            var document = documents.FirstOrDefault(document => document.DocumentId == function.Symbol.DocumentId);
            if (document is null) continue;
            var opportunity = opportunities.FirstOrDefault(opportunity =>
                PowerShellCompilationPathSafety.PathEquals(opportunity.SourcePath, document.Path) &&
                opportunity.SourceName.Equals(function.Symbol.Name, StringComparison.OrdinalIgnoreCase));
            if (opportunity is null) continue;
            var syntax = document.SyntaxRoot.Find(node => node is FunctionDefinitionAst definition &&
                definition.Extent.StartOffset == function.Symbol.Declaration.StartOffset &&
                definition.Name.Equals(function.Symbol.Name, StringComparison.OrdinalIgnoreCase), true) as FunctionDefinitionAst;
            if (syntax is null) continue;
            var authored = syntax.Body.EndBlock?.Statements.ToArray() ?? Array.Empty<StatementAst>();
            var bindings = opportunity.AllBindings.ToArray();
            PowerShellBoundRegionCandidate? prefix = null;
            if (PowerShellBoundRegionCandidateSelector.TryCreateContinuation(document, syntax, function.Symbol,
                    function.Parameters.ToArray(), function.Locals.ToArray(), authored, bindings, out var initial) ||
                PowerShellBoundRegionCandidateSelector.TryCreateLaterContinuation(document, syntax, function.Symbol,
                    function.Parameters.ToArray(), function.Locals.ToArray(), authored, bindings, out initial))
            {
                prefix = initial;
                if (!result.ContainsKey(initial.RegionId)) result.Add(initial.RegionId, initial);
            }
            foreach (var candidate in PowerShellBoundRegionCandidateSelector.CreateDetachedContinuations(document, syntax,
                         function.Symbol, function.Parameters.ToArray(), function.Locals.ToArray(), authored, bindings, prefix))
                if (!result.ContainsKey(candidate.RegionId)) result.Add(candidate.RegionId, candidate);
        }
        return result.Values.OrderBy(static candidate => candidate.RegionFunction.Symbol.StableKey, StringComparer.Ordinal).ToArray();
    }
}
