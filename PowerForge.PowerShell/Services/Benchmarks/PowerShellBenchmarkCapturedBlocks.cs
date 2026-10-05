using System.Management.Automation;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Threading;

namespace PowerForge;

// Captured DSL handlers own context setup and restoration around the authored body.
// Weak registration distinguishes that boundary from arbitrary module-bound handlers
// without changing their public argument list or retaining completed suites.
internal static class PowerShellBenchmarkCapturedBlocks
{
    private static readonly ConditionalWeakTable<ScriptBlock, object> Blocks = new();
    private static readonly AsyncLocal<Stopwatch?> CurrentTimer = new();

    // The captured delegate remains usable after its declaring runspace is disposed.
    // Each invocation restores its predecessor, including nested or failing runs.
    internal static Func<Stopwatch?> TimerAccessor { get; } = () => CurrentTimer.Value;

    internal static IDisposable EnterTiming(Stopwatch? timer)
    {
        var previous = CurrentTimer.Value;
        CurrentTimer.Value = timer;
        return new TimingScope(previous);
    }

    internal static void Register(ScriptBlock block) => Blocks.Add(block, new object());

    internal static bool Contains(ScriptBlock block) => Blocks.TryGetValue(block, out _);

    private sealed class TimingScope(Stopwatch? previous) : IDisposable
    {
        public void Dispose() => CurrentTimer.Value = previous;
    }
}
