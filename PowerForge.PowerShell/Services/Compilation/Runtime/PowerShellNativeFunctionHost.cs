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
        /// <summary>Installs a prepared body after PowerShell has declared the function and its aliases.</summary>
        /// <remarks>The native declaration owns preferences, scope, and errors. A rejected read-only declaration is left intact.</remarks>
        public static void InstallDeclaredFunction(PSModuleInfo module, string name, ScriptBlock script)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            InstallDeclaredFunction(module.SessionState, name, script);
        }

        /// <summary>Installs a compiled body into a function declared in the current executable session.</summary>
        /// <remarks>The declaration remains the owner of its native parameter and command metadata.</remarks>
        public static void InstallDeclaredFunction(SessionState sessionState, string name, ScriptBlock script)
        {
            if (sessionState == null) throw new ArgumentNullException(nameof(sessionState));
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("A function name is required.", nameof(name));
            if (script == null) throw new ArgumentNullException(nameof(script));
            var contract = NativeContract.Shared;
            var session = contract.NativeSessionState.GetValue(sessionState, null)!;
            var function = (FunctionInfo?)Invoke(contract.GetFunction, session, new object[] { name });
            if (function == null || (function.Options & (ScopedItemOptions.ReadOnly | ScopedItemOptions.Constant)) != 0)
                return;
            Invoke(contract.UpdateFunction, function, new object[] { script, false, function.Options });
        }

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
            Action<PowerShellNativeFunctionContext>? clean = null, string[]? localTypeDeclarations = null)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (localNames == null) throw new ArgumentNullException(nameof(localNames));
            return module.NewBoundScriptBlock(CreateCore(parameterDeclaration, sourcePath, localNames, begin, process, end, clean, localTypeDeclarations));
        }

        /// <summary>Creates a compiled function body for the executable's current PowerShell session.</summary>
        public static ScriptBlock Create(string parameterDeclaration, string sourcePath, string[] localNames,
            Action<PowerShellNativeFunctionContext>? begin,
            Action<PowerShellNativeFunctionContext>? process,
            Action<PowerShellNativeFunctionContext>? end,
            Action<PowerShellNativeFunctionContext>? clean = null, string[]? localTypeDeclarations = null)
        {
            if (localNames == null) throw new ArgumentNullException(nameof(localNames));
            return CreateCore(parameterDeclaration, sourcePath, localNames, begin, process, end, clean, localTypeDeclarations);
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
            Action<PowerShellNativeFunctionContext>? clean, string[]? localTypeDeclarations = null)
        {
            if (parameterDeclaration == null) throw new ArgumentNullException(nameof(parameterDeclaration));
            if (clean != null && typeof(ScriptBlockAst).GetProperty("CleanBlock") == null)
                // Keep other commands importable on older hosts. The unavailable command still
                // owns its native parameter metadata and rejects invocation before doing work.
                return CreateCore(parameterDeclaration, sourcePath, Array.Empty<string>(),
                    _ => throw new NotSupportedException("This command requires PowerShell 7.3 or newer because it has a clean block."),
                    null, null, null);
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
            var typedLocals = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var typedDeclarations = string.Empty;
            foreach (var declaration in localTypeDeclarations ?? Array.Empty<string>())
            {
                var validated = ValidateLocalTypeDeclaration(declaration, localNames, out var name);
                typedLocals.Add(name);
                typedDeclarations += "; " + validated + " = $null";
            }
            foreach (var name in localNames)
            {
                if (string.IsNullOrEmpty(name) || name.IndexOf(':') >= 0)
                    throw new ArgumentException("Native local storage requires unqualified variable names.", nameof(localNames));
                if (!typedLocals.Contains(name))
                    localDeclarations += "; ${" + name.Replace("`", "``").Replace("}", "`}") + "} = $null";
            }
            localDeclarations += typedDeclarations;
            if (begin != null) source += "\nbegin { if ($false) { " + localDeclarations + " }; throw 'Compiled begin callback was not installed.' }";
            if (process != null) source += "\nprocess { if ($false) { " + localDeclarations + " }; throw 'Compiled process callback was not installed.' }";
            if (end != null || begin == null && process == null && clean == null)
                // These declarations allocate native tuple slots. The installed callback replaces the entire clause.
                source += "\nend { if ($false) { " + localDeclarations + " }; throw 'Compiled end callback was not installed.' }";
            if (clean != null) source += "\nclean { if ($false) { " + localDeclarations + " }; throw 'Compiled clean callback was not installed.' }";
            return InstallCompiledClauses(Parse(source, sourcePath).GetScriptBlock(), begin, process, end, clean);
        }

        /// <summary>Preserves a literal block's source metadata while replacing all executable clauses with compiled callbacks.</summary>
        internal static ScriptBlock CreateCompiledScriptBlock(PSModuleInfo module, string sourceDocument, string sourcePath,
            int startOffset, int endOffset, Action<PowerShellNativeFunctionContext>? begin,
            Action<PowerShellNativeFunctionContext>? process, Action<PowerShellNativeFunctionContext>? end,
            Action<PowerShellNativeFunctionContext>? clean)
        {
            var document = ParseSelectedDocument(sourceDocument, sourcePath, startOffset, endOffset);
            var expression = document.Find(node => node is ScriptBlockExpressionAst &&
                node.Extent.StartOffset == startOffset && node.Extent.EndOffset == endOffset, searchNestedScriptBlocks: true) as ScriptBlockExpressionAst
                ?? throw new ArgumentException("The compiled script block's source identity is unavailable.", nameof(sourceDocument));
            var body = expression.ScriptBlock;
            var cleanBlock = typeof(ScriptBlockAst).GetProperty("CleanBlock")?.GetValue(body, null);
            if (body.DynamicParamBlock != null ||
                (body.BeginBlock != null) != (begin != null) ||
                (body.ProcessBlock != null) != (process != null) ||
                (body.EndBlock != null) != (end != null) ||
                (cleanBlock != null) != (clean != null))
                throw new ArgumentException("Every executable script-block clause requires its compiled callback.", nameof(sourceDocument));
            return module.NewBoundScriptBlock(InstallCompiledClauses(body.GetScriptBlock(), begin, process, end, clean));
        }

        private static ScriptBlock InstallCompiledClauses(ScriptBlock script, Action<PowerShellNativeFunctionContext>? begin,
            Action<PowerShellNativeFunctionContext>? process, Action<PowerShellNativeFunctionContext>? end,
            Action<PowerShellNativeFunctionContext>? clean)
        {
            var contract = NativeContract.Shared;
            Invoke(contract.Compile, script, new object[] { false });
            Invoke(contract.Compile, script, new object[] { true });
            var data = contract.Data.GetValue(script)!;
            if (begin != null) contract.Install(data, "BeginBlock", begin);
            if (process != null) contract.Install(data, "ProcessBlock", process);
            if (end != null || begin == null && process == null && clean == null)
                contract.Install(data, "EndBlock", end ?? (_ => { }));
            if (clean != null) contract.Install(data, "CleanBlock", clean);
            return script;
        }

        private static string ValidateLocalTypeDeclaration(string declaration, string[] localNames, out string name)
        {
            var ast = Parse(declaration + " = $null", null);
            if (ast.ParamBlock != null || ast.BeginBlock != null || ast.ProcessBlock != null ||
                ast.DynamicParamBlock != null || ast.EndBlock?.Statements.Count != 1 ||
                ast.EndBlock.Statements[0] is not AssignmentStatementAst assignment || assignment.Left.Extent.Text != declaration)
                throw new ArgumentException("Native local type metadata requires one assignment target.", nameof(declaration));
            ExpressionAst target = assignment.Left;
            if (target is not AttributedExpressionAst)
                throw new ArgumentException("Native local type metadata requires an attributed target.", nameof(declaration));
            while (target is AttributedExpressionAst attribute)
            {
                if (attribute.Attribute is not TypeConstraintAst)
                    throw new ArgumentException("Optimized local storage accepts type constraints only.", nameof(declaration));
                target = attribute.Child;
            }
            if (target is not VariableExpressionAst variable ||
                !(variable.VariablePath.IsUnqualified || variable.VariablePath.IsLocal) ||
                !Array.Exists(localNames, name => name.Equals((variable.VariablePath.IsLocal ? variable.VariablePath.UserPath.Substring(6) : variable.VariablePath.UserPath), StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Native local type metadata must name a declared local slot.", nameof(declaration));
            name = variable.VariablePath.IsLocal ? variable.VariablePath.UserPath.Substring(6) : variable.VariablePath.UserPath;
            return declaration;
        }

        internal static ScriptBlockAst ParseSelectedDocument(string source, string sourcePath, int startOffset, int endOffset)
        {
            var document = ParseCore(source, sourcePath, out var errors);
            if (errors.Length == 0) return document;
            if (startOffset < 0 || endOffset <= startOffset || endOffset > source.Length)
                throw new ArgumentOutOfRangeException(nameof(startOffset));
            // An older host may reject another command's syntax. Reparse only the selected span and
            // its using metadata, retaining every offset and line break. Never compile an AST
            // whose root contains parser errors, or discard errors inside the selected literal.
            var isolated = source.ToCharArray();
            for (var index = 0; index < isolated.Length; index++)
                if ((index < startOffset || index >= endOffset) && isolated[index] != '\r' && isolated[index] != '\n')
                    isolated[index] = ' ';
            var usingStatements = typeof(ScriptBlockAst).GetProperty("UsingStatements")?.GetValue(document, null) as System.Collections.IEnumerable;
            if (usingStatements != null)
                foreach (var item in usingStatements)
                    if (item is Ast statement)
                        for (var index = statement.Extent.StartOffset; index < statement.Extent.EndOffset; index++)
                            isolated[index] = source[index];
            return Parse(new string(isolated), sourcePath);
        }

        internal static ScriptBlockAst Parse(string source, string? sourcePath)
        {
            var ast = ParseCore(source, sourcePath, out var errors);
            if (errors.Length != 0) throw new ArgumentException(errors[0].Message, nameof(source));
            return ast;
        }

        internal static ScriptBlock CreateFunctionScriptBlock(FunctionDefinitionAst function)
            => (ScriptBlock)NativeContract.Shared.FunctionScriptBlockConstructor.Invoke(new object[] { function, function.IsFilter });

        private static ScriptBlockAst ParseCore(string source, string? sourcePath, out ParseError[] errors)
        {
            // The installed 5.1 engine exposes this overload even though its reference assembly omits it.
            var arguments = new object[] { source, sourcePath!, null!, null! };
            var ast = (ScriptBlockAst)Invoke(NativeContract.Shared.ParseInputWithFile, null, arguments)!;
            errors = (ParseError[])arguments[3];
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
            internal readonly FieldInfo ExecutionContext, OutputPipe, LocalsTuple, DefiningFile;
            internal readonly FieldInfo SequencePoints, SequenceIndex;
            internal readonly PropertyInfo SessionState;
            internal readonly PropertyInfo CurrentCommandProcessor, ProcessorRuntime;
            internal readonly PropertyInfo ExecutionStatus;
            internal readonly MethodInfo AddOutput, Stringify;
            internal readonly MethodInfo GetVariableValue;
            internal readonly MethodInfo ParseInputWithFile;
            internal readonly MethodInfo TryGetLocalVariable;
            internal readonly ConstructorInfo InterpolationContext;
            internal readonly ConstructorInfo FunctionScriptBlockConstructor;
            internal readonly MethodInfo GetFunction, UpdateFunction;
            internal readonly PropertyInfo NativeSessionState;
            private readonly Type _functionContext;
            internal Type FunctionContextType => _functionContext;

            private NativeContract()
            {
                ParseInputWithFile = typeof(Parser).GetMethod("ParseInput", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(string), typeof(string), typeof(Token[]).MakeByRefType(), typeof(ParseError[]).MakeByRefType() }, null)
                    ?? throw new NotSupportedException("PowerShell's defining-file parser operation is unavailable.");
                Compile = typeof(ScriptBlock).GetMethod("Compile", Flags, null, new[] { typeof(bool) }, null)
                    ?? throw new NotSupportedException("PowerShell's compiled script contract is unavailable.");
                Data = typeof(ScriptBlock).GetField("_scriptBlockData", Flags)
                    ?? throw new NotSupportedException("PowerShell's compiled script data is unavailable.");
                FunctionScriptBlockConstructor = typeof(ScriptBlock).GetConstructor(Flags, null,
                    new[] { typeof(PSObject).Assembly.GetType("System.Management.Automation.Language.IParameterMetadataProvider", true)!, typeof(bool) }, null)
                    ?? throw new NotSupportedException("PowerShell's authored function metadata owner is unavailable.");
                NativeSessionState = typeof(SessionState).GetProperty("Internal", Flags)
                    ?? throw new NotSupportedException("PowerShell's declaration session is unavailable.");
                GetFunction = NativeSessionState.PropertyType.GetMethod("GetFunction", Flags, null, new[] { typeof(string) }, null)
                    ?? throw new NotSupportedException("PowerShell's declared function lookup is unavailable.");
                UpdateFunction = typeof(FunctionInfo).GetMethod("Update", Flags, null,
                    new[] { typeof(ScriptBlock), typeof(bool), typeof(ScopedItemOptions) }, null)
                    ?? throw new NotSupportedException("PowerShell's prepared function installation is unavailable.");
                _functionContext = typeof(PSObject).Assembly.GetType("System.Management.Automation.Language.FunctionContext", true)!;
                ExecutionContext = _functionContext.GetField("_executionContext", Flags)
                    ?? throw new NotSupportedException("PowerShell's function execution context is unavailable.");
                OutputPipe = _functionContext.GetField("_outputPipe", Flags)
                    ?? throw new NotSupportedException("PowerShell's function output pipe is unavailable.");
                LocalsTuple = _functionContext.GetField("_localsTuple", Flags)
                    ?? throw new NotSupportedException("PowerShell's function local storage is unavailable.");
                DefiningFile = _functionContext.GetField("_file", Flags)
                    ?? throw new NotSupportedException("PowerShell's defining source file is unavailable.");
                SequencePoints = _functionContext.GetField("_sequencePoints", Flags)
                    ?? throw new NotSupportedException("PowerShell's function sequence points are unavailable.");
                SequenceIndex = _functionContext.GetField("_currentSequencePointIndex", Flags)
                    ?? throw new NotSupportedException("PowerShell's function sequence index is unavailable.");
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
