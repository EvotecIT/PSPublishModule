namespace PowerForge;

/// <summary>
/// Composes parsing, binding, deterministic analysis, lowering, and C# emission for one semantic result.
/// </summary>
internal sealed class PowerShellSemanticCompilationPipeline
{
    private readonly PowerShellSemanticBinder _binder;
    private readonly PowerShellBoundOptimizer _optimizer;
    private readonly PowerShellSemanticAnalyzer _analyzer;
    private readonly PowerShellTypedLowerer _lowerer;
    private readonly PowerShellBoundCSharpBackend _backend;

    internal PowerShellSemanticCompilationPipeline()
        : this(PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId)
    {
    }

    internal PowerShellSemanticCompilationPipeline(string semanticProfileId)
        : this(new PowerShellSemanticBinder(semanticProfileId), new PowerShellBoundOptimizer(), new PowerShellSemanticAnalyzer(), new PowerShellTypedLowerer(), new PowerShellBoundCSharpBackend())
    {
    }

    internal PowerShellSemanticCompilationPipeline(PowerShellCommandSemanticRegistry commandRegistry)
        : this(commandRegistry, PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId)
    {
    }

    internal PowerShellSemanticCompilationPipeline(PowerShellCommandSemanticRegistry commandRegistry, string semanticProfileId)
        : this(new PowerShellSemanticBinder(commandRegistry, semanticProfileId), new PowerShellBoundOptimizer(), new PowerShellSemanticAnalyzer(), new PowerShellTypedLowerer(), new PowerShellBoundCSharpBackend())
    {
    }

    internal PowerShellSemanticCompilationPipeline(
        PowerShellSemanticBinder binder,
        PowerShellBoundOptimizer optimizer,
        PowerShellSemanticAnalyzer analyzer,
        PowerShellTypedLowerer lowerer,
        PowerShellBoundCSharpBackend backend)
    {
        _binder = binder;
        _optimizer = optimizer;
        _analyzer = analyzer;
        _lowerer = lowerer;
        _backend = backend;
    }

    internal PowerShellSemanticCompilationResult Compile(
        IEnumerable<ParsedSourceDocument> documents,
        string? targetFramework = null,
        PowerShellCompilationCapability capabilities = PowerShellCompilationCapability.None)
    {
        var binding = _binder.BindWithRegionCandidates(documents, targetFramework, capabilities);
        var bound = binding.Program;
        var optimized = _optimizer.Optimize(bound);
        var analyzed = _analyzer.Analyze(optimized.Program);
        var lowered = _lowerer.Lower(analyzed, capabilities);
        var emitted = _backend.Emit(lowered);
        var promotedRegions = CompileRegions(binding.RegionCandidates, bound.Documents, capabilities);
        return new PowerShellSemanticCompilationResult(bound, optimized.Evidence, analyzed, lowered, emitted, promotedRegions);
    }

    private PowerShellPromotedRegionEmission[] CompileRegions(
        IReadOnlyList<PowerShellBoundRegionCandidate> candidates,
        IReadOnlyList<PowerShellBoundSourceDocument> documents,
        PowerShellCompilationCapability capabilities)
    {
        if (candidates.Count == 0) return Array.Empty<PowerShellPromotedRegionEmission>();
        var candidateProgram = new PowerShellBoundProgram(
            documents.ToArray(),
            candidates.Select(static candidate => candidate.RegionFunction).ToArray(),
            Array.Empty<PowerShellSemanticDiagnostic>());
        var optimized = _optimizer.Optimize(candidateProgram);
        var analyzed = _analyzer.Analyze(optimized.Program);
        var lowered = _lowerer.Lower(analyzed, capabilities);
        var emitted = _backend.Emit(lowered);
        var byKey = candidates.ToDictionary(static candidate => candidate.RegionFunction.Symbol.StableKey, StringComparer.Ordinal);
        var analyzedByKey = analyzed.Functions.ToDictionary(static function => function.Symbol.StableKey, StringComparer.Ordinal);
        var result = new List<PowerShellPromotedRegionEmission>();
        for (var index = 0; index < lowered.Functions.Length && index < emitted.Methods.Length; index++)
        {
            var loweredFunction = lowered.Functions[index];
            if (!byKey.TryGetValue(loweredFunction.Symbol.StableKey, out var candidate)) continue;
            if (!analyzedByKey.TryGetValue(loweredFunction.Symbol.StableKey, out var analyzedFunction)) continue;
            var method = emitted.Methods[index];
            if (PowerShellTypedRegionPromotionPolicy.IsSafe(candidate, loweredFunction, method))
                result.Add(new PowerShellPromotedRegionEmission(candidate, analyzedFunction, loweredFunction, method));
        }
        return result.OrderBy(static region => region.Candidate.RegionFunction.Symbol.StableKey, StringComparer.Ordinal).ToArray();
    }
}

internal sealed class PowerShellSemanticCompilationResult
{
    internal PowerShellSemanticCompilationResult(
        PowerShellBoundProgram bound,
        PowerShellBoundOptimizationEvidence optimization,
        PowerShellBoundProgram analyzed,
        PowerShellLoweredProgram lowered,
        PowerShellBoundCSharpResult emitted,
        PowerShellPromotedRegionEmission[] promotedRegions)
    {
        Bound = bound;
        Optimization = optimization;
        Analyzed = analyzed;
        Lowered = lowered;
        Emitted = emitted;
        PromotedRegions = promotedRegions ?? Array.Empty<PowerShellPromotedRegionEmission>();
    }

    internal PowerShellBoundProgram Bound { get; }
    internal PowerShellBoundOptimizationEvidence Optimization { get; }
    internal PowerShellBoundProgram Analyzed { get; }
    internal PowerShellLoweredProgram Lowered { get; }
    internal PowerShellBoundCSharpResult Emitted { get; }
    internal PowerShellImmutableArray<PowerShellPromotedRegionEmission> PromotedRegions { get; }
}
