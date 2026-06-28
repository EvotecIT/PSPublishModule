namespace PowerForge;

/// <summary>
/// Evaluates whether compatibility module transport can be marked legacy.
/// </summary>
public static class ManagedModuleCompatibilityRetirementEvaluator
{
    private static readonly ManagedModuleBenchmarkOperation[] DefaultRequiredOperations =
    {
        ManagedModuleBenchmarkOperation.Install,
        ManagedModuleBenchmarkOperation.Save,
        ManagedModuleBenchmarkOperation.Update,
        ManagedModuleBenchmarkOperation.Publish
    };

    /// <summary>
    /// Evaluates retirement readiness from a benchmark result.
    /// </summary>
    /// <param name="result">Benchmark result.</param>
    /// <param name="requiredOperations">Optional required operations. Defaults to install, save, update, and publish.</param>
    /// <returns>Compatibility retirement readiness.</returns>
    public static ManagedModuleCompatibilityRetirementResult Evaluate(
        ManagedModuleBenchmarkResult result,
        IReadOnlyList<ManagedModuleBenchmarkOperation>? requiredOperations = null)
    {
        if (result is null)
            throw new ArgumentNullException(nameof(result));

        return Evaluate(result.TransitionGates, result.ProviderSupport, requiredOperations);
    }

    /// <summary>
    /// Evaluates retirement readiness from transition gates and provider-support evidence.
    /// </summary>
    /// <param name="transitionGates">Transition gates.</param>
    /// <param name="providerSupport">Provider-support evidence.</param>
    /// <param name="requiredOperations">Optional required operations. Defaults to install, save, update, and publish.</param>
    /// <returns>Compatibility retirement readiness.</returns>
    public static ManagedModuleCompatibilityRetirementResult Evaluate(
        IReadOnlyList<ManagedModuleBenchmarkTransitionGateResult>? transitionGates,
        IReadOnlyList<ManagedModuleProviderSupport>? providerSupport,
        IReadOnlyList<ManagedModuleBenchmarkOperation>? requiredOperations = null)
    {
        var required = (requiredOperations is { Count: > 0 }
                ? requiredOperations
                : DefaultRequiredOperations)
            .Distinct()
            .OrderBy(static operation => operation)
            .ToArray();
        var gates = transitionGates ?? Array.Empty<ManagedModuleBenchmarkTransitionGateResult>();
        var providers = providerSupport ?? Array.Empty<ManagedModuleProviderSupport>();
        var gatesByOperation = gates
            .GroupBy(static gate => gate.Operation)
            .ToDictionary(static group => group.Key, static group => group.First());

        var missing = required
            .Where(operation => !gatesByOperation.ContainsKey(operation))
            .ToArray();
        var ready = required
            .Where(operation => gatesByOperation.TryGetValue(operation, out var gate) && gate.Status == ManagedModuleBenchmarkTransitionGateStatus.Ready)
            .ToArray();
        var incomplete = required
            .Where(operation => gatesByOperation.TryGetValue(operation, out var gate) && gate.Status == ManagedModuleBenchmarkTransitionGateStatus.Incomplete)
            .ToArray();
        var blocked = required
            .Where(operation => gatesByOperation.TryGetValue(operation, out var gate) && gate.Status == ManagedModuleBenchmarkTransitionGateStatus.Blocked)
            .ToArray();
        var providerFallbacks = providers
            .Where(static support => support.CompatibilityFallbackRecommended)
            .ToArray();

        var reasons = BuildReasons(gatesByOperation, missing, incomplete, blocked, providerFallbacks);
        var status = ResolveStatus(missing, incomplete, blocked, providerFallbacks);
        return new ManagedModuleCompatibilityRetirementResult
        {
            Status = status,
            ReadyToMarkCompatibilityLegacy = status == ManagedModuleCompatibilityRetirementStatus.Ready,
            RequiredOperations = required,
            ReadyOperations = ready,
            MissingOperations = missing,
            IncompleteOperations = incomplete,
            BlockedOperations = blocked,
            ProviderFallbacks = providerFallbacks,
            Reasons = reasons.Count == 0
                ? new[] { "All required transition gates are ready and provider support does not require compatibility fallback." }
                : reasons
        };
    }

    private static ManagedModuleCompatibilityRetirementStatus ResolveStatus(
        IReadOnlyList<ManagedModuleBenchmarkOperation> missing,
        IReadOnlyList<ManagedModuleBenchmarkOperation> incomplete,
        IReadOnlyList<ManagedModuleBenchmarkOperation> blocked,
        IReadOnlyList<ManagedModuleProviderSupport> providerFallbacks)
    {
        if (blocked.Count > 0 ||
            providerFallbacks.Any(static support => support.Level == ManagedModuleProviderSupportLevel.Unsupported))
        {
            return ManagedModuleCompatibilityRetirementStatus.Blocked;
        }

        return missing.Count == 0 && incomplete.Count == 0 && providerFallbacks.Count == 0
            ? ManagedModuleCompatibilityRetirementStatus.Ready
            : ManagedModuleCompatibilityRetirementStatus.Incomplete;
    }

    private static List<string> BuildReasons(
        IReadOnlyDictionary<ManagedModuleBenchmarkOperation, ManagedModuleBenchmarkTransitionGateResult> gatesByOperation,
        IReadOnlyList<ManagedModuleBenchmarkOperation> missing,
        IReadOnlyList<ManagedModuleBenchmarkOperation> incomplete,
        IReadOnlyList<ManagedModuleBenchmarkOperation> blocked,
        IReadOnlyList<ManagedModuleProviderSupport> providerFallbacks)
    {
        var reasons = new List<string>();
        foreach (var operation in missing)
            reasons.Add("Missing transition gate for " + operation + ".");
        foreach (var operation in incomplete)
            reasons.Add(FormatGateReason(gatesByOperation[operation]));
        foreach (var operation in blocked)
            reasons.Add(FormatGateReason(gatesByOperation[operation]));
        foreach (var support in providerFallbacks)
            reasons.Add(FormatProviderReason(support));

        return reasons;
    }

    private static string FormatGateReason(ManagedModuleBenchmarkTransitionGateResult gate)
    {
        var details = gate.Reasons.Count == 0
            ? gate.CompatibilityFallbackReason
            : string.Join("; ", gate.Reasons);
        return gate.Operation + " transition gate is " + gate.Status + (string.IsNullOrWhiteSpace(details) ? "." : ": " + details);
    }

    private static string FormatProviderReason(ManagedModuleProviderSupport support)
    {
        var limitations = support.Limitations.Count == 0
            ? "provider support remains incomplete"
            : string.Join("; ", support.Limitations);
        return support.Provider + " compatibility fallback remains recommended because managed support is " + support.Level + ": " + limitations;
    }
}
