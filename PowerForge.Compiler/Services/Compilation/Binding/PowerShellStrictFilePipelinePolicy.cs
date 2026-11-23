using System.Management.Automation.Language;

namespace PowerForge;

internal static class PowerShellStrictFilePipelinePolicy
{
    internal static bool TryGetSource(CommandAst command, PowerShellCommandInvocationResolution resolution,
        out ExpressionAst path, out bool filesOnly)
    {
        path = null!;
        filesOnly = false;
        if (resolution is not { IsProvider: true, Contract.ProviderId: "powerforge.command.path-read.children" } ||
            !string.Equals(command.GetCommandName()?.Split('\\').Last(), "Get-ChildItem", StringComparison.OrdinalIgnoreCase) ||
            command.InvocationOperator != TokenKind.Unknown || command.Redirections.Count != 0 ||
            command.Parent is not PipelineAst { PipelineElements.Count: > 1 } pipeline ||
            !ReferenceEquals(pipeline.PipelineElements[0], command) ||
            PowerShellCommandRegionSemanticBinder.IsBackground(pipeline))
            return false;

        ExpressionAst? operand = null;
        var recursive = false;
        var elements = command.CommandElements;
        for (var index = 1; index < elements.Count; index++)
        {
            if (elements[index] is not CommandParameterAst parameter)
            {
                if (operand is not null || elements[index] is not ExpressionAst expression) return false;
                operand = expression;
                continue;
            }
            if (parameter.ParameterName.Equals("Recurse", StringComparison.OrdinalIgnoreCase))
            {
                if (recursive || parameter.Argument is not null) return false;
                recursive = true;
                continue;
            }
            var value = parameter.Argument;
            if (value is null)
            {
                if (++index >= elements.Count || elements[index] is not ExpressionAst next) return false;
                value = next;
            }
            if (parameter.ParameterName.Equals("Path", StringComparison.OrdinalIgnoreCase))
            {
                if (operand is not null) return false;
                operand = value;
                continue;
            }
            if (!parameter.ParameterName.Equals("Attributes", StringComparison.OrdinalIgnoreCase) ||
                filesOnly || value is not StringConstantExpressionAst attribute ||
                !attribute.Value.Equals("!Directory", StringComparison.OrdinalIgnoreCase))
                return false;
            filesOnly = true;
        }

        if (!recursive || operand is null || operand is VariableExpressionAst { Splatted: true }) return false;
        path = operand;
        return true;
    }
}
