using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PowerForge;

/// <summary>Measures equivalent coarse typed and fine hosted-boundary workloads without owning either workload.</summary>
public sealed class PowerShellCompilationBoundaryProfiler
{
    /// <summary>Profiles equivalent operations and returns runtime evidence suitable for an artifact build specification.</summary>
    public PowerShellCompilationBoundaryRuntimeProfile Profile(
        string workload,
        int boundaryInvocationsPerIteration,
        Action baselineOperation,
        Action boundaryOperation,
        int warmupIterations = 1,
        int measuredIterations = 5)
    {
        if (string.IsNullOrWhiteSpace(workload)) throw new ArgumentException("A workload identity is required.", nameof(workload));
        if (boundaryInvocationsPerIteration <= 0) throw new ArgumentOutOfRangeException(nameof(boundaryInvocationsPerIteration));
        if (baselineOperation is null) throw new ArgumentNullException(nameof(baselineOperation));
        if (boundaryOperation is null) throw new ArgumentNullException(nameof(boundaryOperation));
        if (warmupIterations < 0) throw new ArgumentOutOfRangeException(nameof(warmupIterations));
        if (measuredIterations <= 0) throw new ArgumentOutOfRangeException(nameof(measuredIterations));

        for (var index = 0; index < warmupIterations; index++)
        {
            baselineOperation();
            boundaryOperation();
        }

        var baselineNanoseconds = Measure(baselineOperation, measuredIterations);
        var boundaryNanoseconds = Measure(boundaryOperation, measuredIterations);
        var invocations = checked((long)boundaryInvocationsPerIteration * measuredIterations);
        var overhead = Math.Max(0L, boundaryNanoseconds - baselineNanoseconds);
        var overheadPerBoundary = overhead / (double)invocations;
        var overheadRatio = boundaryNanoseconds == 0 ? 0d : overhead / (double)boundaryNanoseconds;
        return new PowerShellCompilationBoundaryRuntimeProfile
        {
            Workload = workload.Trim(),
            RuntimeIdentifier = PowerShellCompilationArtifactBuilder.GetDefaultRuntimeIdentifier(
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX),
                PowerShellCompilationArtifactBuilder.GetHostRuntimeIdentifier(),
                RuntimeInformation.ProcessArchitecture),
            BaselineDurationNanoseconds = baselineNanoseconds,
            BoundaryDurationNanoseconds = boundaryNanoseconds,
            BoundaryInvocations = invocations,
            EstimatedOverheadNanosecondsPerBoundary = overheadPerBoundary,
            EstimatedOverheadRatio = overheadRatio,
            Advisory = overheadRatio >= 0.25d
                ? "Measured typed/hosted boundary overhead is at least 25% of this workload; coarsen the boundary or keep the workload hosted."
                : string.Empty
        };
    }

    private static long Measure(Action operation, int iterations)
    {
        var started = Stopwatch.GetTimestamp();
        for (var index = 0; index < iterations; index++) operation();
        var elapsed = Stopwatch.GetTimestamp() - started;
        return checked((long)Math.Round(elapsed * 1_000_000_000d / Stopwatch.Frequency, MidpointRounding.AwayFromZero));
    }
}
