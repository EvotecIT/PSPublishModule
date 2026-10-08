using System.Runtime.CompilerServices;

namespace PowerForge.Tests;

[Collection(ProcessEnvironmentCollection.Name)]
public sealed class BenchmarkManagedMemoryProbeTests
{
    [Fact]
    public void Capture_collects_released_results_and_keeps_the_original_baseline()
    {
        var probe = new BenchmarkManagedMemoryProbe();
        var holder = new Holder();
        WeakReference reference = Allocate(holder);
        BenchmarkManagedMemoryObservation retained = probe.Capture();
        Assert.True(reference.IsAlive);
        Release(holder);
        BenchmarkManagedMemoryObservation released = probe.Capture();
        Assert.False(reference.IsAlive);
        Assert.Equal(retained.BaselineBytes, released.BaselineBytes);
        Assert.Equal(released.CollectedBytes - retained.BaselineBytes, released.DeltaBytes);
        GC.KeepAlive(holder);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Allocate(Holder holder)
    {
        holder.Result = new byte[1024 * 1024];
        return new WeakReference(holder.Result);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Release(Holder holder) => holder.Result = null;

    private sealed class Holder
    {
        internal byte[]? Result;
    }
}
