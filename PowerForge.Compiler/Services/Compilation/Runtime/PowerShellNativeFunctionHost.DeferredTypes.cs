namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Linq;
    using System.Management.Automation;
    using System.Management.Automation.Language;
    using System.Reflection;
    using System.Runtime.CompilerServices;

    public static partial class PowerShellNativeFunctionHost
    {
        private static readonly ConditionalWeakTable<object, DeferredNestedClauses> DeferredNestedCallbacks = new();
        private static readonly ConditionalWeakTable<object, ErrorRecord> DeferredClauseErrors = new();

        /// <summary>Runs a compiled clause after native script invocation has resolved parameter types and allocated storage.</summary>
        public static bool InvokeDeferredTypeClause(EngineIntrinsics engine, string name, string declarationBody,
            Action<PowerShellNativeFunctionContext> callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            var (context, script, optimized) = GetDeferredFrame(engine);
            if (script.Ast is not FunctionDefinitionAst declaration || declaration.Name != name ||
                declaration.Body.Extent.Text.Replace("\r\n", "\n") != "{\n" + declarationBody.Replace("\r\n", "\n") + "\n}")
                throw new NotSupportedException("A deferred compiled clause requires its exact compiler-owned declaration.");
            return RunDeferredClause(callback, context, optimized);
        }

        internal static ScriptBlock CreateDeferredNestedFunction(FunctionDefinitionAst definition, string file, string[] localNames,
            string[] localTypeDeclarations, Action<PowerShellNativeFunctionContext>? begin,
            Action<PowerShellNativeFunctionContext>? process, Action<PowerShellNativeFunctionContext>? end)
        {
            var source = CreateDeclarationSource("param()", file, localNames, begin is not null,
                process is not null, end is not null, false, localTypeDeclarations, null, null,
                RequiresUnoptimizedStorage(definition.Body));
            var host = "[" + typeof(PowerShellNativeFunctionHost).AssemblyQualifiedName + "]";
            var script = CreateNestedDeclarationStub(definition, BindDeferredClauses(source,
                host + "::InvokeDeferredNestedTypeClause", host + "::TakeDeferredClauseError"), file);
            DeferredNestedCallbacks.Add(NativeContract.Shared.Data.GetValue(script)!, new DeferredNestedClauses(begin, process, end));
            return script;
        }

        internal static string BindDeferredClauses(string source, string bridge, string takeError)
        {
            foreach (var (name, clause) in new[] { ("begin", 0), ("process", 1), ("end", 2) })
                source = source.Replace("throw 'Compiled " + name + " callback was not installed.'",
                    "if (" + bridge + "($ExecutionContext, " + clause + ")) { throw (" + takeError + "($ExecutionContext)) }");
            return source;
        }

        internal static ScriptBlock CreateNestedDeclarationStub(FunctionDefinitionAst definition, string source, string file)
        {
            var parsed = Parse(source, file);
            var parameters = definition.Body.ParamBlock is { } declared ? (ParamBlockAst)declared.Copy() :
                new ParamBlockAst(definition.Extent, null,
                    definition.Parameters?.Select(static parameter => (ParameterAst)parameter.Copy()));
            var body = new ScriptBlockAst(definition.Body.Extent, parameters, (NamedBlockAst?)parsed.BeginBlock?.Copy(),
                (NamedBlockAst?)parsed.ProcessBlock?.Copy(), (NamedBlockAst?)parsed.EndBlock?.Copy(),
                (NamedBlockAst?)definition.Body.DynamicParamBlock?.Copy());
            return CreateFunctionScriptBlock(new FunctionDefinitionAst(definition.Extent, false,
                definition.IsFilter, definition.Name, null, body));
        }

        private static readonly Lazy<(ConstructorInfo Constructor, FieldInfo Disabled)> NativeVariableVisitor = new(() =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var visitor = typeof(PSObject).Assembly.GetType("System.Management.Automation.Language.FindAllVariablesVisitor", true)!;
            return (
                visitor.GetConstructor(flags, null, new[] { typeof(bool), typeof(bool) }, null)
                    ?? throw new NotSupportedException("PowerShell's native variable analysis is unavailable."),
                visitor.GetField("_disableOptimizations", flags)
                    ?? throw new NotSupportedException("PowerShell's native variable storage decision is unavailable."));
        });

        internal static bool RequiresUnoptimizedStorage(ScriptBlockAst body)
        {
            // Ask the SDK's visitor to inspect declarations and command syntax without compiling any authored type expressions.
            var contract = NativeVariableVisitor.Value;
            var visitor = (AstVisitor)contract.Constructor.Invoke(new object[] { false, false });
            body.Visit(visitor);
            return (bool)contract.Disabled.GetValue(visitor)!;
        }

        /// <summary>Runs a nested declaration's compiled clause after PowerShell has bound its runtime types.</summary>
        public static bool InvokeDeferredNestedTypeClause(EngineIntrinsics engine, int clause)
        {
            var (context, script, optimized) = GetDeferredFrame(engine);
            if (!DeferredNestedCallbacks.TryGetValue(NativeContract.Shared.Data.GetValue(script)!, out var callbacks) ||
                callbacks.GetCallback(clause) is not { } callback)
                throw new NotSupportedException("A deferred nested clause requires its compiler-owned native frame.");
            return RunDeferredClause(callback, context, optimized);
        }

        /// <summary>Returns the error recorded by the compiled clause on this native invocation.</summary>
        public static ErrorRecord TakeDeferredClauseError(EngineIntrinsics engine)
        {
            var (context, _, _) = GetDeferredFrame(engine);
            if (!DeferredClauseErrors.TryGetValue(context, out var error) || !DeferredClauseErrors.Remove(context))
                throw new NotSupportedException("The active compiled clause has no recorded error.");
            return error;
        }

        private static bool RunDeferredClause(Action<PowerShellNativeFunctionContext> callback, object context, bool optimized)
        {
            try
            {
                Run(callback, context, optimized);
                return false;
            }
            catch (RuntimeException failure) when (failure is not PipelineStoppedException)
            {
                // Native throw consumes the exact ErrorRecord. The weak frame key keeps
                // the record outside authored storage and allows cancellation to drop it.
                DeferredClauseErrors.Remove(context);
                DeferredClauseErrors.Add(context, failure.ErrorRecord);
                return true;
            }
        }

        private static (object Context, ScriptBlock Script, bool Optimized) GetDeferredFrame(EngineIntrinsics engine)
        {
            if (engine == null) throw new ArgumentNullException(nameof(engine));
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
            var script = context.GetType().GetField("_scriptBlock", flags)?.GetValue(context) as ScriptBlock
                ?? throw new NotSupportedException("PowerShell's active native function block is unavailable.");
            var optimized = (owner.GetType().GetField("_runOptimizedCode", flags) ??
                owner.GetType().GetField("_runOptimized", flags))?.GetValue(owner) as bool?
                ?? throw new NotSupportedException("PowerShell's active storage mode is unavailable.");
            return (context, script, optimized);
        }

        private sealed class DeferredNestedClauses
        {
            private readonly Action<PowerShellNativeFunctionContext>?[] _callbacks;

            internal DeferredNestedClauses(Action<PowerShellNativeFunctionContext>? begin,
                Action<PowerShellNativeFunctionContext>? process, Action<PowerShellNativeFunctionContext>? end)
                => _callbacks = new[] { begin, process, end };

            internal Action<PowerShellNativeFunctionContext>? GetCallback(int clause)
                => (uint)clause < (uint)_callbacks.Length ? _callbacks[clause] : null;
        }
    }
}
