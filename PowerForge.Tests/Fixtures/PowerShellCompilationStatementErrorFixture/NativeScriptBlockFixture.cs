using System.Collections.Generic;
using System.Management.Automation;
using PowerForge.Generated.Runtime;

namespace Generic.Compiler.StatementErrors;

public static class NativeScriptBlockFixture
{
    private const string BlockSource = "{ param([string]$Text) $Seen.Add($Marker+':'+$Text); $Marker='child'; $Marker }";
    public static int CompiledInvocations;
    public static ScriptBlock CreateIsolated(PSModuleInfo module)
    {
        const string prefix = "using namespace System.Collections.Generic\nfunction Other { begin { 'begin' } clean { 'unrelated' } }\n";
        const string literal = "{ param([List[string]]$Items) $Items.Count }";
        return PowerShellNativeFunctionHost.CreateCompiledScriptBlock(module, prefix + literal, "isolated.psm1",
            prefix.Length, prefix.Length + literal.Length, null, null,
            context => context.WriteValue("compiled-isolated:" + ((List<string>)context.GetVariable("Items")!).Count), null);
    }

    public static void VerifyCallbackCompleteness(PSModuleInfo module)
    {
        const string invalid = "{ $value = ; 'after' }";
        var rejected = false;
        try
        {
            PowerShellNativeFunctionHost.CreateCompiledScriptBlock(module, invalid, "invalid.ps1", 0, invalid.Length,
                null, null, _ => { }, null);
        }
        catch (System.ArgumentException) { rejected = true; }
        if (!rejected) throw new System.InvalidOperationException("A selected block with invalid syntax was admitted.");
        foreach (var source in new[] { "{ 'body' }", "{ begin { 'body' } }", "{ process { 'body' } }", "{ dynamicparam { } end { 'body' } }" })
        {
            try
            {
                PowerShellNativeFunctionHost.CreateCompiledScriptBlock(module, source, "callback-check.ps1", 0, source.Length,
                    null, null, null, null);
            }
            catch (System.ArgumentException) { continue; }
            throw new System.InvalidOperationException("An executable script-block clause was admitted without a compiled callback.");
        }
    }

    public static ScriptBlock Create(PSModuleInfo module)
        => PowerShellNativeFunctionHost.Create(module, "[CmdletBinding()] param([string]$Value)", "block-owner.psm1",
            new[] { "Seen", "Block", "Marker" }, null, null, context =>
            {
                context.SetVariable("Seen", new List<string>());
                context.SetVariable("Marker", "outer");
                context.SetVariable("Block", context.CreateScriptBlock(BlockSource, 0, BlockSource.Length,
                    null, null, child =>
                    {
                        CompiledInvocations++;
                        var seen = (List<string>)child.GetVariable("Seen")!;
                        seen.Add(child.GetVariable("Marker") + ":" + child.GetVariable("Text"));
                        child.SetVariable("Marker", "child");
                        child.WriteValue(child.GetVariable("Marker"));
                    }));
                var block = (ScriptBlock)context.GetVariable("Block")!;
                if (!block.ToString().Contains("$Seen.Add")) throw new System.InvalidOperationException("The original block metadata was lost.");
                context.InvokeCommandRegion("& $Block $Value; & $Block 'again'", "block-owner.psm1", 1, 1);
                context.WriteValue(context.GetVariable("Marker"));
                context.WriteValue(string.Join("|", (List<string>)context.GetVariable("Seen")!));
            });
}
