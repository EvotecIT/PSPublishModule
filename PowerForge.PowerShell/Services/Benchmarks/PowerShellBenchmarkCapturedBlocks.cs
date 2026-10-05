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
    private static readonly ConditionalWeakTable<ScriptBlock, TimingIdentity> Blocks = new();
    private static readonly AsyncLocal<TimingInvocation?> CurrentInvocation = new();

    // The captured delegate remains usable after its declaring runspace is disposed.
    // Each invocation restores its predecessor, including nested or failing runs.
    internal static TimingIdentity CreateIdentity() => new();

    internal static IDisposable EnterTiming(ScriptBlock block, Stopwatch? timer)
    {
        var previous = CurrentInvocation.Value;
        CurrentInvocation.Value = timer is not null && Blocks.TryGetValue(block, out var identity)
            ? new TimingInvocation(identity, timer) : null;
        return new TimingScope(previous);
    }

    internal static void Register(ScriptBlock block, TimingIdentity identity) => Blocks.Add(block, identity);

    internal static bool Contains(ScriptBlock block) => Blocks.TryGetValue(block, out _);

    internal sealed class TimingIdentity
    {
        internal Func<Stopwatch?> TimerAccessor => ClaimTimer;

        // A direct nested handler must not stop its caller's clock. A one-time claim
        // also excludes recursion of the same handler from owning the outer timer.
        private Stopwatch? ClaimTimer()
        {
            var invocation = CurrentInvocation.Value;
            return invocation is not null && ReferenceEquals(invocation.Identity, this)
                && Interlocked.Exchange(ref invocation.Claimed, 1) == 0 ? invocation.Timer : null;
        }
    }

    private sealed class TimingInvocation(TimingIdentity identity, Stopwatch timer)
    {
        internal TimingIdentity Identity { get; } = identity;
        internal Stopwatch Timer { get; } = timer;
        internal int Claimed;
    }

    private sealed class TimingScope(TimingInvocation? previous) : IDisposable
    {
        public void Dispose() => CurrentInvocation.Value = previous;
    }
}
