using System.Management.Automation;
using System.Management.Automation.Language;
using PowerForge.Generated.Runtime;

namespace Generic.Compiler.StatementErrors;

/// <summary>Qualifies an unchanged declaration AST with fresh compiled callback storage.</summary>
public static class NativeAuthoredMetadataFixture
{
    public static int CallbackCount { get; private set; }

    public static void InstallEndOnly(PSModuleInfo module, string name)
    {
        var script = PowerShellNativeFunctionHost.CreateDeclaredFunction(module, name, null, null,
            context => context.WriteValue("compiled"));
        PowerShellNativeFunctionHost.InstallDeclaredFunction(module, name, script);
    }

    public static ScriptBlock CreateEndOnlyBlock(PSModuleInfo module, string literal)
        => PowerShellNativeFunctionHost.CreateCompiledScriptBlock(module, literal, "type-body.ps1", 0, literal.Length,
            null, null, context => context.WriteValue("block"), null);

    public static void Install(PSModuleInfo module, string name)
    {
        var contract = PowerShellNativeFunctionHost.NativeContract.Shared;
        var session = contract.NativeSessionState.GetValue(module.SessionState, null)!;
        var function = (FunctionInfo)PowerShellNativeFunctionHost.Invoke(contract.GetFunction, session, new object[] { name })!;
        if (function.ScriptBlock.Ast is not FunctionDefinitionAst declaration)
            throw new InvalidOperationException("A declared function AST is required.");
        var script = PowerShellNativeFunctionHost.CreateDeclaredFunction(module, name,
            context => context.SetVariable("Count", 0),
            context =>
            {
                CallbackCount++;
                var count = (int)context.GetVariable("Count")! + 1;
                context.SetVariable("Count", count);
                context.WriteValue(context.GetVariable("Value") + "|" + context.GetVariable("Mode") + "|" +
                    context.GetVariable("script:Marker") + "|" + count);
            }, context => context.WriteValue("end:" + context.GetVariable("Count")));
        if (ReferenceEquals(contract.Data.GetValue(script), contract.Data.GetValue(function.ScriptBlock)))
            throw new InvalidOperationException("The bound replacement shares mutable compilation data.");
        PowerShellNativeFunctionHost.InstallDeclaredFunction(module, name, script);
    }

    public static void VerifyRejectedClauses(PSModuleInfo module)
    {
        Action<PowerShellNativeFunctionContext> fail = _ => throw new InvalidOperationException("Unsupported authored clause executed.");
        Reject("Invoke-Compiled", null, fail, fail);
        Reject("Invoke-Compiled", fail, null, fail);
        Reject("Invoke-Compiled", fail, fail, null);
        Reject("Invoke-Dynamic", null, null, fail);
        Reject("Invoke-Trap", null, null, fail);
        try
        {
            PowerShellNativeFunctionHost.CreateOwnedDeclaration(module, "Invoke-Compiled", "param()",
                Array.Empty<string>(), Array.Empty<string>(), fail, fail, fail);
            throw new InvalidOperationException("The stub owner accepted an authored body.");
        }
        catch (ArgumentException) { }
        // The explicit hosted-discovery field cannot carry another executable clause,
        // and legacy parameter-only callers still cannot smuggle dynamic discovery.
        RejectMetadata("param() dynamicparam { $null }", null, false);
        RejectMetadata("param()", "dynamicparam { $null } end { 'authored body' }", false);
        RejectMetadata("param()", "dynamicparam { $null } begin { 'authored body' }", false);
        RejectMetadata("param()", "dynamicparam { $null }", true);
        void RejectMetadata(string parameters, string? dynamic, bool clean)
        {
            try
            {
                PowerShellNativeFunctionHost.CreateDeclarationSource(parameters, null, Array.Empty<string>(),
                    false, false, true, clean, Array.Empty<string>(), Array.Empty<string>(), dynamic);
            }
            catch (ArgumentException) { return; }
            throw new InvalidOperationException("The declaration owner accepted uncovered executable metadata.");
        }
        void Reject(string name, Action<PowerShellNativeFunctionContext>? begin,
            Action<PowerShellNativeFunctionContext>? process, Action<PowerShellNativeFunctionContext>? end)
        {
            try { PowerShellNativeFunctionHost.CreateDeclaredFunction(module, name, begin, process, end); }
            catch (ArgumentException) { return; }
            throw new InvalidOperationException("An uncovered declaration clause was accepted: " + name);
        }
    }
}
