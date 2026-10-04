namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Management.Automation;
    using System.Reflection;

    public static partial class PowerShellNativeFunctionHost
    {
        /// <summary>Installs callbacks without constructing parameter attributes before native invocation.</summary>
        private static ScriptBlock InstallLazyCompiledClauses(ScriptBlock script,
            Action<PowerShellNativeFunctionContext>? begin, Action<PowerShellNativeFunctionContext>? process,
            Action<PowerShellNativeFunctionContext>? end, string[] functionTypeDeclarations)
        {
            var contract = NativeContract.Shared;
            var data = contract.Data.GetValue(script)!;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var unoptimized = data.GetType().GetMethod("CompileUnoptimized", flags, null, Type.EmptyTypes, null)
                ?? throw new NotSupportedException("PowerShell's lazy declaration compilation is unavailable.");
            var optimized = data.GetType().GetMethod("CompileOptimized", flags, null, Type.EmptyTypes, null)
                ?? throw new NotSupportedException("PowerShell's optimized declaration compilation is unavailable.");
            // Retain native security checks, compilation locks and flags. Leave parameter
            // metadata initialization to ordinary command lookup and the native binder.
            lock (data)
            {
                Invoke(unoptimized, data, Array.Empty<object>());
                Invoke(optimized, data, Array.Empty<object>());
                if (functionTypeDeclarations.Length != 0) RegisterFunctionEnumScope(script, data);
                if (begin != null) contract.Install(data, "BeginBlock", begin);
                if (process != null) contract.Install(data, "ProcessBlock", process);
                if (end != null || begin == null && process == null)
                    contract.Install(data, "EndBlock", end ?? (_ => { }));
            }
            return script;
        }
    }
}
