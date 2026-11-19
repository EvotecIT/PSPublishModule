namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Management.Automation;
    using System.Management.Automation.Language;
    using System.Reflection;

    public static partial class PowerShellNativeFunctionHost
    {
        /// <summary>Runs a compiled clause after native script invocation has resolved parameter types and allocated storage.</summary>
        public static void InvokeDeferredTypeClause(EngineIntrinsics engine, string name, string declarationBody,
            Action<PowerShellNativeFunctionContext> callback)
        {
            if (engine == null) throw new ArgumentNullException(nameof(engine));
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            if (engine.SessionState.LanguageMode != PSLanguageMode.FullLanguage)
                throw new NotSupportedException("Compiled native clauses require a FullLanguage invocation.");
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var execution = typeof(EngineIntrinsics).GetField("_context", flags)?.GetValue(engine)
                ?? throw new NotSupportedException("PowerShell's active execution context is unavailable.");
            var processor = NativeContract.Shared.CurrentCommandProcessor.GetValue(execution, null)
                ?? throw new NotSupportedException("PowerShell's active command processor is unavailable.");
            var owner = processor.GetType().GetField("_functionContext", flags) is not null
                ? processor : processor.GetType().GetProperty("Command", flags)?.GetValue(processor, null);
            if (owner == null || owner.GetType().Assembly != typeof(PSObject).Assembly)
                throw new NotSupportedException("A deferred compiled clause requires an active SDK script command.");
            var context = owner.GetType().GetField("_functionContext", flags)?.GetValue(owner)
                ?? throw new NotSupportedException("PowerShell's active native function frame is unavailable.");
            var script = context.GetType().GetField("_scriptBlock", flags)?.GetValue(context) as ScriptBlock;
            if (script?.Ast is not FunctionDefinitionAst declaration || declaration.Name != name ||
                declaration.Body.Extent.Text.Replace("\r\n", "\n") != "{\n" + declarationBody.Replace("\r\n", "\n") + "\n}")
                throw new NotSupportedException("A deferred compiled clause requires its exact compiler-owned declaration.");
            var optimized = (owner.GetType().GetField("_runOptimizedCode", flags) ??
                owner.GetType().GetField("_runOptimized", flags))?.GetValue(owner) as bool?
                ?? throw new NotSupportedException("PowerShell's active storage mode is unavailable.");
            Run(callback, context, optimized);
        }
    }
}
