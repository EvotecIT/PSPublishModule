namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Linq.Expressions;
    using System.Management.Automation;
    using System.Management.Automation.Language;
    using System.Reflection;
    using System.Runtime.ExceptionServices;

    /// <summary>Hosts compiled clauses behind PowerShell's native script parameter binder.</summary>
    /// <remarks>This host requires PowerShell; it is not a runtime-free Strict implementation.</remarks>
    public static class PowerShellNativeFunctionHost
    {
        /// <summary>Creates a compiled function owned by the specified module's native session.</summary>
        /// <remarks>Defaults, validation, private command lookup, and body variables share the defining module.</remarks>
        public static ScriptBlock Create(PSModuleInfo module, string parameterDeclaration,
            Action<PowerShellNativeFunctionContext>? begin,
            Action<PowerShellNativeFunctionContext>? process,
            Action<PowerShellNativeFunctionContext>? end,
            Action<PowerShellNativeFunctionContext>? clean = null)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            return module.NewBoundScriptBlock(Create(parameterDeclaration, begin, process, end, clean));
        }

        /// <summary>Creates a function with the deployed defining file as its native script context.</summary>
        public static ScriptBlock Create(PSModuleInfo module, string parameterDeclaration, string sourcePath,
            Action<PowerShellNativeFunctionContext>? begin,
            Action<PowerShellNativeFunctionContext>? process,
            Action<PowerShellNativeFunctionContext>? end,
            Action<PowerShellNativeFunctionContext>? clean = null)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            return module.NewBoundScriptBlock(CreateCore(parameterDeclaration, sourcePath, Array.Empty<string>(), begin, process, end, clean));
        }

        /// <summary>Creates a native function with compiler-declared, initially unset local storage.</summary>
        public static ScriptBlock Create(PSModuleInfo module, string parameterDeclaration, string sourcePath, string[] localNames,
            Action<PowerShellNativeFunctionContext>? begin,
            Action<PowerShellNativeFunctionContext>? process,
            Action<PowerShellNativeFunctionContext>? end,
            Action<PowerShellNativeFunctionContext>? clean = null)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (localNames == null) throw new ArgumentNullException(nameof(localNames));
            return module.NewBoundScriptBlock(CreateCore(parameterDeclaration, sourcePath, localNames, begin, process, end, clean));
        }

        /// <summary>Creates a fresh function block containing parameter metadata and compiled callbacks.</summary>
        /// <remarks>Parameter defaults and validation remain native metadata. Authored body statements are rejected.</remarks>
        public static ScriptBlock Create(string parameterDeclaration,
            Action<PowerShellNativeFunctionContext>? begin,
            Action<PowerShellNativeFunctionContext>? process,
            Action<PowerShellNativeFunctionContext>? end,
            Action<PowerShellNativeFunctionContext>? clean = null)
            => CreateCore(parameterDeclaration, null, Array.Empty<string>(), begin, process, end, clean);

        private static ScriptBlock CreateCore(string parameterDeclaration, string? sourcePath, string[] localNames,
            Action<PowerShellNativeFunctionContext>? begin,
            Action<PowerShellNativeFunctionContext>? process,
            Action<PowerShellNativeFunctionContext>? end,
            Action<PowerShellNativeFunctionContext>? clean)
        {
            if (parameterDeclaration == null) throw new ArgumentNullException(nameof(parameterDeclaration));
            if (clean != null && typeof(ScriptBlockAst).GetProperty("CleanBlock") == null)
                throw new NotSupportedException("The loaded PowerShell host does not support clean clauses.");
            var metadata = Parse(parameterDeclaration, sourcePath);
            if (metadata.ParamBlock == null || metadata.BeginBlock != null || metadata.ProcessBlock != null ||
                metadata.DynamicParamBlock != null || HasUsingStatements(metadata) || metadata.ScriptRequirements != null ||
                typeof(ScriptBlockAst).GetProperty("CleanBlock")?.GetValue(metadata, null) != null ||
                (metadata.EndBlock != null && (metadata.EndBlock.Statements.Count != 0 ||
                    metadata.EndBlock.Traps != null && metadata.EndBlock.Traps.Count != 0)))
                throw new ArgumentException("Only a parameter declaration is accepted by the compiled function host.", nameof(parameterDeclaration));
            // Reparse a compiler-owned body. Never mutate ScriptBlock.Create's shared source cache.
            // ParamBlock.Extent excludes its function attributes, including CmdletBinding.
            var source = parameterDeclaration;
            var localDeclarations = string.Empty;
            foreach (var name in localNames)
            {
                if (string.IsNullOrEmpty(name) || name.IndexOf(':') >= 0)
                    throw new ArgumentException("Native local storage requires unqualified variable names.", nameof(localNames));
                localDeclarations += "; ${" + name.Replace("`", "``").Replace("}", "`}") + "} = $null";
            }
            if (begin != null) source += "\nbegin { throw 'Compiled begin callback was not installed.' }";
            if (process != null) source += "\nprocess { throw 'Compiled process callback was not installed.' }";
            if (end != null || begin == null && process == null)
                // These declarations allocate native tuple slots. The installed callback replaces the entire clause.
                source += "\nend { if ($false) { " + localDeclarations + " }; throw 'Compiled end callback was not installed.' }";
            if (clean != null) source += "\nclean { throw 'Compiled clean callback was not installed.' }";
            var script = Parse(source, sourcePath).GetScriptBlock();
            var contract = NativeContract.Shared;
            Invoke(contract.Compile, script, new object[] { false });
            Invoke(contract.Compile, script, new object[] { true });
            var data = contract.Data.GetValue(script)!;
            if (begin != null) contract.Install(data, "BeginBlock", begin);
            if (process != null) contract.Install(data, "ProcessBlock", process);
            if (end != null || begin == null && process == null)
                contract.Install(data, "EndBlock", end ?? (_ => { }));
            if (clean != null) contract.Install(data, "CleanBlock", clean);
            return script;
        }

        private static ScriptBlockAst Parse(string source, string? sourcePath)
        {
            // The installed 5.1 engine exposes this overload even though its reference assembly omits it.
            var arguments = new object[] { source, sourcePath!, null!, null! };
            var ast = (ScriptBlockAst)Invoke(NativeContract.Shared.ParseInputWithFile, null, arguments)!;
            var errors = (ParseError[])arguments[3];
            if (errors.Length != 0) throw new ArgumentException(errors[0].Message, nameof(source));
            return ast;
        }

        private static bool HasUsingStatements(ScriptBlockAst ast)
        {
            // The 5.1 reference assembly and installed engine expose different generic return types.
            var statements = typeof(ScriptBlockAst).GetProperty("UsingStatements")?.GetValue(ast, null) as System.Collections.IEnumerable;
            if (statements != null) foreach (var statement in statements) return true;
            return false;
        }

        internal static object? Invoke(MethodInfo method, object? target, object[] arguments)
        {
            try { return method.Invoke(target, arguments); }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }

        private static void Run(Action<PowerShellNativeFunctionContext> callback, object nativeContext, bool optimized)
        {
            using (var context = new PowerShellNativeFunctionContext(nativeContext, optimized)) callback(context);
        }

        internal sealed class NativeContract
        {
            private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            private static readonly Lazy<NativeContract> Cached = new(() => new NativeContract());
            internal static NativeContract Shared => Cached.Value;
            internal readonly MethodInfo Compile;
            internal readonly FieldInfo Data;
            internal readonly FieldInfo ExecutionContext, OutputPipe, LocalsTuple;
            internal readonly PropertyInfo SessionState;
            internal readonly PropertyInfo CurrentCommandProcessor, ProcessorRuntime;
            internal readonly PropertyInfo ExecutionStatus;
            internal readonly MethodInfo AddOutput, Stringify;
            internal readonly MethodInfo GetVariableValue;
            internal readonly MethodInfo ParseInputWithFile;
            internal readonly MethodInfo TryGetLocalVariable;
            internal readonly ConstructorInfo InterpolationContext;
            private readonly Type _functionContext;

            private NativeContract()
            {
                ParseInputWithFile = typeof(Parser).GetMethod("ParseInput", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(string), typeof(string), typeof(Token[]).MakeByRefType(), typeof(ParseError[]).MakeByRefType() }, null)
                    ?? throw new NotSupportedException("PowerShell's defining-file parser operation is unavailable.");
                Compile = typeof(ScriptBlock).GetMethod("Compile", Flags, null, new[] { typeof(bool) }, null)
                    ?? throw new NotSupportedException("PowerShell's compiled script contract is unavailable.");
                Data = typeof(ScriptBlock).GetField("_scriptBlockData", Flags)
                    ?? throw new NotSupportedException("PowerShell's compiled script data is unavailable.");
                _functionContext = typeof(PSObject).Assembly.GetType("System.Management.Automation.Language.FunctionContext", true)!;
                ExecutionContext = _functionContext.GetField("_executionContext", Flags)
                    ?? throw new NotSupportedException("PowerShell's function execution context is unavailable.");
                OutputPipe = _functionContext.GetField("_outputPipe", Flags)
                    ?? throw new NotSupportedException("PowerShell's function output pipe is unavailable.");
                LocalsTuple = _functionContext.GetField("_localsTuple", Flags)
                    ?? throw new NotSupportedException("PowerShell's function local storage is unavailable.");
                TryGetLocalVariable = LocalsTuple.FieldType.GetMethod("TryGetLocalVariable", Flags, null,
                    new[] { typeof(string), typeof(bool), typeof(PSVariable).MakeByRefType() }, null)
                    ?? throw new NotSupportedException("PowerShell's native local-variable operation is unavailable.");
                SessionState = ExecutionContext.FieldType.GetProperty("SessionState", Flags)
                    ?? throw new NotSupportedException("PowerShell's native session is unavailable.");
                ExecutionStatus = ExecutionContext.FieldType.GetProperty("QuestionMarkVariableValue", Flags)
                    ?? throw new NotSupportedException("PowerShell's native execution status is unavailable.");
                CurrentCommandProcessor = ExecutionContext.FieldType.GetProperty("CurrentCommandProcessor", Flags)
                    ?? throw new NotSupportedException("PowerShell's native command processor is unavailable.");
                ProcessorRuntime = CurrentCommandProcessor.PropertyType.GetProperty("CommandRuntime", Flags)
                    ?? throw new NotSupportedException("PowerShell's native command runtime is unavailable.");
                AddOutput = OutputPipe.FieldType.GetMethod("Add", Flags, null, new[] { typeof(object) }, null)
                    ?? throw new NotSupportedException("PowerShell's function output operation is unavailable.");
                Stringify = typeof(PSObject).GetMethod("ToStringParser", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { ExecutionContext.FieldType, typeof(object) }, null)
                    ?? throw new NotSupportedException("PowerShell's native stringification operation is unavailable.");
                GetVariableValue = typeof(PSObject).Assembly.GetType("System.Management.Automation.VariableOps", true)!
                    .GetMethod("GetVariableValue", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                        null, new[] { typeof(VariablePath), ExecutionContext.FieldType, typeof(VariableExpressionAst) }, null)
                    ?? throw new NotSupportedException("PowerShell's native variable-read operation is unavailable.");
                InterpolationContext = typeof(ExpandableStringExpressionAst).GetConstructor(Flags, null,
                    new[] { typeof(IScriptExtent), typeof(string), typeof(string), typeof(StringConstantType),
                        typeof(System.Collections.Generic.IEnumerable<ExpressionAst>) }, null)
                    ?? throw new NotSupportedException("PowerShell's native interpolation context is unavailable.");
            }

            internal void Install(object data, string clause, Action<PowerShellNativeFunctionContext> callback)
            {
                var parameter = Expression.Parameter(_functionContext, "context");
                var run = typeof(PowerShellNativeFunctionHost).GetMethod(nameof(Run), BindingFlags.Static | BindingFlags.NonPublic)!;
                foreach (var name in new[] { clause, "Unoptimized" + clause })
                {
                    var call = Expression.Call(run, Expression.Constant(callback), Expression.Convert(parameter, typeof(object)),
                        Expression.Constant(name == clause));
                    var body = Expression.Lambda(typeof(Action<>).MakeGenericType(_functionContext), call, parameter).Compile();
                    var property = data.GetType().GetProperty(name, Flags)
                        ?? throw new NotSupportedException("PowerShell's compiled clause contract is unavailable: " + name);
                    property.SetValue(data, body, null);
                }
            }
        }
    }
}
