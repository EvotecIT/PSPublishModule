namespace PowerForge;

/// <summary>
/// Evaluates benchmark evidence before managed module transport replaces compatibility defaults.
/// </summary>
public static class ManagedModuleBenchmarkTransitionGateEvaluator
{
    private static readonly string[] RequiredCompatibilityEngines =
    {
        ManagedModuleBenchmarkEngine.PSResourceGet.ToString(),
        ManagedModuleBenchmarkEngine.PowerShellGet.ToString()
    };

    /// <summary>
    /// Evaluates operation-level transition gates from benchmark runs.
    /// </summary>
    /// <param name="runs">Benchmark runs.</param>
    /// <returns>Transition gate results for benchmarked install/save/update/publish operations.</returns>
    public static IReadOnlyList<ManagedModuleBenchmarkTransitionGateResult> Evaluate(
        IReadOnlyList<ManagedModuleBenchmarkRunResult>? runs)
    {
        var benchmarkRuns = runs ?? Array.Empty<ManagedModuleBenchmarkRunResult>();
        return benchmarkRuns
            .Where(static run => IsTransitionOperation(run.Operation))
            .GroupBy(static run => run.Operation)
            .OrderBy(static group => group.Key)
            .Select(static group => EvaluateOperation(group.Key, group.ToArray()))
            .ToArray();
    }

    private static ManagedModuleBenchmarkTransitionGateResult EvaluateOperation(
        ManagedModuleBenchmarkOperation operation,
        IReadOnlyList<ManagedModuleBenchmarkRunResult> runs)
    {
        var managedRuns = runs
            .Where(static run => IsManagedEngine(run.Engine))
            .ToArray();
        var compatibilityRuns = runs
            .Where(static run => !IsManagedEngine(run.Engine))
            .ToArray();
        var coveredCompatibilityEngines = RequiredCompatibilityEngines
            .Where(engine => compatibilityRuns.Any(run => IsEngine(run.Engine, engine) && run.Succeeded))
            .ToArray();
        var reasons = BuildReasons(operation, managedRuns, compatibilityRuns, coveredCompatibilityEngines);
        var status = ResolveStatus(operation, compatibilityRuns, reasons);
        var nativeIsolationRequired = RequiresNativeIsolation(operation, compatibilityRuns);
        var fallbackReason = ResolveCompatibilityFallbackReason(status, nativeIsolationRequired, reasons);

        return new ManagedModuleBenchmarkTransitionGateResult
        {
            Operation = operation,
            Status = status,
            ReadyForDefaultManagedTransport = status == ManagedModuleBenchmarkTransitionGateStatus.Ready,
            CompatibilityFallbackRequired = status != ManagedModuleBenchmarkTransitionGateStatus.Ready,
            CompatibilityFallbackReason = fallbackReason,
            NativeIsolationRequired = nativeIsolationRequired,
            ManagedRunCount = managedRuns.Length,
            SuccessfulManagedRunCount = managedRuns.Count(static run => run.Succeeded),
            CompatibilityRunCount = compatibilityRuns.Length,
            SuccessfulCompatibilityRunCount = compatibilityRuns.Count(static run => run.Succeeded),
            RequiredCompatibilityEngines = RequiredCompatibilityEngines,
            CoveredCompatibilityEngines = coveredCompatibilityEngines,
            Reasons = reasons.Count == 0
                ? new[] { "Managed and compatibility benchmark evidence passed for this operation." }
                : reasons
        };
    }

    private static List<string> BuildReasons(
        ManagedModuleBenchmarkOperation operation,
        IReadOnlyList<ManagedModuleBenchmarkRunResult> managedRuns,
        IReadOnlyList<ManagedModuleBenchmarkRunResult> compatibilityRuns,
        IReadOnlyCollection<string> coveredCompatibilityEngines)
    {
        var reasons = new List<string>();
        if (managedRuns.Count == 0)
            reasons.Add("No managed benchmark runs were recorded.");
        else if (managedRuns.Any(static run => !run.Succeeded))
            reasons.Add("One or more managed benchmark runs failed.");

        if (managedRuns.Any(static run => run.VersionValidationSucceeded == false))
            reasons.Add("One or more managed runs failed installed-version validation.");
        if (managedRuns.Any(HasFailedImportValidation))
            reasons.Add("One or more managed runs failed import validation.");

        var missingCompatibility = RequiredCompatibilityEngines
            .Where(engine => !coveredCompatibilityEngines.Contains(engine, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (missingCompatibility.Length > 0)
            reasons.Add("Missing successful compatibility baseline for " + string.Join(", ", missingCompatibility) + ".");

        var failedCompatibility = compatibilityRuns
            .Where(static run => !run.Succeeded)
            .ToArray();
        if (failedCompatibility.Any(IsDefaultIsolationBlock))
            reasons.Add("Compatibility install/update baseline requires an explicit disposable-host runner for native module cmdlets.");
        else if (failedCompatibility.Length > 0)
            reasons.Add("One or more compatibility benchmark runs failed.");

        if (operation is ManagedModuleBenchmarkOperation.Install or ManagedModuleBenchmarkOperation.Update &&
            compatibilityRuns.Count == 0)
        {
            reasons.Add("Install/update default transition requires native comparison evidence from an explicit compatibility runner.");
        }

        return reasons;
    }

    private static ManagedModuleBenchmarkTransitionGateStatus ResolveStatus(
        ManagedModuleBenchmarkOperation operation,
        IReadOnlyList<ManagedModuleBenchmarkRunResult> compatibilityRuns,
        IReadOnlyCollection<string> reasons)
    {
        if (reasons.Count == 0)
            return ManagedModuleBenchmarkTransitionGateStatus.Ready;

        if (operation is ManagedModuleBenchmarkOperation.Install or ManagedModuleBenchmarkOperation.Update &&
            compatibilityRuns.Any(IsDefaultIsolationBlock))
        {
            return ManagedModuleBenchmarkTransitionGateStatus.Blocked;
        }

        return ManagedModuleBenchmarkTransitionGateStatus.Incomplete;
    }

    private static bool IsTransitionOperation(ManagedModuleBenchmarkOperation operation)
        => operation is ManagedModuleBenchmarkOperation.Install
            or ManagedModuleBenchmarkOperation.Save
            or ManagedModuleBenchmarkOperation.Update
            or ManagedModuleBenchmarkOperation.Publish;

    private static bool RequiresNativeIsolation(
        ManagedModuleBenchmarkOperation operation,
        IReadOnlyList<ManagedModuleBenchmarkRunResult> compatibilityRuns)
        => operation is ManagedModuleBenchmarkOperation.Install or ManagedModuleBenchmarkOperation.Update &&
           (compatibilityRuns.Count == 0 || compatibilityRuns.Any(IsDefaultIsolationBlock));

    private static string? ResolveCompatibilityFallbackReason(
        ManagedModuleBenchmarkTransitionGateStatus status,
        bool nativeIsolationRequired,
        IReadOnlyList<string> reasons)
    {
        if (status == ManagedModuleBenchmarkTransitionGateStatus.Ready)
            return null;
        if (nativeIsolationRequired)
            return "Native install/update comparison requires an isolated disposable host before compatibility fallback can be retired.";

        return reasons.FirstOrDefault();
    }

    private static bool IsManagedEngine(string? engine)
        => IsEngine(engine, ManagedModuleBenchmarkEngine.Managed.ToString());

    private static bool IsEngine(string? value, string engine)
        => string.Equals(value, engine, StringComparison.OrdinalIgnoreCase);

    private static bool HasFailedImportValidation(ManagedModuleBenchmarkRunResult run)
        => (run.ImportValidations ?? Array.Empty<ManagedModuleImportValidationResult>())
            .Any(static validation => !validation.Succeeded);

    private static bool IsDefaultIsolationBlock(ManagedModuleBenchmarkRunResult run)
        => !run.Succeeded &&
           !string.IsNullOrWhiteSpace(run.ErrorMessage) &&
           run.ErrorMessage!.IndexOf("custom module-root isolation", StringComparison.OrdinalIgnoreCase) >= 0;
}
