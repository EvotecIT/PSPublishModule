using System.Management.Automation;
using PowerForge.Generated.Runtime;

namespace Generic.Compiler.StatementErrors;

public static class NativeDeclarationFixture
{
    public const string Snapshot = "'state=' + $(if ($null -eq $x) {'null'} else {$x.GetType().FullName + ':' + [string]$x}) + ';attrs=' + ((Get-Variable x).Attributes | ForEach-Object { $_.GetType().FullName }) + ';trace=' + $Trace";

    public static ScriptBlock Create(PSModuleInfo module, string[] targets, string[] operations, string[] values, string[] slotTypes)
        => PowerShellNativeFunctionHost.Create(module, "[CmdletBinding()] param()", "declarations.psm1",
            slotTypes.Length == 0 ? new[] { "Trace" } : new[] { "x", "Trace" }, null, null, context =>
            {
                context.SetVariable("x", 2);
                context.SetVariable("Trace", "");
                for (var index = 0; index < targets.Length; index++)
                {
                    var current = index;
                    try
                    {
                        var result = context.AssignVariableTarget(targets[index], operations[index],
                            () => context.CaptureCommandRegion(values[current], "declarations.psm1", 1, 1, false),
                            "declarations.psm1", 1, 1);
                        context.WriteValue("result=" + (result is null ? "null" : result.GetType().FullName + ":" + result));
                    }
                    catch (Exception error) when (error is IContainsErrorRecord)
                    {
                        context.WriteValue("error=" + ((IContainsErrorRecord)error).ErrorRecord.FullyQualifiedErrorId + ":" + error.Message);
                    }
                    context.InvokeCommandRegion(Snapshot, "declarations.psm1", 1, 1);
                }
            }, localTypeDeclarations: slotTypes);
}
