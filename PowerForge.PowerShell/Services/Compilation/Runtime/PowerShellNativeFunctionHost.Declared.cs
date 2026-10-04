namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Management.Automation;
    using System.Management.Automation.Language;

    public static partial class PowerShellNativeFunctionHost
    {
        /// <summary>Creates compiled callbacks using a function's already declared native metadata and storage.</summary>
        /// <remarks>Call before exporting the function. Every declared executable clause requires a callback.</remarks>
        public static ScriptBlock CreateDeclaredFunction(PSModuleInfo module, string name,
            Action<PowerShellNativeFunctionContext>? begin, Action<PowerShellNativeFunctionContext>? process,
            Action<PowerShellNativeFunctionContext>? end)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            return module.NewBoundScriptBlock(CreateDeclaredFunction(module.SessionState, name, begin, process, end));
        }

        /// <summary>Creates compiled callbacks using a declaration in the executable's native session.</summary>
        /// <remarks>The declaration AST is preserved; mutable compiled storage is never shared with its original body.</remarks>
        public static ScriptBlock CreateDeclaredFunction(SessionState sessionState, string name,
            Action<PowerShellNativeFunctionContext>? begin, Action<PowerShellNativeFunctionContext>? process,
            Action<PowerShellNativeFunctionContext>? end)
        {
            if (sessionState == null) throw new ArgumentNullException(nameof(sessionState));
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("A function name is required.", nameof(name));
            var contract = NativeContract.Shared;
            var session = contract.NativeSessionState.GetValue(sessionState, null)!;
            var function = (FunctionInfo?)Invoke(contract.GetFunction, session, new object[] { name });
            if (function?.ScriptBlock.Ast is not FunctionDefinitionAst declaration)
                throw new ArgumentException("A native function declaration is required.", nameof(name));
            var body = declaration.Body;
            if (body.DynamicParamBlock != null ||
                typeof(ScriptBlockAst).GetProperty("CleanBlock")?.GetValue(body, null) != null ||
                (body.BeginBlock != null) != (begin != null) ||
                (body.ProcessBlock != null) != (process != null) ||
                (body.EndBlock != null) != (end != null) ||
                HasTraps(body.BeginBlock) || HasTraps(body.ProcessBlock) || HasTraps(body.EndBlock))
                throw new ArgumentException("Every supported declared clause requires its compiled callback.", nameof(name));
            // This existing constructor allocates fresh data from the unchanged declaration AST.
            // ScriptBlock.Clone shares compilation data and must not be used for callback replacement.
            var script = CreateFunctionScriptBlock(declaration);
            if (ReferenceEquals(contract.Data.GetValue(script), contract.Data.GetValue(function.ScriptBlock)))
                throw new NotSupportedException("PowerShell did not allocate independent compilation storage.");
            return InstallCompiledClauses(script, begin, process, end, null);
        }

        private static bool HasTraps(NamedBlockAst? block)
            => block?.Traps != null && block.Traps.Count != 0;
    }
}
