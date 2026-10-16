namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Management.Automation.Language;

    public sealed partial class PowerShellNativeFunctionContext
    {
        private static ScriptBlockAst GuardFileSystemPathOperands(ScriptBlockAst ast, int[] spans)
        {
            if (spans.Length % 2 != 0) throw new ArgumentException("Path admission spans must be start/end pairs.", nameof(spans));
            var remaining = new HashSet<(int Start, int End)>();
            for (var index = 0; index < spans.Length; index += 2)
                if (spans[index] < 0 || spans[index + 1] < spans[index] || !remaining.Add((spans[index], spans[index + 1])))
                    throw new ArgumentException("Path admission spans must be distinct authored operand extents or command-start markers.", nameof(spans));
            ExpressionAst Wrap(ExpressionAst operand)
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
            var pipelines = new List<StatementAst>();
            foreach (var statement in ast.EndBlock!.Statements)
            {
                if (statement is not PipelineAst pipeline) throw new ArgumentException("A path guard requires a selected native pipeline.", nameof(ast));
                CommandAst? implicitRead = null;
                var commands = new List<CommandBaseAst>();
                foreach (var element in pipeline.PipelineElements)
                {
                    if (element is not CommandAst command) { commands.Add((CommandBaseAst)element.Copy()); continue; }
                    if (remaining.Remove((command.Extent.StartOffset, command.Extent.StartOffset)))
                    {
                        if (pipeline.PipelineElements.Count != 1 || command.CommandElements.Count != 1 ||
                            command.InvocationOperator != TokenKind.Unknown || command.Redirections.Count != 0)
                            throw new ArgumentException("An implicit path admission requires a bare standalone command.", nameof(ast));
                        implicitRead = command;
                    }
                    var arguments = new List<CommandElementAst>();
                    foreach (var argument in command.CommandElements)
                    {
                        if (argument is CommandParameterAst { Argument: not null } parameter &&
                            remaining.Remove((parameter.Argument.Extent.StartOffset, parameter.Argument.Extent.EndOffset)))
                            arguments.Add(new CommandParameterAst(parameter.Extent, parameter.ParameterName, Wrap(parameter.Argument), parameter.ErrorPosition));
                        else if (argument is ExpressionAst expression &&
                            remaining.Remove((expression.Extent.StartOffset, expression.Extent.EndOffset)))
                            arguments.Add(Wrap(expression));
                        else arguments.Add((CommandElementAst)argument.Copy());
                    }
                    commands.Add(new CommandAst(command.Extent, arguments, command.InvocationOperator,
                        command.Redirections.Select(static redirection => (RedirectionAst)redirection.Copy())));
                }
                if (pipeline.GetType().GetProperty("Background")?.GetValue(pipeline) is true)
                    throw new ArgumentException("Background provider-path pipelines are unsupported.", nameof(ast));
                var copied = new PipelineAst(pipeline.Extent, commands);
                pipelines.Add(implicitRead is null ? copied : GuardImplicitCurrentLocation(copied, implicitRead.Extent));
            }
            if (remaining.Count != 0) throw new ArgumentException("A selected provider operand is not a direct native pipeline argument.", nameof(spans));
            return new ScriptBlockAst(ast.Extent, null, new StatementBlockAst(ast.EndBlock.Extent, pipelines, null), false);
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
