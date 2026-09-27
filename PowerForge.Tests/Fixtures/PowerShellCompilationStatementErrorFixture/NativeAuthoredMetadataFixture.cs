using System.Management.Automation;
using System.Management.Automation.Language;
using PowerForge.Generated.Runtime;

namespace Generic.Compiler.StatementErrors;

/// <summary>Qualifies an unchanged declaration AST with fresh compiled callback storage.</summary>
public static class NativeAuthoredMetadataFixture
{
    public static int CallbackCount { get; private set; }

    public static void Install(PSModuleInfo module, string name)
    {
        var contract = PowerShellNativeFunctionHost.NativeContract.Shared;
        var session = contract.NativeSessionState.GetValue(module.SessionState, null)!;
        var function = (FunctionInfo)PowerShellNativeFunctionHost.Invoke(contract.GetFunction, session, new object[] { name })!;
        if (function.ScriptBlock.Ast is not FunctionDefinitionAst declaration)
            throw new InvalidOperationException("A declared function AST is required.");
        // Preserve the original, already declared AST. No AST reconstruction, extent substitution,
        // shared ScriptBlock clone, or transfer of private parameter metadata is involved.
        var script = PowerShellNativeFunctionHost.CreateFunctionScriptBlock(declaration);
        if (ReferenceEquals(contract.Data.GetValue(script), contract.Data.GetValue(function.ScriptBlock)))
            throw new InvalidOperationException("The replacement shares mutable compilation data.");
        script = module.NewBoundScriptBlock(script);
        PowerShellNativeFunctionHost.InstallCompiledClauses(script,
            context => context.SetVariable("Count", 0),
            context =>
            {
                CallbackCount++;
                var count = (int)context.GetVariable("Count")! + 1;
                context.SetVariable("Count", count);
                context.WriteValue(context.GetVariable("Value") + "|" + context.GetVariable("Mode") + "|" +
                    context.GetVariable("script:Marker") + "|" + count);
            }, context => context.WriteValue("end:" + context.GetVariable("Count")), null);
        PowerShellNativeFunctionHost.InstallDeclaredFunction(module, name, script);
    }
}
