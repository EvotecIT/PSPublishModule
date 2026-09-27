using System;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Reflection;
using PowerForge.Generated.Runtime;

namespace Generic.Compiler.StatementErrors;

// Qualification fixture only: admission and generated executable integration remain separate.
public static class NativeScriptEntryFixture
{
    public static int CallbackInvocations;

    public static ExternalScriptInfo Create(string path)
        => Create(path, null);

    public static ExternalScriptInfo Create(string path, Action<PowerShellNativeFunctionContext>? compiledBody)
    {
        CallbackInvocations = 0;
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var constructor = typeof(ExternalScriptInfo).GetConstructor(
            flags, null, new[] { typeof(string), typeof(string) }, null)!;
        var info = (ExternalScriptInfo)constructor.Invoke(new object[] { Path.GetFileName(path), path });
        var ast = Parser.ParseFile(path, out _, out var errors);
        if (errors.Length != 0) throw new ArgumentException(errors[0].Message);
        // A fresh parsed AST owns independent compilation data; never patch a cached clone.
        var script = ast.GetScriptBlock();
        PowerShellNativeFunctionHost.InstallCompiledClauses(script, null, null, Body, null);
        typeof(ExternalScriptInfo).GetProperty("ScriptBlock", flags)!.SetValue(info, script, null);
        return info;

        void Body(PowerShellNativeFunctionContext context)
        {
            CallbackInvocations++;
            if (compiledBody is not null)
            {
                compiledBody(context);
                return;
            }
            object? Read(string name) => context.GetVariable(
                name, false, path, 1, 1, 1, name.Length + 2, "$" + name, true);
            context.SetVariable("Scratch", "owned");
            if ((Read("Value") as string) == "rebind") context.SetVariable("Value", "changed");
            context.WriteValue(Read("Value"));
            context.WriteValue(Read("global:BindCount"));
            context.WriteValue(Read("global:ValidateCount"));
            var bound = (System.Collections.IDictionary)Read("PSBoundParameters")!;
            context.WriteValue(bound.Contains("Value"));
            // This probe claims PowerShell's Count observation, not args reference identity.
            context.WriteValue((Read("args") as Array)?.Length ?? 0);
            var invocation = (InvocationInfo)Read("MyInvocation")!;
            context.WriteValue(invocation.MyCommand.CommandType.ToString());
            context.WriteValue(Read("PSCommandPath"));
            context.WriteValue(Read("PSScriptRoot"));
            context.WriteValue(Read("Scratch"));
        }
    }
}
