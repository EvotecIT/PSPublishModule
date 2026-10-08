using System.ComponentModel;
using System.Diagnostics;

namespace PowerForge;

public sealed partial class PowerShellBenchmarkRunner
{
    private readonly struct OperationMemorySnapshot(long? allocatedBytes, long? workingSetBytes)
    {
        internal long? AllocatedBytes { get; } = allocatedBytes;
        internal long? WorkingSetBytes { get; } = workingSetBytes;
    }

    private static OperationMemorySnapshot CaptureOperationMemory()
    {
        // Read process information before the allocation baseline so this observation
        // does not add its own allocations to the measured operation.
        long? workingSet = ReadWorkingSet();
        return new OperationMemorySnapshot(ReadAllocatedBytes(), workingSet);
    }

    private static void CaptureOperationMemoryDifference(OperationMemorySnapshot before,
        out long? allocatedBytes, out long? workingSetDeltaBytes)
    {
        // Snapshot allocation first: validation, metric callbacks and resident-memory
        // observation are outside the operation's allocation interval.
        long? afterAllocated = ReadAllocatedBytes();
        long? afterWorkingSet = ReadWorkingSet();
        allocatedBytes = before.AllocatedBytes.HasValue && afterAllocated.HasValue
            ? Math.Max(0, afterAllocated.Value - before.AllocatedBytes.Value) : null;
        workingSetDeltaBytes = before.WorkingSetBytes.HasValue && afterWorkingSet.HasValue
            ? afterWorkingSet.Value - before.WorkingSetBytes.Value : null;
    }

    private static long? ReadAllocatedBytes()
    {
#if NETFRAMEWORK
        // .NET Framework has no equivalent process-wide managed allocation counter.
        return null;
#else
        return GC.GetTotalAllocatedBytes(precise: true);
#endif
    }

    private static long? ReadWorkingSet()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return process.WorkingSet64;
        }
        catch (Exception exception) when (exception is Win32Exception
            or PlatformNotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }
}
