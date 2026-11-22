namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Management.Automation.Language;

    public sealed partial class PowerShellNativeFunctionContext
    {
        internal static bool CanGuardFileSystemPathOperand(Ast command, Ast region)
        {
            for (var node = command; node is not null; node = node.Parent)
            {
                if (ReferenceEquals(node, region)) return true;
                if (!IsGuardContainer(node)) return false;
            }
            return false;
        }

        private static bool IsGuardContainer(Ast node)
            => node switch
            {
                CommandAst or CommandParameterAst or CommandExpressionAst or AssignmentStatementAst or
                    BinaryExpressionAst or ParenExpressionAst or ArrayExpressionAst or ScriptBlockExpressionAst => true,
                PipelineAst pipeline => pipeline.GetType().GetProperty("Background")?.GetValue(pipeline) is not true,
                StatementBlockAst block => block.Traps is null || block.Traps.Count == 0,
                NamedBlockAst block => block.Unnamed && (block.Traps is null || block.Traps.Count == 0),
                ScriptBlockAst script => IsGuardedScriptBlock(script),
                MemberExpressionAst member => member.GetType() == typeof(MemberExpressionAst) &&
                    member.GetType().GetProperty("NullConditional")?.GetValue(member) is not true,
                _ => false
            };

        private static bool IsGuardedScriptBlock(ScriptBlockAst ast)
            => ast.ParamBlock is null && ast.BeginBlock is null && ast.ProcessBlock is null &&
               ast.DynamicParamBlock is null && ast.EndBlock is { Unnamed: true } &&
               (ast.EndBlock.Traps is null || ast.EndBlock.Traps.Count == 0) &&
               ast.UsingStatements.Count == 0 && ast.Attributes.Count == 0 && ast.ScriptRequirements is null &&
               ast.GetType().GetProperty("CleanBlock")?.GetValue(ast) is null;

        private static ScriptBlockAst GuardFileSystemPathOperands(ScriptBlockAst ast, int[] spans)
            => new FileSystemPathOperandRewriter(spans).RewriteRegion(ast);

        private sealed class FileSystemPathOperandRewriter
        {
            private readonly HashSet<(int Start, int End)> _remaining = new HashSet<(int Start, int End)>();

            internal FileSystemPathOperandRewriter(int[] spans)
            {
                if (spans.Length % 2 != 0) throw new ArgumentException("Path admission spans must be start/end pairs.", nameof(spans));
                for (var index = 0; index < spans.Length; index += 2)
                {
                    if (spans[index] < 0 || spans[index + 1] < spans[index] ||
                        !_remaining.Add((spans[index], spans[index + 1])))
                        throw new ArgumentException("Path admission spans must be distinct authored operand extents or command-start markers.", nameof(spans));
                }
            }

            internal ScriptBlockAst RewriteRegion(ScriptBlockAst ast)
            {
                if (ast.EndBlock!.Statements.Any(statement => statement is not PipelineAst))
                    throw new ArgumentException("A path guard requires a selected native pipeline.", nameof(ast));
                var rewritten = RewriteScriptBlock(ast);
                if (_remaining.Count != 0)
                    throw new ArgumentException("A selected provider operand is outside the guarded native pipeline shape.", nameof(ast));
                return rewritten;
            }

            private T Rewrite<T>(T node) where T : Ast
            {
                if (!_remaining.Any(span => span.Start >= node.Extent.StartOffset && span.End <= node.Extent.EndOffset))
                    return (T)node.Copy();
                if (!IsGuardContainer(node))
                    throw new ArgumentException("The provider operand crosses a native syntax boundary that cannot be guarded.", nameof(node));

                Ast rewritten = node switch
                {
                    PipelineAst pipeline => RewritePipeline(pipeline),
                    CommandAst command => new CommandAst(command.Extent, command.CommandElements.Select(RewriteCommandArgument),
                        command.InvocationOperator, CopyRedirections(command.Redirections)),
                    CommandExpressionAst command => new CommandExpressionAst(command.Extent, Rewrite(command.Expression),
                        CopyRedirections(command.Redirections)),
                    AssignmentStatementAst assignment => new AssignmentStatementAst(assignment.Extent, Rewrite(assignment.Left),
                        assignment.Operator, Rewrite(assignment.Right), assignment.ErrorPosition),
                    ScriptBlockExpressionAst callback => new ScriptBlockExpressionAst(callback.Extent, RewriteScriptBlock(callback.ScriptBlock)),
                    ParenExpressionAst paren => new ParenExpressionAst(paren.Extent, Rewrite(paren.Pipeline)),
                    ArrayExpressionAst array when IsGuardContainer(array.SubExpression) => new ArrayExpressionAst(array.Extent, RewriteStatements(array.SubExpression.Extent, array.SubExpression.Statements)),
                    BinaryExpressionAst binary => new BinaryExpressionAst(binary.Extent, Rewrite(binary.Left), binary.Operator,
                        Rewrite(binary.Right), binary.ErrorPosition),
                    MemberExpressionAst member => new MemberExpressionAst(member.Extent, Rewrite(member.Expression),
                        Rewrite(member.Member), member.Static),
                    _ => throw new ArgumentException("The provider operand crosses an unsupported native syntax boundary.", nameof(node))
                };
                return (T)rewritten;
            }

            private ScriptBlockAst RewriteScriptBlock(ScriptBlockAst ast)
            {
                if (!IsGuardedScriptBlock(ast))
                    throw new ArgumentException("A guarded native callback requires an ordinary unnamed statement block.", nameof(ast));
                return new ScriptBlockAst(ast.Extent, null, RewriteStatements(ast.EndBlock!.Extent, ast.EndBlock.Statements), false);
            }

            private StatementBlockAst RewriteStatements(IScriptExtent extent, IEnumerable<StatementAst> statements)
                => new StatementBlockAst(extent, statements.Select(statement => Rewrite(statement)), null);

            private static IEnumerable<RedirectionAst> CopyRedirections(IEnumerable<RedirectionAst> redirections)
                => redirections.Select(redirection => (RedirectionAst)redirection.Copy());

            private PipelineAst RewritePipeline(PipelineAst pipeline)
            {
                CommandAst? implicitRead = null;
                var commands = new List<CommandBaseAst>();
                foreach (var element in pipeline.PipelineElements)
                {
                    if (element is CommandAst command && _remaining.Remove((command.Extent.StartOffset, command.Extent.StartOffset)))
                    {
                        if (pipeline.PipelineElements.Count != 1 || command.CommandElements.Count != 1 ||
                            command.InvocationOperator != TokenKind.Unknown || command.Redirections.Count != 0)
                            throw new ArgumentException("An implicit path admission requires a bare standalone command.", nameof(pipeline));
                        implicitRead = command;
                    }
                    commands.Add(Rewrite(element));
                }
                var copied = new PipelineAst(pipeline.Extent, commands);
                return implicitRead is null ? copied : GuardImplicitCurrentLocation(copied, implicitRead.Extent);
            }

            private CommandElementAst RewriteCommandArgument(CommandElementAst argument)
                => argument switch
                {
                    CommandParameterAst { Argument: not null } parameter
                        => new CommandParameterAst(parameter.Extent, parameter.ParameterName,
                            RewritePathExpression(parameter.Argument), parameter.ErrorPosition),
                    ExpressionAst expression => RewritePathExpression(expression),
                    _ => (CommandElementAst)argument.Copy()
                };

            private ExpressionAst RewritePathExpression(ExpressionAst expression)
                => _remaining.Remove((expression.Extent.StartOffset, expression.Extent.EndOffset))
                    ? WrapFileSystemPathOperand(expression)
                    : Rewrite(expression);
        }

        private static ExpressionAst WrapFileSystemPathOperand(ExpressionAst operand)
        {
            var context = new VariableExpressionAst(operand.Extent, "ExecutionContext", false);
            var session = new MemberExpressionAst(operand.Extent, context,
                new StringConstantExpressionAst(operand.Extent, "SessionState", StringConstantType.BareWord), false);
            var call = new InvokeMemberExpressionAst(operand.Extent,
                new TypeExpressionAst(operand.Extent, new TypeName(operand.Extent,
                    "PowerForge.Generated.Runtime.PowerShellNativeFunctionFileSystemPathGuard")),
                new StringConstantExpressionAst(operand.Extent, "Validate", StringConstantType.BareWord),
                new ExpressionAst[] { (ExpressionAst)operand.Copy(), session }, true);
            return new ParenExpressionAst(operand.Extent,
                new PipelineAst(operand.Extent, new[] { new CommandExpressionAst(operand.Extent, call, null) }));
        }

        private static PipelineAst GuardImplicitCurrentLocation(PipelineAst authoredPipeline, IScriptExtent extent)
        {
            var context = new VariableExpressionAst(extent, "ExecutionContext", false);
            var session = new MemberExpressionAst(extent, context,
                new StringConstantExpressionAst(extent, "SessionState", StringConstantType.BareWord), false);
            var admission = new InvokeMemberExpressionAst(extent,
                new TypeExpressionAst(extent, new TypeName(extent,
                    "PowerForge.Generated.Runtime.PowerShellNativeFunctionFileSystemPathGuard")),
                new StringConstantExpressionAst(extent, "ValidateCurrentLocation", StringConstantType.BareWord),
                new ExpressionAst[] { session }, true);
            var guard = new PipelineAst(extent, new[] { new CommandExpressionAst(extent, admission, null) });
            // A subexpression has no new invocation scope or command arguments. The
            // original command node keeps its live lookup, InvocationName and extent.
            var block = new StatementBlockAst(extent, new StatementAst[] { guard, authoredPipeline }, null);
            var expression = new SubExpressionAst(extent, block);
            return new PipelineAst(authoredPipeline.Extent, new[] { new CommandExpressionAst(extent, expression, null) });
        }
    }
}
