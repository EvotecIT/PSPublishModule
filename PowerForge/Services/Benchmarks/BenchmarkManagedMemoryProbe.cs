namespace PowerForge;

/// <summary>Opt-in process-wide managed-heap observations after full garbage collection.</summary>
/// <remarks>
/// Construct outside the timed operation after warmup and after releasing previous results.
/// Release the current operation's results before calling <see cref="Capture"/>. Observations
/// include live host objects, caches and other managed threads; they exclude native memory
/// and are not peak-memory measurements. Full collection and finalizer waiting can be disruptive,
/// so use this only in explicit diagnostic or benchmark runs, not normal application paths.
/// </remarks>
public sealed class BenchmarkManagedMemoryProbe
{
    private readonly long _baselineBytes;

    /// <summary>Forces collection and captures the live managed-heap baseline.</summary>
    public BenchmarkManagedMemoryProbe()
    {
        _baselineBytes = ReadCollectedHeap();
    }

    /// <summary>Forces collection and observes the live heap relative to the original baseline.</summary>
    /// <remarks>May be called repeatedly. Negative deltas are retained, not clamped to zero.</remarks>
    public BenchmarkManagedMemoryObservation Capture()
        => new(_baselineBytes, ReadCollectedHeap());

    private static long ReadCollectedHeap()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return GC.GetTotalMemory(forceFullCollection: false);
    }
}

/// <summary>Two collected managed-heap observations in the same process.</summary>
public sealed class BenchmarkManagedMemoryObservation
{
    internal BenchmarkManagedMemoryObservation(long baselineBytes, long collectedBytes)
    {
        BaselineBytes = baselineBytes;
        CollectedBytes = collectedBytes;
    }

    /// <summary>Gets the live managed bytes observed when the probe was constructed.</summary>
    public long BaselineBytes { get; }

    /// <summary>Gets the live managed bytes after collection at the capture boundary.</summary>
    public long CollectedBytes { get; }

    /// <summary>Gets the signed change in live managed bytes since the probe's baseline.</summary>
    public long DeltaBytes => CollectedBytes - BaselineBytes;
}
