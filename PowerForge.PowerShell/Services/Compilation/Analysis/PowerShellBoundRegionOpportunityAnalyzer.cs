using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

/// <summary>
/// Lowers binder-owned runs and exposes their maximal typed regions as analysis evidence. It does
/// not invoke a code-generation backend and cannot approve or rewrite a Hybrid function.
/// </summary>
internal sealed class PowerShellBoundRegionOpportunityAnalyzer
{
    private readonly PowerShellBoundOptimizer _optimizer;
    private readonly PowerShellSemanticAnalyzer _analyzer;
    private readonly PowerShellTypedLowerer _lowerer;

    internal PowerShellBoundRegionOpportunityAnalyzer(
        PowerShellBoundOptimizer optimizer,
        PowerShellSemanticAnalyzer analyzer,
        PowerShellTypedLowerer lowerer)
    {
        _optimizer = optimizer ?? throw new ArgumentNullException(nameof(optimizer));
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        _lowerer = lowerer ?? throw new ArgumentNullException(nameof(lowerer));
    }

    internal PowerShellCompilationRegionOpportunity[] Analyze(
        IReadOnlyList<PowerShellBoundRegionOpportunity> opportunities,
        PowerShellBoundProgram boundProgram,
        IReadOnlyList<PowerShellBoundRegionCandidate> terminalCandidates,
        PowerShellCompilationCapability capabilities)
    {
        if (opportunities.Count == 0) return Array.Empty<PowerShellCompilationRegionOpportunity>();
        var callable = boundProgram.Functions.ToDictionary(
            static function => function.Symbol.StableKey,
            StringComparer.Ordinal);
        var requiredFunctions = new Dictionary<string, PowerShellBoundFunction>(StringComparer.Ordinal);
        var eligible = new List<PowerShellBoundRegionOpportunity>();
        foreach (var opportunity in opportunities)
        {
            if (!TryResolveCallClosure(opportunity.RegionFunction.Body, callable, out var closure))
                continue;
            eligible.Add(opportunity);
            foreach (var function in closure)
                requiredFunctions[function.Symbol.StableKey] = function;
        }
        if (eligible.Count == 0) return Array.Empty<PowerShellCompilationRegionOpportunity>();

        var discoveryProgram = new PowerShellBoundProgram(
            boundProgram.Documents.ToArray(),
            requiredFunctions.Values.OrderBy(static function => function.Symbol.StableKey, StringComparer.Ordinal)
                .Concat(eligible.Select(static item => item.RegionFunction))
                .ToArray(),
            Array.Empty<PowerShellSemanticDiagnostic>());
        var optimized = _optimizer.Optimize(discoveryProgram);
        var analyzed = _analyzer.AnalyzeRegionOpportunities(optimized.Program);
        var lowered = _lowerer.Lower(analyzed, capabilities);
        var loweredByKey = lowered.Functions.ToDictionary(static function => function.Symbol.StableKey, StringComparer.Ordinal);
        var results = new List<PowerShellCompilationRegionOpportunity>();
        foreach (var opportunity in eligible)
        {
            if (!loweredByKey.TryGetValue(opportunity.RegionFunction.Symbol.StableKey, out var loweredFunction))
                continue;
            var graph = PowerShellLoweredRegionGraphBuilder.Create(loweredFunction);
            foreach (var region in graph.Regions.Where(static region =>
                         region.Execution == PowerShellCompilationRegionExecution.Typed))
            {
                var evidence = CreateEvidence(opportunity, region, graph.Regions, terminalCandidates);
                if (evidence is not null) results.Add(evidence);
            }
        }
        return results.OrderBy(static item => item.SourcePath, PowerShellCompilationPathSafety.PathComparer)
            .ThenBy(static item => item.StartOffset)
            .ToArray();
    }

    private static PowerShellCompilationRegionOpportunity? CreateEvidence(
        PowerShellBoundRegionOpportunity opportunity,
        PowerShellCompilationRegion region,
        IReadOnlyList<PowerShellCompilationRegion> graphRegions,
        IReadOnlyList<PowerShellBoundRegionCandidate> terminalCandidates)
    {
        var selected = opportunity.AllBindings.Where(binding =>
                binding.AuthoredStatementIndex >= opportunity.StartStatementIndex &&
                binding.AuthoredStatementEndIndex <= opportunity.EndStatementIndex &&
                binding.Statement.Span.StartOffset < region.EndOffset &&
                binding.Statement.Span.EndOffset > region.StartOffset)
            .ToArray();
        if (selected.Length == 0) return null;
        var authoredSpan = CreateAuthoredSpan(selected);
        if (graphRegions.Any(item =>
                item.StartOffset < authoredSpan.EndOffset &&
                item.EndOffset > authoredSpan.StartOffset &&
                item.Execution != PowerShellCompilationRegionExecution.Typed))
            return null;
        var regionId = PowerShellLoweredRegionGraphBuilder.CreateRegionId(
            authoredSpan,
            PowerShellCompilationRegionExecution.Typed);
        var startStatementIndex = selected.Min(static item => item.AuthoredStatementIndex);
        var endStatementIndex = selected.Max(static item => item.AuthoredStatementEndIndex);
        var statementIndexes = selected.SelectMany(static item => Enumerable.Range(
                item.AuthoredStatementIndex,
                item.AuthoredStatementEndIndex - item.AuthoredStatementIndex + 1))
            .Distinct()
            .ToArray();
        var selectedStatements = selected.Select(static item => item.Statement).ToArray();
        var canFallThrough = PowerShellBoundRegionOpportunitySelector.CanFallThrough(selectedStatements);
        var hasFollowingStatements = endStatementIndex < opportunity.AuthoredStatementCount - 1;
        var hasUnboundPrecedingStatements = HasUnboundStatements(
            opportunity,
            startStatementIndex: 0,
            count: startStatementIndex);
        var hasUnboundFollowingStatements = canFallThrough && HasUnboundFollowingStatements(opportunity, endStatementIndex);
        var laterStatements = opportunity.AllBindings
            .Where(binding => binding.AuthoredStatementIndex > endStatementIndex)
            .Select(static binding => binding.Statement)
            .ToArray();
        var laterReads = PowerShellBoundRegionOpportunitySelector.EnumerateReadSymbols(laterStatements)
            .Select(PowerShellBoundRegionOpportunitySelector.SymbolIdentity)
            .ToHashSet(StringComparer.Ordinal);
        var externalOutputs = hasUnboundFollowingStatements
            ? region.Mutations
            : region.Mutations.Where(laterReads.Contains);
        var outputs = region.Outputs.Concat(externalOutputs)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
        var enrichedRegion = new PowerShellCompilationRegion(
            regionId,
            ordinal: 0,
            region.Execution,
            authoredSpan.StartOffset,
            authoredSpan.EndOffset,
            authoredSpan.StartLine,
            authoredSpan.StartColumn,
            authoredSpan.EndLine,
            authoredSpan.EndColumn,
            region.Inputs,
            outputs,
            region.Mutations,
            region.Streams,
            region.Errors,
            region.Ordering,
            region.HostedCommandBoundarySites,
            region.ModuleStateReadBoundarySites,
            region.ModuleStateWriteBoundarySites);
        var facts = opportunity.SymbolFacts
            .GroupBy(static fact => PowerShellBoundRegionOpportunitySelector.SymbolIdentity(fact.Symbol), StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
        var liveInputs = CreateTransfers(region.Inputs, facts);
        var liveOutputs = CreateTransfers(outputs.Where(static output => !output.StartsWith("stream:", StringComparison.Ordinal)), facts);
        var continuation = !canFallThrough
            ? PowerShellCompilationRegionContinuation.Terminating
            : !hasFollowingStatements
                ? PowerShellCompilationRegionContinuation.FunctionEnd
                : hasUnboundFollowingStatements
                    ? PowerShellCompilationRegionContinuation.UnboundFallThrough
                    : PowerShellCompilationRegionContinuation.BoundFallThrough;
        var insideTerminalCandidate = terminalCandidates.Any(candidate =>
            PowerShellCompilationPathSafety.PathEquals(candidate.SourcePath, opportunity.SourcePath) &&
            candidate.SourceName.Equals(opportunity.SourceName, StringComparison.OrdinalIgnoreCase) &&
            candidate.RegionFunction.Body.Span.StartOffset <= authoredSpan.StartOffset &&
            candidate.RegionFunction.Body.Span.EndOffset >= authoredSpan.EndOffset);
        var exactSource = opportunity.SourceText.Substring(
            authoredSpan.StartOffset,
            authoredSpan.EndOffset - authoredSpan.StartOffset);
        return new PowerShellCompilationRegionOpportunity(
            "opportunity:" + regionId,
            ComputeSha256(exactSource),
            opportunity.SourceDocumentSha256,
            opportunity.SourceName,
            opportunity.SourceLine,
            opportunity.SourcePath,
            authoredSpan.StartOffset,
            authoredSpan.EndOffset,
            authoredSpan.StartLine,
            authoredSpan.StartColumn,
            authoredSpan.EndLine,
            authoredSpan.EndColumn,
            startStatementIndex,
            endStatementIndex,
            statementIndexes.Length,
            continuation,
            continuation != PowerShellCompilationRegionContinuation.UnboundFallThrough,
            liveInputs.Length == 0 || !hasUnboundPrecedingStatements,
            !canFallThrough || !hasUnboundFollowingStatements,
            insideTerminalCandidate,
            liveInputs,
            liveOutputs,
            PowerShellBoundRegionOpportunitySelector.EnumerateLocalCalls(selectedStatements),
            new PowerShellCompilationRegionGraph(new[] { enrichedRegion }));
    }

    private static SourceSpan CreateAuthoredSpan(IReadOnlyList<PowerShellBoundStatementBinding> bindings)
    {
        var first = bindings.OrderBy(static binding => binding.Statement.Span.StartOffset).First().Statement.Span;
        var last = bindings.OrderBy(static binding => binding.Statement.Span.EndOffset).Last().Statement.Span;
        return new SourceSpan(
            first.DocumentId,
            first.StartOffset,
            last.EndOffset,
            first.StartLine,
            first.StartColumn,
            last.EndLine,
            last.EndColumn);
    }

    private static PowerShellCompilationRegionTransfer[] CreateTransfers(
        IEnumerable<string> identities,
        IReadOnlyDictionary<string, PowerShellBoundRegionOpportunitySelector.SymbolFact> facts)
        => identities.Where(facts.ContainsKey)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .Select(identity =>
            {
                var fact = facts[identity];
                return new PowerShellCompilationRegionTransfer(
                    identity,
                    fact.Type.ClrType.FullName ?? fact.Type.ClrType.Name,
                    fact.Type.Provenance.ToString(),
                    PowerShellStableScalarTypePolicy.IsSupported(fact.Type.ClrType));
            })
            .ToArray();

    private static IEnumerable<PowerShellBoundInvocationExpression> EnumerateInvocations(PowerShellBoundBlock block)
        => PowerShellSemanticAnalyzer.EnumerateStatements(block)
            .SelectMany(PowerShellSemanticAnalyzer.EnumerateDirectExpressions)
            .SelectMany(PowerShellSemanticAnalyzer.EnumerateExpressions)
            .OfType<PowerShellBoundInvocationExpression>();

    private static bool TryResolveCallClosure(
        PowerShellBoundBlock body,
        IReadOnlyDictionary<string, PowerShellBoundFunction> callable,
        out PowerShellBoundFunction[] closure)
    {
        var resolved = new Dictionary<string, PowerShellBoundFunction>(StringComparer.Ordinal);
        var pending = new Queue<PowerShellSymbolId>(EnumerateInvocations(body).Select(static invocation => invocation.Target));
        while (pending.Count > 0)
        {
            var target = pending.Dequeue();
            if (resolved.ContainsKey(target.StableKey)) continue;
            if (!callable.TryGetValue(target.StableKey, out var function))
            {
                closure = Array.Empty<PowerShellBoundFunction>();
                return false;
            }
            resolved[target.StableKey] = function;
            foreach (var nested in EnumerateInvocations(function.Body))
                pending.Enqueue(nested.Target);
        }
        closure = resolved.Values.OrderBy(static function => function.Symbol.StableKey, StringComparer.Ordinal).ToArray();
        return true;
    }

    private static bool HasUnboundFollowingStatements(PowerShellBoundRegionOpportunity opportunity, int endStatementIndex)
        => HasUnboundStatements(
            opportunity,
            endStatementIndex + 1,
            Math.Max(0, opportunity.AuthoredStatementCount - endStatementIndex - 1));

    private static bool HasUnboundStatements(
        PowerShellBoundRegionOpportunity opportunity,
        int startStatementIndex,
        int count)
    {
        var represented = opportunity.AllBindings.SelectMany(static binding => Enumerable.Range(
                binding.AuthoredStatementIndex,
                binding.AuthoredStatementEndIndex - binding.AuthoredStatementIndex + 1))
            .ToHashSet();
        return Enumerable.Range(startStatementIndex, Math.Max(0, count))
            .Any(index => !represented.Contains(index));
    }

    private static string ComputeSha256(string value)
    {
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value))
            .Select(static item => item.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
    }
}
