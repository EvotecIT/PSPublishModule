namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Management.Automation;
    using System.Management.Automation.Language;
    using System.Reflection;

    public static partial class PowerShellNativeFunctionHost
    {
        /// <summary>Prepares only compiler-owned declaration stubs while retaining native lazy parameter metadata.</summary>
        /// <remarks>Authored bodies are rejected. The caller supplies stateless compiled callbacks before export.</remarks>
        public static ScriptBlock CreateOwnedDeclaration(PSModuleInfo module, string name, string parameterDeclaration,
            string[] localNames, string[] localTypeDeclarations,
            Action<PowerShellNativeFunctionContext>? begin, Action<PowerShellNativeFunctionContext>? process,
            Action<PowerShellNativeFunctionContext>? end)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            return CreateOwnedDeclaration(module.SessionState, name, parameterDeclaration, localNames, localTypeDeclarations, begin, process, end);
        }

        /// <summary>Prepares an executable's compiler-owned declaration without creating parameter attributes during registration.</summary>
        public static ScriptBlock CreateOwnedDeclaration(SessionState sessionState, string name, string parameterDeclaration,
            string[] localNames, string[] localTypeDeclarations,
            Action<PowerShellNativeFunctionContext>? begin, Action<PowerShellNativeFunctionContext>? process,
            Action<PowerShellNativeFunctionContext>? end)
        {
            if (sessionState == null) throw new ArgumentNullException(nameof(sessionState));
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("A function name is required.", nameof(name));
            if (localNames == null) throw new ArgumentNullException(nameof(localNames));
            if (localTypeDeclarations == null) throw new ArgumentNullException(nameof(localTypeDeclarations));
            var contract = NativeContract.Shared;
            var session = contract.NativeSessionState.GetValue(sessionState, null)!;
            var function = (FunctionInfo?)Invoke(contract.GetFunction, session, new object[] { name });
            // A rejected declaration leaves its protected predecessor in place. Preserve the native
            // failure without inspecting/preparing its body; installation also skips this function.
            if (function != null && (function.Options & (ScopedItemOptions.ReadOnly | ScopedItemOptions.Constant)) != 0)
                return function.ScriptBlock;
            var expected = "{\n" + CreateDeclarationSource(parameterDeclaration, null, localNames,
                begin != null, process != null, end != null, false, localTypeDeclarations) + "\n}";
            if (function?.ScriptBlock.Ast is not FunctionDefinitionAst declaration ||
                !string.Equals(declaration.Body.Extent.Text.Replace("\r\n", "\n"), expected.Replace("\r\n", "\n"), StringComparison.Ordinal))
                throw new ArgumentException("Only the exact compiler-owned declaration stub can retain shared native metadata.", nameof(name));
            var script = function.ScriptBlock;
            var data = contract.Data.GetValue(script)!;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var unoptimized = data.GetType().GetMethod("CompileUnoptimized", flags, null, Type.EmptyTypes, null)
                ?? throw new NotSupportedException("PowerShell's lazy declaration compilation is unavailable.");
            var optimized = data.GetType().GetMethod("CompileOptimized", flags, null, Type.EmptyTypes, null)
                ?? throw new NotSupportedException("PowerShell's optimized declaration compilation is unavailable.");
            // These native operations retain security checks and normal compilation locks/flags, but
            // leave parameter metadata initialization to the engine's ordinary command lookup/binder.
            // Only generated stubs share this data; the general authored-body owner stays independent.
            lock (data)
            {
                Invoke(unoptimized, data, Array.Empty<object>());
                Invoke(optimized, data, Array.Empty<object>());
                if (begin != null) contract.Install(data, "BeginBlock", begin);
                if (process != null) contract.Install(data, "ProcessBlock", process);
                if (end != null || begin == null && process == null)
                    contract.Install(data, "EndBlock", end ?? (_ => { }));
            }
            return script;
        }
    }
}
