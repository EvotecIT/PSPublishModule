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

        /// <summary>Prepares a compiler-owned function frame with bounded lexical enum declarations.</summary>
        public static ScriptBlock CreateOwnedDeclaration(PSModuleInfo module, string name, string parameterDeclaration,
            string[] localNames, string[] localTypeDeclarations,
            Action<PowerShellNativeFunctionContext>? begin, Action<PowerShellNativeFunctionContext>? process,
            Action<PowerShellNativeFunctionContext>? end, string[] functionTypeDeclarations)
            => CreateOwnedDeclaration(module, name, parameterDeclaration, localNames, localTypeDeclarations,
                begin, process, end, functionTypeDeclarations, null);

        /// <summary>Retains SDK dynamic discovery beside the exact generated executable-clause stubs.</summary>
        public static ScriptBlock CreateOwnedDeclaration(PSModuleInfo module, string name, string parameterDeclaration,
            string[] localNames, string[] localTypeDeclarations,
            Action<PowerShellNativeFunctionContext>? begin, Action<PowerShellNativeFunctionContext>? process,
            Action<PowerShellNativeFunctionContext>? end, string[] functionTypeDeclarations, string? dynamicParameterDeclaration)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            return CreateOwnedDeclaration(module.SessionState, name, parameterDeclaration, localNames, localTypeDeclarations,
                begin, process, end, functionTypeDeclarations, dynamicParameterDeclaration);
        }

        /// <summary>Prepares an executable's compiler-owned declaration without creating parameter attributes during registration.</summary>
        public static ScriptBlock CreateOwnedDeclaration(SessionState sessionState, string name, string parameterDeclaration,
            string[] localNames, string[] localTypeDeclarations,
            Action<PowerShellNativeFunctionContext>? begin, Action<PowerShellNativeFunctionContext>? process,
            Action<PowerShellNativeFunctionContext>? end)
            => CreateOwnedDeclaration(sessionState, name, parameterDeclaration, localNames, localTypeDeclarations, begin, process, end, Array.Empty<string>());

        /// <summary>Retains bounded enum identities in the executable function's exact declaration stub.</summary>
        public static ScriptBlock CreateOwnedDeclaration(SessionState sessionState, string name, string parameterDeclaration,
            string[] localNames, string[] localTypeDeclarations,
            Action<PowerShellNativeFunctionContext>? begin, Action<PowerShellNativeFunctionContext>? process,
            Action<PowerShellNativeFunctionContext>? end, string[] functionTypeDeclarations)
            => CreateOwnedDeclaration(sessionState, name, parameterDeclaration, localNames, localTypeDeclarations,
                begin, process, end, functionTypeDeclarations, null);

        /// <summary>Prepares compiled clauses while preserving native binding-time dynamic discovery.</summary>
        public static ScriptBlock CreateOwnedDeclaration(SessionState sessionState, string name, string parameterDeclaration,
            string[] localNames, string[] localTypeDeclarations,
            Action<PowerShellNativeFunctionContext>? begin, Action<PowerShellNativeFunctionContext>? process,
            Action<PowerShellNativeFunctionContext>? end, string[] functionTypeDeclarations, string? dynamicParameterDeclaration)
        {
            if (sessionState == null) throw new ArgumentNullException(nameof(sessionState));
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("A function name is required.", nameof(name));
            if (localNames == null) throw new ArgumentNullException(nameof(localNames));
            if (localTypeDeclarations == null) throw new ArgumentNullException(nameof(localTypeDeclarations));
            if (functionTypeDeclarations == null) throw new ArgumentNullException(nameof(functionTypeDeclarations));
            var contract = NativeContract.Shared;
            var session = contract.NativeSessionState.GetValue(sessionState, null)!;
            var function = (FunctionInfo?)Invoke(contract.GetFunction, session, new object[] { name });
            // A rejected declaration leaves its protected predecessor in place. Preserve the native
            // failure without inspecting/preparing its body; installation also skips this function.
            if (function != null && (function.Options & (ScopedItemOptions.ReadOnly | ScopedItemOptions.Constant)) != 0)
                return function.ScriptBlock;
            var expected = "{\n" + CreateDeclarationSource(parameterDeclaration, null, localNames,
                begin != null, process != null, end != null, false, localTypeDeclarations, functionTypeDeclarations, dynamicParameterDeclaration) + "\n}";
            if (function?.ScriptBlock.Ast is not FunctionDefinitionAst declaration ||
                !string.Equals(declaration.Body.Extent.Text.Replace("\r\n", "\n"), expected.Replace("\r\n", "\n"), StringComparison.Ordinal))
                throw new ArgumentException("Only the exact compiler-owned declaration stub can retain shared native metadata.", nameof(name));
            var script = function.ScriptBlock;
            return InstallLazyCompiledClauses(script, begin, process, end, functionTypeDeclarations);
        }
    }
}
