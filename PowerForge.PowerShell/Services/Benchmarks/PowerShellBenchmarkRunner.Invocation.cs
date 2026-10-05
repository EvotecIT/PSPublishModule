using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Management.Automation;

namespace PowerForge;

public sealed partial class PowerShellBenchmarkRunner
{
    private static Collection<PSObject> InvokeStrict(ScriptBlock block, params object[] args)
        => InvokeGuarded(block, args, null);

    // Raw handlers are timed by the native wrapper. Captured DSL handlers start and
    // stop the same clock around their authored body, after captured-context setup.
    private static Collection<PSObject> InvokeOperation(ScriptBlock block, Stopwatch stopwatch, params object[] args)
        => InvokeGuarded(block, args, stopwatch);

    private static Collection<PSObject> InvokeGuarded(ScriptBlock block, object[] args, Stopwatch? stopwatch)
    {
        var variables = new List<PSVariable>
        {
            new("ErrorActionPreference", ActionPreference.Stop),
            new("PSNativeCommandUseErrorActionPreference", true)
        };
        return NativeExitAwareInvokeWrapper.InvokeWithContext(functionsToDefine: null, variablesToDefine: variables,
            new object[] { PrepareNativeExitGuardedBlock(block), args, false, typeof(PowerShellNativeExitCodeTracker),
                stopwatch!, stopwatch is not null && PowerShellBenchmarkCapturedBlocks.Contains(block) });
    }

    private static ScriptBlock PrepareNativeExitGuardedBlock(ScriptBlock block)
        => block.Module is null ? ScriptBlock.Create(PowerShellNativeExitCodeGuard.AddChecks(block.ToString())) : block;

    private static readonly ScriptBlock NativeExitAwareInvokeWrapper =
        ScriptBlock.Create(EmbeddedScripts.Load("Scripts/Benchmarks/Invoke-NativeExitAwareBlock.ps1"));
}
