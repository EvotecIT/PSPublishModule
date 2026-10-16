namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Management.Automation.Language;
    using System.Reflection;

    public sealed partial class PowerShellNativeFunctionContext
    {
        private readonly Dictionary<string, NativeAstOperation> _commandRegions = new(StringComparer.Ordinal);

        /// <summary>Executes an explicitly hosted pipeline region in the existing native invocation.</summary>
        public void InvokeCommandRegion(string source, string file, int line, int column,
            string? sourceDocument = null, int startOffset = -1, int endOffset = -1)
            => InvokeCommandRegion(source, file, line, column, sourceDocument, startOffset, endOffset, null);

        /// <summary>Executes a pipeline statement and hands its final authored position to the surrounding error owner.</summary>
        /// <remarks>The completedPosition callback runs only on success, before SDK context restoration.</remarks>
        public void InvokeCommandRegion(string source, string file, int line, int column,
            string? sourceDocument, int startOffset, int endOffset, Action<IScriptExtent>? completedPosition,
            int[]? guardedPathOperands = null)
            => GetCommandRegion(source, file, line, column, false, false, sourceDocument, startOffset, endOffset,
                guardedPathOperands: guardedPathOperands)
                .Invoke(FunctionContext, completedPosition: completedPosition);

        /// <summary>Loads one planned dependency in the active scope after validating its actual operand and staged bytes.</summary>
        public void InvokeGuardedSourceLoad(string expectedRelativePath, string source, string file, int line, int column,
            string sourceDocument, int startOffset, int endOffset, Action<IScriptExtent>? completedPosition,
            int[]? guardedPathOperands = null)
            => GetCommandRegion(source, file, line, column, false, false, sourceDocument, startOffset, endOffset,
                expectedRelativePath, guardedPathOperands).Invoke(FunctionContext, completedPosition: completedPosition);

        /// <summary>Writes pipeline records directly into an enclosing collector without scalarizing them.</summary>
        public void InvokeCommandRegion(string source, string file, int line, int column, Action<object?> output,
            string? sourceDocument = null, int startOffset = -1, int endOffset = -1)
            => InvokeCommandRegion(source, file, line, column, output, sourceDocument, startOffset, endOffset, null);

        /// <summary>Writes pipeline records and preserves the statement's last authored position for its enclosing operation.</summary>
        /// <remarks>The completedPosition callback runs only on success, before SDK context restoration.</remarks>
        public void InvokeCommandRegion(string source, string file, int line, int column, Action<object?> output,
            string? sourceDocument, int startOffset, int endOffset, Action<IScriptExtent>? completedPosition,
            int[]? guardedPathOperands = null)
        {
            using (RedirectOutput(output))
                InvokeCommandRegion(source, file, line, column, sourceDocument, startOffset, endOffset, completedPosition, guardedPathOperands);
        }

        /// <summary>Captures an authored pipeline value with its explicit partial-output-on-error contract.</summary>
        public object? CaptureCommandRegion(string source, string file, int line, int column, bool preservePartialOutput,
            string? sourceDocument = null, int startOffset = -1, int endOffset = -1, int[]? guardedPathOperands = null)
            => GetCommandRegion(source, file, line, column, true, preservePartialOutput, sourceDocument, startOffset, endOffset,
                guardedPathOperands: guardedPathOperands).Invoke(FunctionContext);

        /// <summary>Routes preserved partial records into the enclosing compiled collector.</summary>
        public object? CaptureCommandRegion(string source, string file, int line, int column,
            bool preservePartialOutput, Action<object?> partialOutput,
            string? sourceDocument = null, int startOffset = -1, int endOffset = -1, int[]? guardedPathOperands = null)
        {
            var region = GetCommandRegion(source, file, line, column, true, preservePartialOutput, sourceDocument, startOffset, endOffset,
                guardedPathOperands: guardedPathOperands);
            using (RedirectOutput(partialOutput))
                return region.Invoke(FunctionContext);
        }

        private NativeAstOperation GetCommandRegion(string source, string file, int line, int column,
            bool capture, bool preservePartialOutput, string? sourceDocument, int startOffset, int endOffset,
            string? guardedDotSourceRelativePath = null, int[]? guardedPathOperands = null)
        {
            EnsureActive();
            var key = file + "\0" + line + "\0" + column + "\0" + capture + "\0" + preservePartialOutput + "\0" +
                startOffset + "\0" + endOffset + "\0" + source + "\0" + sourceDocument + "\0" + guardedDotSourceRelativePath + "\0" +
                string.Join(",", guardedPathOperands ?? Array.Empty<int>());
            if (!_commandRegions.TryGetValue(key, out var region))
                _commandRegions.Add(key, region = NativeCommandRegion.Create(this, source, file, line, column,
                    capture, preservePartialOutput, sourceDocument, startOffset, endOffset, guardedDotSourceRelativePath, guardedPathOperands));
            return region;
        }

        private sealed class NativeCommandRegion
        {
            internal static NativeAstOperation Create(PowerShellNativeFunctionContext owner,
                string source, string file, int line, int column, bool capture, bool preservePartialOutput,
                string? sourceDocument, int startOffset, int endOffset, string? guardedDotSourceRelativePath, int[]? guardedPathOperands)
            {
                if (line < 1 || column < 1) throw new ArgumentOutOfRangeException(nameof(line));
                var parseText = sourceDocument ?? new string('\n', line - 1) + new string(' ', column - 1) + source;
                var parseArguments = new object[] { parseText, file, null!, null! };
                var ast = (ScriptBlockAst)PowerShellNativeFunctionHost.Invoke(
                    owner._contract.ParseInputWithFile, null, parseArguments)!;
                var errors = (ParseError[])parseArguments[3];
                ValidateSelectedSyntax(errors, source, nameof(source));
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

                if (guardedDotSourceRelativePath is not null)
                    ast = GuardSourceLoad(ast, guardedDotSourceRelativePath);
                if (guardedPathOperands is { Length: > 0 })
                    ast = GuardFileSystemPathOperands(ast, guardedPathOperands);

                // Statement regions advance the surrounding source position.
                // Expression captures keep their caller's existing boundary.
                var native = new NativeAstCompiler(owner, ast, preserveSelectedSequencePoints: !capture);
                var expressions = native.Expressions;
                var temporaries = native.Temporaries;
                var compilerType = native.CompilerType;
                if (capture)
                {
                    if (ast.EndBlock.Statements.Count != 1)
                        throw new ArgumentException("A captured native region requires one authored pipeline.", nameof(source));
                    var captureContext = Enum.Parse(compilerType.GetNestedType("CaptureAstContext", BindingFlags.NonPublic)!,
                        preservePartialOutput ? "AssignmentWithResultPreservation" : "AssignmentWithoutResultPreservation");
                    var result = (Expression)native.Invoke("CaptureStatementResults",
                        ast.EndBlock.Statements[0], captureContext, null!)!;
                    expressions.Add(Expression.Convert(result, typeof(object)));
                }
                else
                {
                    native.Invoke("CompileStatementListWithTraps",
                        ast.EndBlock.Statements, ast.EndBlock.Traps!, expressions, temporaries);
                    expressions.Add(Expression.Constant(null, typeof(object)));
                }
                return native.Compile();
            }

            private static ScriptBlockAst GuardSourceLoad(ScriptBlockAst ast, string expectedRelativePath)
            {
                if (ast.EndBlock!.Statements.Count != 1 ||
                    ast.EndBlock.Statements[0] is not PipelineAst { PipelineElements.Count: 1 } pipeline ||
                    pipeline.PipelineElements[0] is not CommandAst { InvocationOperator: TokenKind.Dot } command ||
                    command.CommandElements[0] is not ExpressionAst operand || command.Redirections.Count != 0)
                    throw new ArgumentException("A guarded source load requires one authored dot-source pipeline.", nameof(ast));
                // Only this operand changes. The SDK still evaluates it exactly once, then binds
                // the authored arguments and dot-sources in the same invocation scope.
                var guard = new InvokeMemberExpressionAst(operand.Extent,
                    new TypeExpressionAst(operand.Extent, new TypeName(operand.Extent,
                        "PowerForge.Compiled.PowerForgePackagedEntryPoint")),
                    new StringConstantExpressionAst(operand.Extent, "ValidateDotSource", StringConstantType.BareWord),
                    new ExpressionAst[] { (ExpressionAst)operand.Copy(),
                        new StringConstantExpressionAst(operand.Extent, expectedRelativePath, StringConstantType.SingleQuoted) }, true);
                var wrapped = new ParenExpressionAst(operand.Extent,
                    new PipelineAst(operand.Extent, new[] { new CommandExpressionAst(operand.Extent, guard, null) }));
                var guardedCommand = new CommandAst(command.Extent,
                    new CommandElementAst[] { wrapped }.Concat(command.CommandElements.Skip(1)
                        .Select(element => (CommandElementAst)element.Copy())), TokenKind.Dot, null);
                var guardedPipeline = new PipelineAst(pipeline.Extent, new[] { guardedCommand });
                return new ScriptBlockAst(ast.Extent, null,
                    new StatementBlockAst(ast.EndBlock.Extent, new[] { guardedPipeline }, null), false);
            }
        }
    }
}
