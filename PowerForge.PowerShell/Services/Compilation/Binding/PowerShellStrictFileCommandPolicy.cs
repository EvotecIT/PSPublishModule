using System.Management.Automation.Language;

namespace PowerForge;

internal static class PowerShellStrictFileCommandPolicy
{
    internal static bool TryGetResolvePath(CommandAst command, ISet<string>? localFunctions,
        out ExpressionAst operand, out bool literal)
    {
        operand = null!;
        literal = false;
        if (!IsDirectCommand(command, "Resolve-Path", "Management", localFunctions) ||
            command.Parent.Parent is not AssignmentStatementAst
            {
                Operator: TokenKind.Equals,
                Left: VariableExpressionAst { VariablePath.IsUnqualified: true }
            } || !PowerShellStrictCatchObservation.HasContainingUntypedCatch(command))
            return false;

        var args = command.CommandElements;
        ExpressionAst? value = null;
        if (args.Count == 2 && args[1] is ExpressionAst positional)
            value = positional;
        else if (args.Count is 2 or 3 && args[1] is CommandParameterAst parameter &&
                 (parameter.ParameterName.Equals("Path", StringComparison.OrdinalIgnoreCase) ||
                  parameter.ParameterName.Equals("LiteralPath", StringComparison.OrdinalIgnoreCase)))
        {
            value = args.Count == 2 ? parameter.Argument : parameter.Argument is null ? args[2] as ExpressionAst : null;
            literal = parameter.ParameterName.Equals("LiteralPath", StringComparison.OrdinalIgnoreCase);
        }
        if (value is null or VariableExpressionAst { Splatted: true }) return false;
        operand = value;
        return true;
    }

    internal static bool TryGetProgress(CommandAst command, ISet<string>? localFunctions,
        out ExpressionAst activity, out bool completed)
    {
        activity = null!;
        completed = false;
        if (!IsDirectCommand(command, "Write-Progress", "Utility", localFunctions) || command.Parent.Parent is not StatementBlockAst and not NamedBlockAst)
            return false;
        ExpressionAst? value = null;
        var args = command.CommandElements;
        for (var index = 1; index < args.Count; index++)
        {
            if (args[index] is CommandParameterAst parameter)
            {
                if (parameter.ParameterName.Equals("Completed", StringComparison.OrdinalIgnoreCase) && parameter.Argument is null && !completed)
                {
                    completed = true;
                    continue;
                }
                if (!parameter.ParameterName.Equals("Activity", StringComparison.OrdinalIgnoreCase) || value is not null)
                    return false;
                value = parameter.Argument ?? (++index < args.Count ? args[index] as ExpressionAst : null);
                if (value is null) return false;
            }
            else
            {
                if (value is not null || args[index] is not ExpressionAst positional) return false;
                value = positional;
            }
        }
        if (value is null or VariableExpressionAst { Splatted: true }) return false;
        activity = value;
        return true;
    }

    private static bool IsDirectCommand(CommandAst command, string name, string module, ISet<string>? localFunctions)
    {
        var writtenName = command.GetCommandName();
        return command.InvocationOperator == TokenKind.Unknown && command.Redirections.Count == 0 &&
               (string.Equals(writtenName, name, StringComparison.OrdinalIgnoreCase) && localFunctions?.Contains(name) != true ||
                string.Equals(writtenName, $"Microsoft.PowerShell.{module}\\{name}", StringComparison.OrdinalIgnoreCase)) &&
               command.Parent is PipelineAst { PipelineElements.Count: 1 } pipeline &&
               !PowerShellCommandRegionSemanticBinder.IsBackground(pipeline);
    }
}
