using System;
using System.Collections.Generic;
using System.Management.Automation;
using PowerForge.Generated.Runtime;

namespace Generic.Compiler.StatementErrors;

public static class NativeFunctionBindingFixture
{
    private static PowerShellNativeFunctionContext? _lastContext;
    public static readonly List<string> CleanupTrace = new();

    public static ScriptBlock Create(bool pipeline)
    {
        var metadata = "[CmdletBinding()] param([ValidatePattern('.*')][string]$First," +
            (pipeline ? "[Parameter(ValueFromPipeline)]" : "") + "[string]$Value)";
        return PowerShellNativeFunctionHost.Create(metadata, null, pipeline ? Body : null, pipeline ? null : Body);
    }

    private static void Body(PowerShellNativeFunctionContext context)
    {
        var first = context.GetVariable("First");
        var value = context.GetVariable("Value");
        context.WriteValue((first == null ? "null" : first.GetType().FullName) + "|" + first + "|" + value);
    }

    public static ScriptBlock CreateLifecycle()
        => PowerShellNativeFunctionHost.Create("[CmdletBinding()] param([Parameter(ValueFromPipeline)][int]$Value)",
            context => { context.SetVariable("Count", 0); context.WriteValue("begin"); },
            context => { context.SetVariable("Count", (int)context.GetVariable("Count")! + 1); context.WriteValue(context.GetVariable("Value")); },
            context => { _lastContext = context; context.WriteValue("end:" + context.GetVariable("Count")); });

    public static ScriptBlock CreateModuleOwned(PSModuleInfo module)
        => PowerShellNativeFunctionHost.Create(module,
            "[CmdletBinding()] param([ValidateScript({ Test-LocalValue $_ })][string]$Value = (Get-LocalValue))",
            null, null, context => context.WriteValue(context.GetVariable("script:Marker") + "|" + context.GetVariable("Value")));

    public static ScriptBlock CreateObservedBody()
        => PowerShellNativeFunctionHost.Create("[CmdletBinding()] param([object]$Value)", null, null, context => {
            context.SetVariable("Observed", "before");
            context.SetVariable("OFS", "::");
            context.WriteValue("value=" + context.Stringify(context.GetVariable("Value")));
            context.WriteValue("after=" + context.Stringify(context.GetVariable("Observed")));
        });

    public static ScriptBlock CreateParameterWrite()
        => PowerShellNativeFunctionHost.Create("[CmdletBinding()] param([ValidateRange(0,10)][int]$Value,[object]$Replacement)",
            null, null, context => {
                try { context.SetVariable("Value", context.GetVariable("Replacement")); }
                catch (Exception error) {
                    context.WriteValue("error:" + error.GetType().FullName + "|" + (error as IContainsErrorRecord)?.ErrorRecord.FullyQualifiedErrorId);
                }
                context.WriteValue(context.GetVariable("Value")!.GetType().FullName + "|" + context.Stringify(context.GetVariable("Value")));
            });

    public static ScriptBlock CreateCleanup()
        => PowerShellNativeFunctionHost.Create("[CmdletBinding()] param([Parameter(ValueFromPipeline)][int]$Value,[switch]$Fail)",
            context => CleanupTrace.Add("begin"),
            context => {
                CleanupTrace.Add("process:" + context.GetVariable("Value"));
                if (((SwitchParameter)context.GetVariable("Fail")!).IsPresent) throw new InvalidOperationException("process-failure");
                context.WriteValue(context.GetVariable("Value"));
            },
            context => CleanupTrace.Add("end"),
            context => { CleanupTrace.Add("clean"); context.WriteValue("discard-clean-output"); });

    public static void VerifyRetiredContext()
    {
        if (_lastContext == null) throw new InvalidOperationException("No lifecycle invocation was observed.");
        try { _lastContext.GetVariable("Count"); }
        catch (ObjectDisposedException) { return; }
        throw new InvalidOperationException("A retired context remained accessible.");
    }

    public static void VerifyMetadataBoundary()
    {
        foreach (var source in new[] {
            "param() 'authored body'", "param() begin { 'body' }", "param() process { 'body' }",
            "param() dynamicparam { 'body' }", "param() trap { 'body' }",
            "using namespace System\nparam()", "#requires -Version 5.1\nparam()", "param() clean { 'body' }" })
        {
            try { PowerShellNativeFunctionHost.Create(source, null, null, null); }
            catch (ArgumentException) { continue; }
            throw new InvalidOperationException("Authored code was accepted as parameter metadata: " + source);
        }
    }
}
