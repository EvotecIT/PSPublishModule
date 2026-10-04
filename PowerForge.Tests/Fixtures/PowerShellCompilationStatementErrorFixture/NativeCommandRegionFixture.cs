using System.Management.Automation;
using PowerForge.Generated.Runtime;

namespace Generic.Compiler.StatementErrors;

public static class NativeCommandRegionFixture
{
    public static readonly System.Collections.Generic.List<string> Trace = new();
    public static ScriptBlock Create(PSModuleInfo module, string source)
        => PowerShellNativeFunctionHost.Create(module,
            "[CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Value)",
            "native-region.psm1", new[] { "Seen", "Count", "OFS" }, null, null, context =>
            {
                context.SetVariable("Seen", "before");
                context.SetVariable("Count", 0);
                context.SetVariable("OFS", ":");
                context.InvokeCommandRegion(source, "native-region.psm1", 5, 1);
                context.WriteValue("after=" + context.GetVariable("Seen") + ";count=" + context.GetVariable("Count"));
            });
}
