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
        var sourceDocuments = documents.ToArray();
        var binding = _binder.BindWithRegionCandidates(sourceDocuments, targetFramework, capabilities);
        var bound = binding.Program;
        capabilities = bound.TargetCapabilities;
        var optimized = _optimizer.Optimize(bound);
        var analyzed = _analyzer.Analyze(optimized.Program);
        // OutputType metadata is provisional. Rebind consumers through the ordinary binder
        // when canonical return analysis discovers a different value representation.
        var returnTypes = new Dictionary<string, PowerShellTypeFact>(StringComparer.Ordinal);
        var remainingRounds = Math.Max(1, bound.Functions.Length + 1);
        while (TryRefineCallReturnTypes(analyzed, returnTypes))
        {
            if (remainingRounds-- == 0)
                throw new InvalidOperationException("Local-call return contracts did not reach a stable representation.");
            binding = _binder.BindWithRegionCandidates(sourceDocuments, targetFramework, capabilities, returnTypes);
            bound = binding.Program;
            optimized = _optimizer.Optimize(bound);
            analyzed = _analyzer.Analyze(optimized.Program);
        }
        var lowered = _lowerer.Lower(analyzed, capabilities);
        var emitted = _backend.Emit(lowered);
        var regions = CompileRegions(binding.RegionCandidates, bound.Documents, capabilities, bound.SemanticHostFamily);
        var regionOpportunities = new PowerShellBoundRegionOpportunityAnalyzer(_optimizer, _analyzer, _lowerer).Analyze(
            binding.RegionOpportunities,
            bound,
            binding.RegionCandidates,
            capabilities);
        return new PowerShellSemanticCompilationResult(
            bound,
            optimized.Evidence,
            analyzed,
            lowered,
            emitted,
            regions.Promoted,
            regions.Decisions,
            regionOpportunities, binding.RuntimeFreeModule);
    }

    private static bool TryRefineCallReturnTypes(PowerShellBoundProgram program,
        IDictionary<string, PowerShellTypeFact> returnTypes)
    {
        var functions = program.Functions.ToDictionary(static function => function.Symbol.StableKey, StringComparer.Ordinal);
        var changed = false;
        foreach (var call in program.Functions.SelectMany(function => PowerShellSemanticAnalyzer.EnumerateStatements(function.Body))
            .SelectMany(PowerShellSemanticAnalyzer.EnumerateDirectExpressions)
            .SelectMany(PowerShellSemanticAnalyzer.EnumerateExpressions).OfType<PowerShellBoundInvocationExpression>())
        {
            if (!functions.TryGetValue(call.Target.StableKey, out var target) || target.NativeFunctionBinding is not null ||
                target.ReturnType.Provenance == PowerShellTypeFactProvenance.Unknown) continue;
            // The call's value contract is independent of the generated method's CLR return.
            var capturesCommandOutput = (target.ReturnType.ClrType == typeof(void) ||
                target.Capabilities.HasFlag(PowerShellRequiredCapability.CommandRegion)) &&
                program.TargetCapabilities.HasFlag(PowerShellCompilationCapability.PowerShellStatementErrors) &&
                program.TargetCapabilities.HasFlag(PowerShellCompilationCapability.PowerShellStreams);
            if (!capturesCommandOutput && target.ReturnType.ClrType == typeof(void) &&
                target.OutputCardinality != PowerShellOutputCardinality.None) continue;
            var actual = capturesCommandOutput
                ? new PowerShellTypeFact(typeof(object), PowerShellTypeFactProvenance.CapturedCommandOutput,
                    "Consuming a local command collapses its success records to empty, one record, or an array.")
                : target.ReturnType;
            if (actual.ClrType == call.Type.ClrType &&
                actual.Provenance == call.Type.Provenance) continue;
            if (returnTypes.TryGetValue(target.Symbol.StableKey, out var previous) &&
                previous.ClrType == actual.ClrType && previous.Provenance == actual.Provenance) continue;
            returnTypes[target.Symbol.StableKey] = actual;
            changed = true;
        }
        return changed;
    }

    private PowerShellRegionCompilationResult CompileRegions(
        IReadOnlyList<PowerShellBoundRegionCandidate> candidates,
        IReadOnlyList<PowerShellBoundSourceDocument> documents,
        PowerShellCompilationCapability capabilities,
        PowerShellCompilationSemanticHostFamily semanticHostFamily)
    {
        if (candidates.Count == 0) return PowerShellRegionCompilationResult.Empty;
        var candidateProgram = new PowerShellBoundProgram(
            documents.ToArray(),
            candidates.Select(static candidate => candidate.RegionFunction).ToArray(),
            Array.Empty<PowerShellSemanticDiagnostic>(), targetCapabilities: capabilities, semanticHostFamily: semanticHostFamily);
        var optimized = _optimizer.Optimize(candidateProgram);
        var analyzed = _analyzer.Analyze(optimized.Program);
        var lowered = _lowerer.Lower(analyzed, capabilities);
        var emitted = _backend.Emit(lowered);
        var byKey = candidates.ToDictionary(static candidate => candidate.RegionFunction.Symbol.StableKey, StringComparer.Ordinal);
        var analyzedByKey = analyzed.Functions.ToDictionary(static function => function.Symbol.StableKey, StringComparer.Ordinal);
        var decisions = new Dictionary<string, PowerShellRegionCandidateDecision>(StringComparer.Ordinal);
        var promoted = new List<PowerShellPromotedRegionEmission>();
        for (var index = 0; index < lowered.Functions.Length && index < emitted.Methods.Length; index++)
        {
            var loweredFunction = lowered.Functions[index];
            if (!byKey.TryGetValue(loweredFunction.Symbol.StableKey, out var candidate)) continue;
            if (!analyzedByKey.TryGetValue(loweredFunction.Symbol.StableKey, out var analyzedFunction)) continue;
            var method = emitted.Methods[index];
            var policy = PowerShellTypedRegionPromotionPolicy.Evaluate(candidate, loweredFunction, method);
            decisions[candidate.RegionId] = new PowerShellRegionCandidateDecision(candidate, policy, method);
            if (policy.IsSafe)
                promoted.Add(new PowerShellPromotedRegionEmission(candidate, analyzedFunction, loweredFunction, method));
        }
        foreach (var candidate in candidates.Where(candidate => !decisions.ContainsKey(candidate.RegionId)))
        {
            var analyzedFunction = analyzedByKey.TryGetValue(candidate.RegionFunction.Symbol.StableKey, out var value)
                ? value
                : null;
            var code = analyzedFunction?.Disposition.ReasonCode;
            var reason = analyzedFunction?.Disposition.Explanation;
            decisions[candidate.RegionId] = new PowerShellRegionCandidateDecision(
                candidate,
                new PowerShellTypedRegionPromotionDecision(
                    isSafe: false,
                    string.IsNullOrWhiteSpace(code) ? "region.not-emitted" : code!,
                    string.IsNullOrWhiteSpace(reason)
                        ? "The candidate did not survive canonical semantic analysis and lowering."
                        : reason!),
                emission: null);
        }
        return new PowerShellRegionCompilationResult(
            promoted.OrderBy(static region => region.Candidate.RegionFunction.Symbol.StableKey, StringComparer.Ordinal).ToArray(),
            decisions.Values.OrderBy(static decision => decision.Candidate.RegionFunction.Symbol.StableKey, StringComparer.Ordinal).ToArray());
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
        PowerShellPromotedRegionEmission[] promotedRegions,
        PowerShellRegionCandidateDecision[] regionCandidateDecisions,
        PowerShellCompilationRegionOpportunity[] regionOpportunities,
        PowerShellRuntimeFreeModuleDefinition? runtimeFreeModule = null)
    {
        Bound = bound;
        Optimization = optimization;
        Analyzed = analyzed;
        Lowered = lowered;
        Emitted = emitted;
        PromotedRegions = promotedRegions ?? Array.Empty<PowerShellPromotedRegionEmission>();
        RegionCandidateDecisions = regionCandidateDecisions ?? Array.Empty<PowerShellRegionCandidateDecision>();
        RegionOpportunities = regionOpportunities ?? Array.Empty<PowerShellCompilationRegionOpportunity>();
        RuntimeFreeModule = runtimeFreeModule;
    }

    internal PowerShellBoundProgram Bound { get; }
    internal PowerShellBoundOptimizationEvidence Optimization { get; }
    internal PowerShellBoundProgram Analyzed { get; }
    internal PowerShellLoweredProgram Lowered { get; }
    internal PowerShellBoundCSharpResult Emitted { get; }
    internal PowerShellImmutableArray<PowerShellPromotedRegionEmission> PromotedRegions { get; }
    internal PowerShellImmutableArray<PowerShellRegionCandidateDecision> RegionCandidateDecisions { get; }
    internal PowerShellImmutableArray<PowerShellCompilationRegionOpportunity> RegionOpportunities { get; }
    internal PowerShellRuntimeFreeModuleDefinition? RuntimeFreeModule { get; }
}

internal sealed class PowerShellRegionCompilationResult
{
    internal static PowerShellRegionCompilationResult Empty { get; } = new(
        Array.Empty<PowerShellPromotedRegionEmission>(),
        Array.Empty<PowerShellRegionCandidateDecision>());

    internal PowerShellRegionCompilationResult(
        PowerShellPromotedRegionEmission[] promoted,
        PowerShellRegionCandidateDecision[] decisions)
    {
        Promoted = promoted ?? Array.Empty<PowerShellPromotedRegionEmission>();
        Decisions = decisions ?? Array.Empty<PowerShellRegionCandidateDecision>();
    }

    internal PowerShellPromotedRegionEmission[] Promoted { get; }
    internal PowerShellRegionCandidateDecision[] Decisions { get; }
}

internal sealed class PowerShellRegionCandidateDecision
{
    internal PowerShellRegionCandidateDecision(
        PowerShellBoundRegionCandidate candidate,
        PowerShellTypedRegionPromotionDecision policy,
        PowerShellCSharpMethodEmission? emission)
    {
        Candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
        Policy = policy ?? throw new ArgumentNullException(nameof(policy));
        Emission = emission;
    }

    internal PowerShellBoundRegionCandidate Candidate { get; }
    internal PowerShellTypedRegionPromotionDecision Policy { get; }
    internal PowerShellCSharpMethodEmission? Emission { get; }
}
