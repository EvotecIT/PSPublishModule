namespace PowerForge;

/// <summary>
/// Evaluates whether managed module engine evidence is ready for benchmarked lifecycle operations.
/// </summary>
public static class ManagedModuleManagedEvidenceEvaluator
{
    /// <summary>
    /// Evaluates managed evidence readiness from a benchmark result.
    /// </summary>
    /// <param name="result">Benchmark result.</param>
    /// <param name="requiredOperations">Optional required operations. Defaults to operations represented by transition gates.</param>
    /// <returns>Managed evidence readiness.</returns>
    public static ManagedModuleManagedEvidenceResult Evaluate(
        ManagedModuleBenchmarkResult result,
        IReadOnlyList<ManagedModuleBenchmarkOperation>? requiredOperations = null)
    {
        if (result is null)
            throw new ArgumentNullException(nameof(result));

        return Evaluate(result.TransitionGates, requiredOperations);
    }

    /// <summary>
    /// Evaluates managed evidence readiness from transition gates.
    /// </summary>
    /// <param name="transitionGates">Transition gates.</param>
    /// <param name="requiredOperations">Optional required operations. Defaults to operations represented by transition gates.</param>
    /// <returns>Managed evidence readiness.</returns>
    public static ManagedModuleManagedEvidenceResult Evaluate(
        IReadOnlyList<ManagedModuleBenchmarkTransitionGateResult>? transitionGates,
        IReadOnlyList<ManagedModuleBenchmarkOperation>? requiredOperations = null)
    {
        var gates = transitionGates ?? Array.Empty<ManagedModuleBenchmarkTransitionGateResult>();
        var gatesByOperation = gates
            .GroupBy(static gate => gate.Operation)
            .ToDictionary(static group => group.Key, static group => group.First());
        var required = ResolveRequiredOperations(gates, requiredOperations);
        var missing = required
            .Where(operation => !gatesByOperation.ContainsKey(operation))
            .ToArray();
        var ready = required
            .Where(operation => gatesByOperation.TryGetValue(operation, out var gate) && gate.ManagedEvidenceReady)
            .ToArray();
        var incomplete = required
            .Where(operation => gatesByOperation.TryGetValue(operation, out var gate) && !gate.ManagedEvidenceReady)
            .ToArray();
        var reasons = BuildReasons(gatesByOperation, required, missing, incomplete);
        var status = missing.Length == 0 && incomplete.Length == 0 && required.Length > 0
            ? ManagedModuleManagedEvidenceStatus.Ready
            : ManagedModuleManagedEvidenceStatus.Incomplete;

        return new ManagedModuleManagedEvidenceResult
        {
            Status = status,
            Ready = status == ManagedModuleManagedEvidenceStatus.Ready,
            RequiredOperations = required,
            ReadyOperations = ready,
            MissingOperations = missing,
            IncompleteOperations = incomplete,
            Reasons = reasons.Count == 0
                ? new[] { "Managed evidence is ready for all required operations." }
                : reasons
        };
    }

    private static ManagedModuleBenchmarkOperation[] ResolveRequiredOperations(
        IReadOnlyList<ManagedModuleBenchmarkTransitionGateResult> gates,
        IReadOnlyList<ManagedModuleBenchmarkOperation>? requiredOperations)
    {
        if (requiredOperations is { Count: > 0 })
        {
            return requiredOperations
                .Distinct()
                .OrderBy(static operation => operation)
                .ToArray();
        }

        return gates
            .Select(static gate => gate.Operation)
            .Distinct()
            .OrderBy(static operation => operation)
            .ToArray();
    }

    private static List<string> BuildReasons(
        IReadOnlyDictionary<ManagedModuleBenchmarkOperation, ManagedModuleBenchmarkTransitionGateResult> gatesByOperation,
        IReadOnlyList<ManagedModuleBenchmarkOperation> required,
        IReadOnlyList<ManagedModuleBenchmarkOperation> missing,
        IReadOnlyList<ManagedModuleBenchmarkOperation> incomplete)
    {
        var reasons = new List<string>();
        if (required.Count == 0)
            reasons.Add("No managed evidence gates were evaluated.");
        foreach (var operation in missing)
            reasons.Add("Missing managed evidence gate for " + operation + ".");
        foreach (var operation in incomplete)
            reasons.Add(FormatGateReason(gatesByOperation[operation]));

        return reasons;
    }

    private static string FormatGateReason(ManagedModuleBenchmarkTransitionGateResult gate)
    {
        var details = gate.Reasons.Count == 0
            ? gate.CompatibilityFallbackReason
            : string.Join("; ", gate.Reasons);
        return gate.Operation + " managed evidence is not ready" + (string.IsNullOrWhiteSpace(details) ? "." : ": " + details);
    }
}
