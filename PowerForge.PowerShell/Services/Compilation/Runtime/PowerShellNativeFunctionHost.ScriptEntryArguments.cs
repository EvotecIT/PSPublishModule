namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Management.Automation;
    using System.Management.Automation.Language;
    using System.Management.Automation.Runspaces;

    public static partial class PowerShellNativeFunctionHost
    {
        /// <summary>Preserves ordered command arguments through native script dispatch without parsing argument data as source.</summary>
        internal static ScriptBlock CreateScriptEntryInvocation(IReadOnlyList<CommandParameter> arguments,
            ISet<string> switchParameters, out object?[] values)
        {
            var skeleton = Parser.ParseInput("param($Entry, [object[]]$Values) & $Entry", out _, out _);
            var extent = skeleton.Extent;
            var elements = new List<CommandElementAst> { new VariableExpressionAst(extent, "Entry", false) };
            values = new object?[arguments.Count];
            for (var index = 0; index < arguments.Count; index++)
            {
                var argument = arguments[index];
                values[index] = argument.Value;
                var value = new IndexExpressionAst(extent, new VariableExpressionAst(extent, "Values", false),
                    new ConstantExpressionAst(extent, index));
                if (argument.Name is null)
                {
                    elements.Add(value);
                    continue;
                }
                // Only an explicit switch value belongs to the parameter AST itself.
                // Attaching an ordinary value changes an unbound '-Name' into '-Name:'.
                var isSwitch = switchParameters.Contains(argument.Name);
                var parameterText = "-" + argument.Name + (isSwitch && argument.Value is not null ? ":" : string.Empty);
                var parameterExtent = new ScriptExtent(new ScriptPosition(null, 1, 1, parameterText),
                    new ScriptPosition(null, 1, parameterText.Length + 1, parameterText));
                elements.Add(new CommandParameterAst(parameterExtent, argument.Name,
                    isSwitch && argument.Value is not null ? value : null, parameterExtent));
                if (!isSwitch)
                    elements.Add(value);
            }
            var command = new CommandAst(extent, elements, TokenKind.Ampersand, Array.Empty<RedirectionAst>());
            var pipeline = new PipelineAst(extent, new CommandBaseAst[] { command });
            var body = new StatementBlockAst(extent, new StatementAst[] { pipeline }, Array.Empty<TrapStatementAst>());
            return new ScriptBlockAst(extent, (ParamBlockAst)skeleton.ParamBlock!.Copy(), body, false).GetScriptBlock();
        }
    }
}
