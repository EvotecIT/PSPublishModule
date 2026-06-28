using System.Globalization;

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
    /// <param name="maximumManagedSlowdownRatio">Maximum allowed managed median slowdown ratio. Values less than or equal to zero disable performance gating.</param>
    /// <param name="maximumManagedSlowdownMilliseconds">Absolute managed median slowdown tolerance, in milliseconds.</param>
    /// <returns>Transition gate results for benchmarked install/save/update/publish operations.</returns>
    public static IReadOnlyList<ManagedModuleBenchmarkTransitionGateResult> Evaluate(
        IReadOnlyList<ManagedModuleBenchmarkRunResult>? runs,
        double maximumManagedSlowdownRatio = 0,
        int maximumManagedSlowdownMilliseconds = 0)
    {
        var benchmarkRuns = runs ?? Array.Empty<ManagedModuleBenchmarkRunResult>();
        return benchmarkRuns
            .Where(static run => IsTransitionOperation(run.Operation))
            .GroupBy(static run => run.Operation)
            .OrderBy(static group => group.Key)
            .Select(group => EvaluateOperation(
                group.Key,
                group.ToArray(),
                maximumManagedSlowdownRatio,
                Math.Max(0, maximumManagedSlowdownMilliseconds)))
            .ToArray();
    }

    private static ManagedModuleBenchmarkTransitionGateResult EvaluateOperation(
        ManagedModuleBenchmarkOperation operation,
        IReadOnlyList<ManagedModuleBenchmarkRunResult> runs,
        double maximumManagedSlowdownRatio,
        int maximumManagedSlowdownMilliseconds)
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
        var performance = EvaluatePerformance(
            managedRuns,
            compatibilityRuns,
            maximumManagedSlowdownRatio,
            maximumManagedSlowdownMilliseconds);
        var providerLimitations = BuildCompatibilityProviderLimitations(compatibilityRuns);
        var reasons = BuildReasons(operation, managedRuns, compatibilityRuns, coveredCompatibilityEngines, performance, providerLimitations);
        var status = ResolveStatus(operation, compatibilityRuns, reasons);
        var nativeIsolationRequired = RequiresNativeIsolation(operation, compatibilityRuns);
        var fallbackReason = ResolveCompatibilityFallbackReason(status, nativeIsolationRequired, reasons, providerLimitations);

        return new ManagedModuleBenchmarkTransitionGateResult
        {
            Operation = operation,
            Status = status,
            ReadyForDefaultManagedTransport = status == ManagedModuleBenchmarkTransitionGateStatus.Ready,
            CompatibilityFallbackRequired = status != ManagedModuleBenchmarkTransitionGateStatus.Ready,
            CompatibilityFallbackReason = fallbackReason,
            CompatibilityProviderLimitations = providerLimitations,
            NativeIsolationRequired = nativeIsolationRequired,
            ManagedRunCount = managedRuns.Length,
            SuccessfulManagedRunCount = managedRuns.Count(static run => run.Succeeded),
            CompatibilityRunCount = compatibilityRuns.Length,
            SuccessfulCompatibilityRunCount = compatibilityRuns.Count(static run => run.Succeeded),
            RequiredCompatibilityEngines = RequiredCompatibilityEngines,
            CoveredCompatibilityEngines = coveredCompatibilityEngines,
            ManagedMedianMilliseconds = ToMilliseconds(performance?.ManagedMedian),
            CompatibilityMedianMilliseconds = ToMilliseconds(performance?.CompatibilityMedian),
            AllowedManagedMilliseconds = ToMilliseconds(performance?.AllowedManagedMedian),
            PerformanceWithinPolicy = performance?.WithinPolicy,
            MaximumManagedSlowdownRatio = performance?.MaximumManagedSlowdownRatio,
            MaximumManagedSlowdownMilliseconds = performance?.MaximumManagedSlowdownMilliseconds,
            Reasons = reasons.Count == 0
                ? new[] { "Managed and compatibility benchmark evidence passed for this operation." }
                : reasons
        };
    }

    private static List<string> BuildReasons(
        ManagedModuleBenchmarkOperation operation,
        IReadOnlyList<ManagedModuleBenchmarkRunResult> managedRuns,
        IReadOnlyList<ManagedModuleBenchmarkRunResult> compatibilityRuns,
        IReadOnlyCollection<string> coveredCompatibilityEngines,
        PerformanceEvidence? performance,
        IReadOnlyList<string> providerLimitations)
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
        if (performance is { WithinPolicy: false } && !string.IsNullOrWhiteSpace(performance.Reason))
            reasons.Add(performance.Reason!);

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
        {
            reasons.Add("One or more compatibility benchmark runs failed.");
            reasons.AddRange(providerLimitations);
        }

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
        IReadOnlyList<string> reasons,
        IReadOnlyList<string> providerLimitations)
    {
        if (status == ManagedModuleBenchmarkTransitionGateStatus.Ready)
            return null;
        if (nativeIsolationRequired)
            return "Native install/update comparison requires an isolated disposable host before compatibility fallback can be retired.";
        if (providerLimitations.Count > 0)
            return providerLimitations[0];

        return reasons.FirstOrDefault();
    }

    private static IReadOnlyList<string> BuildCompatibilityProviderLimitations(
        IReadOnlyList<ManagedModuleBenchmarkRunResult> compatibilityRuns)
        => compatibilityRuns
            .Where(static run => !run.Succeeded && !IsDefaultIsolationBlock(run))
            .GroupBy(static run => string.IsNullOrWhiteSpace(run.Engine) ? "Unknown" : run.Engine!)
            .Select(static group => FormatCompatibilityFailureReason(group.Key, group.First()))
            .ToArray();

    private static bool IsManagedEngine(string? engine)
        => IsEngine(engine, ManagedModuleBenchmarkEngine.Managed.ToString());

    private static bool IsEngine(string? value, string engine)
        => string.Equals(value, engine, StringComparison.OrdinalIgnoreCase);

    private static bool HasFailedImportValidation(ManagedModuleBenchmarkRunResult run)
        => (run.ImportValidations ?? Array.Empty<ManagedModuleImportValidationResult>())
            .Any(static validation => !validation.Succeeded);

    private static PerformanceEvidence? EvaluatePerformance(
        IReadOnlyList<ManagedModuleBenchmarkRunResult> managedRuns,
        IReadOnlyList<ManagedModuleBenchmarkRunResult> compatibilityRuns,
        double maximumManagedSlowdownRatio,
        int maximumManagedSlowdownMilliseconds)
    {
        if (maximumManagedSlowdownRatio <= 0)
            return null;

        var managedMedian = MedianElapsed(managedRuns.Where(static run => run.Succeeded));
        var compatibilityMedian = compatibilityRuns
            .Where(static run => run.Succeeded)
            .GroupBy(static run => string.IsNullOrWhiteSpace(run.Engine) ? "Unknown" : run.Engine!)
            .Select(static group => MedianElapsed(group))
            .Where(static median => median.HasValue)
            .OrderBy(static median => median!.Value)
            .FirstOrDefault();
        if (!managedMedian.HasValue || !compatibilityMedian.HasValue)
            return null;

        var tolerance = TimeSpan.FromMilliseconds(Math.Max(0, maximumManagedSlowdownMilliseconds));
        var ratioLimit = TimeSpan.FromTicks((long)(compatibilityMedian.Value.Ticks * maximumManagedSlowdownRatio));
        var absoluteLimit = compatibilityMedian.Value + tolerance;
        var allowed = ratioLimit > absoluteLimit ? ratioLimit : absoluteLimit;
        var withinPolicy = managedMedian.Value <= allowed;
        return new PerformanceEvidence
        {
            ManagedMedian = managedMedian.Value,
            CompatibilityMedian = compatibilityMedian.Value,
            AllowedManagedMedian = allowed,
            WithinPolicy = withinPolicy,
            MaximumManagedSlowdownRatio = maximumManagedSlowdownRatio,
            MaximumManagedSlowdownMilliseconds = maximumManagedSlowdownMilliseconds,
            Reason = withinPolicy
                ? null
                : "Managed median elapsed " + FormatMilliseconds(managedMedian.Value) +
                  " exceeded allowed " + FormatMilliseconds(allowed) +
                  " for fastest compatibility median " + FormatMilliseconds(compatibilityMedian.Value) +
                  " with ratio " + maximumManagedSlowdownRatio.ToString("0.##", CultureInfo.InvariantCulture) +
                  " and tolerance " + maximumManagedSlowdownMilliseconds.ToString(CultureInfo.InvariantCulture) + " ms."
        };
    }

    private static TimeSpan? MedianElapsed(IEnumerable<ManagedModuleBenchmarkRunResult> runs)
    {
        var values = runs
            .Select(static run => run.Elapsed)
            .Where(static elapsed => elapsed > TimeSpan.Zero)
            .OrderBy(static elapsed => elapsed)
            .ToArray();
        if (values.Length == 0)
            return null;

        return values.Length % 2 == 1
            ? values[values.Length / 2]
            : TimeSpan.FromTicks((values[(values.Length / 2) - 1].Ticks + values[values.Length / 2].Ticks) / 2);
    }

    private static double? ToMilliseconds(TimeSpan? value)
        => value?.TotalMilliseconds;

    private static string FormatMilliseconds(TimeSpan value)
        => value.TotalMilliseconds.ToString("0.##", CultureInfo.InvariantCulture) + " ms";

    private static string FormatCompatibilityFailureReason(string engine, ManagedModuleBenchmarkRunResult run)
    {
        var message = NormalizeFailureMessage(run.ErrorMessage);
        return string.IsNullOrWhiteSpace(message)
            ? "Compatibility baseline failed for " + engine + "."
            : "Compatibility baseline failed for " + engine + ": " + message;
    }

    private static string? NormalizeFailureMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        var safeMessage = message!;
        var normalized = string.Join(
            " ",
            safeMessage.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(static part => part.Trim())
                .Where(static part => part.Length > 0));
        const int maxLength = 220;
        return normalized.Length <= maxLength
            ? normalized
            : normalized.Substring(0, maxLength) + "...";
    }

    private static bool IsDefaultIsolationBlock(ManagedModuleBenchmarkRunResult run)
        => !run.Succeeded &&
           !string.IsNullOrWhiteSpace(run.ErrorMessage) &&
           run.ErrorMessage!.IndexOf("custom module-root isolation", StringComparison.OrdinalIgnoreCase) >= 0;

    private sealed class PerformanceEvidence
    {
        internal TimeSpan ManagedMedian { get; set; }

        internal TimeSpan CompatibilityMedian { get; set; }

        internal TimeSpan AllowedManagedMedian { get; set; }

        internal bool WithinPolicy { get; set; }

        internal double MaximumManagedSlowdownRatio { get; set; }

        internal int MaximumManagedSlowdownMilliseconds { get; set; }

        internal string? Reason { get; set; }
    }
}
