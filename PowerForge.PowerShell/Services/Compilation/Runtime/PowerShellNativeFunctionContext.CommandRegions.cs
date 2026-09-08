namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Management.Automation;
    using System.Management.Automation.Language;
    using System.Reflection;

    public sealed partial class PowerShellNativeFunctionContext
    {
        private readonly Dictionary<string, NativeCommandRegion> _commandRegions = new(StringComparer.Ordinal);

        /// <summary>Executes an explicitly hosted pipeline region in the existing native invocation.</summary>
        public void InvokeCommandRegion(string source, string file, int line, int column,
            string? sourceDocument = null, int startOffset = -1, int endOffset = -1)
            => GetCommandRegion(source, file, line, column, false, false, sourceDocument, startOffset, endOffset).Invoke(FunctionContext);

        /// <summary>Captures an authored pipeline value with its explicit partial-output-on-error contract.</summary>
        public object? CaptureCommandRegion(string source, string file, int line, int column, bool preservePartialOutput,
            string? sourceDocument = null, int startOffset = -1, int endOffset = -1)
            => GetCommandRegion(source, file, line, column, true, preservePartialOutput, sourceDocument, startOffset, endOffset).Invoke(FunctionContext);

        /// <summary>Routes preserved partial records into the enclosing compiled collector.</summary>
        public object? CaptureCommandRegion(string source, string file, int line, int column,
            bool preservePartialOutput, Action<object?> partialOutput,
            string? sourceDocument = null, int startOffset = -1, int endOffset = -1)
        {
            var region = GetCommandRegion(source, file, line, column, true, preservePartialOutput, sourceDocument, startOffset, endOffset);
            if (partialOutput is null) throw new ArgumentNullException(nameof(partialOutput));
            var contract = NativeOutputContract.Shared.Value;
            var previous = _contract.OutputPipe.GetValue(FunctionContext)!;
            var writer = new SuccessSinkWriter(partialOutput);
            var pipe = contract.CreatePipe();
            contract.ExternalWriter.SetValue(pipe, writer, null);
            PowerShellNativeFunctionHost.Invoke(contract.SetTemporaryVariableLists, previous, new[] { pipe });
            try
            {
                _contract.OutputPipe.SetValue(FunctionContext, pipe);
                return region.Invoke(FunctionContext);
            }
            finally
            {
                _contract.OutputPipe.SetValue(FunctionContext, previous);
                writer.Close();
            }
        }

        private NativeCommandRegion GetCommandRegion(string source, string file, int line, int column,
            bool capture, bool preservePartialOutput, string? sourceDocument, int startOffset, int endOffset)
        {
            EnsureActive();
            var key = file + "\0" + line + "\0" + column + "\0" + capture + "\0" + preservePartialOutput + "\0" +
                startOffset + "\0" + endOffset + "\0" + source + "\0" + sourceDocument;
            if (!_commandRegions.TryGetValue(key, out var region))
                _commandRegions.Add(key, region = NativeCommandRegion.Create(this, source, file, line, column,
                    capture, preservePartialOutput, sourceDocument, startOffset, endOffset));
            return region;
        }

        private sealed class NativeCommandRegion
        {
            private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            private readonly Func<object, object?> _body;
            private readonly IScriptExtent[] _points;
            private readonly FieldInfo _sequencePoints, _sequenceIndex;

            private NativeCommandRegion(Func<object, object?> body, IScriptExtent[] points, Type contextType)
            {
                _body = body;
                _points = points;
                _sequencePoints = contextType.GetField("_sequencePoints", Instance)!;
                _sequenceIndex = contextType.GetField("_currentSequencePointIndex", Instance)!;
            }

            internal object? Invoke(object context)
            {
                var previousPoints = _sequencePoints.GetValue(context);
                var previousIndex = _sequenceIndex.GetValue(context);
                try
                {
                    _sequencePoints.SetValue(context, _points);
                    _sequenceIndex.SetValue(context, 0);
                    return _body(context);
                }
                finally
                {
                    _sequenceIndex.SetValue(context, previousIndex);
                    _sequencePoints.SetValue(context, previousPoints);
                }
            }

            internal static NativeCommandRegion Create(PowerShellNativeFunctionContext owner,
                string source, string file, int line, int column, bool capture, bool preservePartialOutput,
                string? sourceDocument, int startOffset, int endOffset)
            {
                if (line < 1 || column < 1) throw new ArgumentOutOfRangeException(nameof(line));
                var parseText = sourceDocument ?? new string('\n', line - 1) + new string(' ', column - 1) + source;
                var parseArguments = new object[] { parseText, file, null!, null! };
                var ast = (ScriptBlockAst)PowerShellNativeFunctionHost.Invoke(
                    owner._contract.ParseInputWithFile, null, parseArguments)!;
                var errors = (ParseError[])parseArguments[3];
                if (errors.Length != 0) throw new ArgumentException(errors[0].Message, nameof(source));
                if (sourceDocument is not null)
                {
                    // The document supplies source metadata only. Detach exactly the selected pipeline;
                    // analysis and emission never receive another authored statement or function body.
                    var selected = ast.FindAll(node => node is PipelineAst or CommandBaseAst && node.Extent.StartOffset == startOffset &&
                        node.Extent.EndOffset == endOffset, searchNestedScriptBlocks: true)
                        .OrderBy(node => node is PipelineAst ? 0 : 1).FirstOrDefault();
                    if (selected is null || selected.Extent.Text != source ||
                        selected.Extent.StartLineNumber != line || selected.Extent.StartColumnNumber != column)
                        throw new ArgumentException("The native region does not match its authored pipeline span.", nameof(sourceDocument));
                    var statement = selected is PipelineAst pipeline ? (PipelineAst)pipeline.Copy() :
                        new PipelineAst(selected.Extent, new[] { (CommandBaseAst)selected.Copy() });
                    ast = new ScriptBlockAst(statement.Extent, null,
                        new StatementBlockAst(statement.Extent, new[] { statement }, null), isFilter: false);
                }
                if (ast.ParamBlock != null || ast.BeginBlock != null || ast.ProcessBlock != null ||
                    ast.DynamicParamBlock != null || ast.EndBlock == null ||
                    ast.EndBlock.Traps?.Count > 0 || ast.EndBlock.Statements.Count == 0 ||
                    ast.EndBlock.Statements.Any(statement => statement is not PipelineAst))
                    throw new ArgumentException("A native command region requires explicit pipeline statements.", nameof(source));

                var assembly = typeof(PSObject).Assembly;
                var compilerType = assembly.GetType("System.Management.Automation.Language.Compiler", true)!;
                var analysisType = assembly.GetType("System.Management.Automation.Language.VariableAnalysis", true)!;
                var functionScript = (ScriptBlock)owner.FunctionContext.GetType().GetField("_scriptBlock", Instance)!
                    .GetValue(owner.FunctionContext)!;
                var usesCmdletBinding = (bool)typeof(ScriptBlock).GetProperty("UsesCmdletBinding", Instance)!
                    .GetValue(functionScript, null)!;
                var analyze = analysisType.GetMethods(Static).Single(method => method.Name == "Analyze" && method.GetParameters().Length == 3);
                PowerShellNativeFunctionHost.Invoke(analyze, null, new object[] { ast, true, usesCmdletBinding });
                var compiler = Activator.CreateInstance(compilerType, nonPublic: true)!;
                compilerType.GetProperty("Optimize", Instance)!.SetValue(compiler, false, null);
                compilerType.GetField("_compilingScriptCmdlet", Instance)!.SetValue(compiler, usesCmdletBinding);
                compilerType.GetField("_switchTupleIndex", Instance)!.SetValue(compiler, -2);
                compilerType.GetField("_foreachTupleIndex", Instance)!.SetValue(compiler, -2);

                // Analysis keeps ordinary variables dynamic. Automatic slots are emitted against
                // this invocation's actual tuple, rather than allocating a region-local scope.
                var tupleType = owner._contract.LocalsTuple.GetValue(owner.FunctionContext)!.GetType();
                var locals = Expression.Variable(tupleType, "locals");
                compilerType.GetProperty("LocalVariablesTupleType", Instance)!.SetValue(compiler, tupleType, null);
                compilerType.GetProperty("LocalVariablesParameter", Instance)!.SetValue(compiler, locals, null);
                var nativeContext = (ParameterExpression)(compilerType.GetField("s_functionContext", Static) ??
                    compilerType.GetField("_functionContext", Static) ??
                    throw new NotSupportedException("PowerShell's native compiler context parameter is unavailable.")).GetValue(null)!;
                var execution = (ParameterExpression)(compilerType.GetField("s_executionContextParameter", Static) ??
                    compilerType.GetField("_executionContextParameter", Static) ??
                    throw new NotSupportedException("PowerShell's native compiler execution parameter is unavailable.")).GetValue(null)!;
                var expressions = new List<Expression>
                {
                    Expression.Assign(execution, Expression.Field(nativeContext, owner._contract.ExecutionContext)),
                    Expression.Assign(locals, Expression.Convert(Expression.Field(nativeContext, owner._contract.LocalsTuple), tupleType))
                };
                var temporaries = new List<ParameterExpression> { execution, locals };
                if (capture)
                {
                    if (ast.EndBlock.Statements.Count != 1)
                        throw new ArgumentException("A captured native region requires one authored pipeline.", nameof(source));
                    var captureContext = Enum.Parse(compilerType.GetNestedType("CaptureAstContext", BindingFlags.NonPublic)!,
                        preservePartialOutput ? "AssignmentWithResultPreservation" : "AssignmentWithoutResultPreservation");
                    var compile = compilerType.GetMethod("CaptureStatementResults", Instance)!;
                    var result = (Expression)PowerShellNativeFunctionHost.Invoke(compile, compiler,
                        new object[] { ast.EndBlock.Statements[0], captureContext, null! })!;
                    expressions.Add(Expression.Convert(result, typeof(object)));
                }
                else
                {
                    var compile = compilerType.GetMethod("CompileStatementListWithTraps", Instance)!;
                    PowerShellNativeFunctionHost.Invoke(compile, compiler,
                        new object[] { ast.EndBlock.Statements, ast.EndBlock.Traps!, expressions, temporaries });
                    expressions.Add(Expression.Constant(null, typeof(object)));
                }
                var lambda = Expression.Lambda(Expression.Block(typeof(object), temporaries, expressions), nativeContext);
                var context = Expression.Parameter(typeof(object), "context");
                var body = Expression.Lambda<Func<object, object?>>(
                    Expression.Invoke(lambda, Expression.Convert(context, nativeContext.Type)), context).Compile();
                var points = ((IEnumerable<IScriptExtent>)compilerType.GetField("_sequencePoints", Instance)!.GetValue(compiler)!).ToArray();
                if (points.Length == 0) points = new[] { ast.EndBlock.Statements[0].Extent };
                return new NativeCommandRegion(body, points, nativeContext.Type);
            }
        }
    }
}
