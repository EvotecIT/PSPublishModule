namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Linq.Expressions;
    using System.Management.Automation;
    using System.Management.Automation.Language;
    using System.Reflection;

    public sealed partial class PowerShellNativeFunctionContext
    {
        private readonly Dictionary<string, NativeAstOperation> _functionDeclarations = new(StringComparer.Ordinal);

        /// <summary>Prepares a nested function's original native metadata with completely compiled clauses.</summary>
        /// <remarks>The returned body remains unbound; the native declaration later clones and binds it at execution.</remarks>
        public ScriptBlock CreateFunctionBody(string sourceDocument, int startOffset, int endOffset,
            Action<PowerShellNativeFunctionContext>? begin, Action<PowerShellNativeFunctionContext>? process,
            Action<PowerShellNativeFunctionContext>? end, Action<PowerShellNativeFunctionContext>? clean = null)
        {
            EnsureActive();
            var file = _contract.DefiningFile.GetValue(FunctionContext) as string ?? string.Empty;
            var definition = FindFunctionDefinition(sourceDocument, file, startOffset, endOffset);
            var clauses = definition.Body;
            var cleanBlock = typeof(ScriptBlockAst).GetProperty("CleanBlock")?.GetValue(clauses, null);
            if ((clauses.BeginBlock != null) != (begin != null) ||
                (clauses.ProcessBlock != null) != (process != null) ||
                (clauses.EndBlock != null) != (end != null) ||
                (cleanBlock != null) != (clean != null))
                throw new ArgumentException("Every executable nested-function clause requires its compiled callback.", nameof(sourceDocument));
            var native = new NativeAstCompiler(this, WrapDeclaration(definition));
            var wrapper = GetDefinitionWrapper((Expression)native.Invoke("VisitFunctionDefinition", definition)!);
            var field = GetWrapperBodyField(wrapper);
            var method = wrapper.GetType().GetMethod("GetScriptBlock", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { _executionContext.GetType(), typeof(bool) }, null)
                ?? throw new NotSupportedException("PowerShell's native declaration body factory is unavailable.");
            // The engine retains an unbound cached block and returns a fresh session-bound clone.
            // Install on that unbound block so the declaration still owns cloning and session binding.
            PowerShellNativeFunctionHost.Invoke(method, wrapper, new object[] { _executionContext, definition.IsFilter });
            var body = field.GetValue(wrapper) as ScriptBlock
                ?? throw new NotSupportedException("PowerShell's native declaration body is unavailable.");
            ValidateUnboundBody(body);
            return PowerShellNativeFunctionHost.InstallCompiledClauses(body, begin, process, end, clean);
        }

        /// <summary>Registers a compiled nested body at the authored statement through PowerShell's declaration operation.</summary>
        /// <remarks>Native registration owns scope, aliases, visibility, replacement errors, and source positions.</remarks>
        public ScriptBlock DeclareFunction(string sourceDocument, int startOffset, int endOffset, ScriptBlock compiledBody)
        {
            EnsureActive();
            if (compiledBody is null) throw new ArgumentNullException(nameof(compiledBody));
            ValidateUnboundBody(compiledBody);
            var key = startOffset + "\0" + endOffset + "\0" + sourceDocument;
            if (!_functionDeclarations.TryGetValue(key, out var operation))
            {
                var file = _contract.DefiningFile.GetValue(FunctionContext) as string ?? string.Empty;
                var definition = FindFunctionDefinition(sourceDocument, file, startOffset, endOffset);
                var native = new NativeAstCompiler(this, WrapDeclaration(definition));
                var expression = (Expression)native.Invoke("VisitFunctionDefinition", definition)!;
                var wrapper = GetDefinitionWrapper(expression);
                GetWrapperBodyField(wrapper).SetValue(wrapper, compiledBody);
                native.Expressions.Add(expression);
                native.Expressions.Add(Expression.Constant(null, typeof(object)));
                _functionDeclarations.Add(key, operation = native.Compile());
            }
            operation.Invoke(FunctionContext);
            return compiledBody;
        }

        private static FunctionDefinitionAst FindFunctionDefinition(string source, string file, int startOffset, int endOffset)
        {
            var document = PowerShellNativeFunctionHost.ParseSelectedDocument(source, file, startOffset, endOffset);
            var definition = document.Find(node => node is FunctionDefinitionAst && node.Extent.StartOffset == startOffset &&
                node.Extent.EndOffset == endOffset, searchNestedScriptBlocks: true) as FunctionDefinitionAst;
            if (definition is null || definition.IsWorkflow ||
                definition.Body.DynamicParamBlock is not null)
                throw new ArgumentException("A nested compiled declaration requires one qualified function span and body metadata.", nameof(source));
            return definition;
        }

        private static ScriptBlockAst WrapDeclaration(FunctionDefinitionAst definition)
            => new ScriptBlockAst(definition.Extent, null,
                new StatementBlockAst(definition.Extent, new[] { (StatementAst)definition.Copy() }, null), isFilter: false);

        private static object GetDefinitionWrapper(Expression expression)
        {
            if (expression is not MethodCallExpression call || call.Method.Name != "DefineFunction" ||
                call.Method.DeclaringType?.FullName != "System.Management.Automation.FunctionOps" ||
                call.Arguments.Count != 3 || call.Arguments[2] is not ConstantExpression { Value: { } wrapper } ||
                wrapper.GetType().FullName != "System.Management.Automation.ScriptBlockExpressionWrapper")
                throw new NotSupportedException("PowerShell's native function declaration contract is unavailable.");
            return wrapper;
        }

        private static FieldInfo GetWrapperBodyField(object wrapper)
        {
            var field = wrapper.GetType().GetField("_scriptBlock", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field?.FieldType != typeof(ScriptBlock))
                throw new NotSupportedException("PowerShell's native declaration body storage is unavailable.");
            return field;
        }

        private static void ValidateUnboundBody(ScriptBlock body)
        {
            var property = typeof(ScriptBlock).GetProperty("SessionStateInternal", BindingFlags.Instance | BindingFlags.NonPublic);
            if (property is null || property.GetValue(body, null) is not null)
                throw new NotSupportedException("A compiled declaration requires unbound native body metadata.");
        }
    }
}
