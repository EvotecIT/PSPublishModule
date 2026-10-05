using System.Management.Automation;
using System.Runtime.CompilerServices;

namespace PowerForge;

// Captured DSL handlers own context setup and restoration around the authored body.
// Weak registration distinguishes that boundary from arbitrary module-bound handlers
// without changing their public argument list or retaining completed suites.
internal static class PowerShellBenchmarkCapturedBlocks
{
    private static readonly ConditionalWeakTable<ScriptBlock, object> Blocks = new();

    internal static void Register(ScriptBlock block) => Blocks.Add(block, new object());

    internal static bool Contains(ScriptBlock block) => Blocks.TryGetValue(block, out _);
}
