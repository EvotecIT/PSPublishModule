using System.Management.Automation.Language;

namespace PowerForge;

internal static partial class PowerShellCompilationSourcePathPlanner
{
    /// <summary>Finds a staged child script candidate. The original call operand still requires verification at execution.</summary>
    internal static PowerShellCompilationSourcePathFact? PlanScriptCall(CommandAst command)
    {
        if (command.InvocationOperator != TokenKind.Ampersand ||
            !IsFileBodyCommand(command) ||
            command.CommandElements.FirstOrDefault() is not { } operand)
            return null;

        var producer = FindDominatingAssignment(operand);
        var path = operand switch
        {
            _ when TryReadScriptRootLiteral(operand, out var literal) => literal,
            ParenExpressionAst expression => ReadScriptCallJoin(expression.Pipeline),
            _ when producer is not null => ReadScriptCallJoin(producer.Right),
            _ => null
        };
        if (string.IsNullOrWhiteSpace(path) || path!.IndexOfAny(new[] { '\0', ':' }) >= 0 ||
            path.StartsWith("/", StringComparison.Ordinal) || path.StartsWith("\\", StringComparison.Ordinal) ||
            !path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
            return null;
        return new PowerShellCompilationSourcePathFact(operand, path, producer);
    }

    private static bool IsFileBodyCommand(CommandAst command)
    {
        // The authored file owns these operands. A lowered function command region cannot use the file rewrite.
        for (Ast? node = command.Parent; node is not null && node is not ScriptBlockAst; node = node.Parent)
        {
            if (node is NamedBlockAst named)
                return named.Unnamed && named.Parent is ScriptBlockAst { Parent: null };
        }
        return false;
    }

    private static string? ReadScriptCallJoin(StatementAst statement)
    {
        if (ReadSingleCommand(statement, 3) is not { } join || !IsJoinPathCommand(join) ||
            join.CommandElements[2] is not StringConstantExpressionAst child)
            return null;
        var receiver = join.CommandElements[1];
        return receiver is VariableExpressionAst variable && IsScriptRoot(variable) ||
               IsScriptDirectoryAssignment(FindDominatingAssignment(receiver))
            ? child.Value
            : null;
    }

    private static bool IsScriptDirectoryAssignment(AssignmentStatementAst? assignment)
    {
        if (assignment?.Parent is not NamedBlockAst { Parent: ScriptBlockAst { Parent: null } } ||
            ReadSingleCommand(assignment.Right, 3) is not { } split ||
            split.CommandElements[1] is not CommandParameterAst { Argument: null } parent ||
            !parent.ParameterName.Equals("Parent", StringComparison.OrdinalIgnoreCase) ||
            !IsMember(split.CommandElements[2], "Path", out var command) ||
            !IsMember(command, "MyCommand", out var invocation) ||
            invocation is not VariableExpressionAst variable || !variable.VariablePath.IsUnqualified ||
            !variable.VariablePath.UserPath.Equals("MyInvocation", StringComparison.OrdinalIgnoreCase))
            return false;
        var name = split.GetCommandName();
        return string.Equals(name, "Split-Path", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Microsoft.PowerShell.Management\\Split-Path", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMember(CommandElementAst element, string name, out ExpressionAst receiver)
    {
        if (element is MemberExpressionAst { Static: false, Member: StringConstantExpressionAst member } access &&
            access is not InvokeMemberExpressionAst && member.Value.Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            receiver = access.Expression;
            return true;
        }
        receiver = null!;
        return false;
    }

    private static CommandAst? ReadSingleCommand(StatementAst statement, int elementCount)
        => statement is PipelineAst { PipelineElements.Count: 1 } pipeline &&
           pipeline.PipelineElements[0] is CommandAst { InvocationOperator: TokenKind.Unknown, Redirections.Count: 0 } command &&
           command.CommandElements.Count == elementCount ? command : null;

    private static AssignmentStatementAst? FindDominatingAssignment(CommandElementAst operand)
    {
        if (operand is not VariableExpressionAst { Splatted: false } variable || !variable.VariablePath.IsUnqualified)
            return null;
        for (Ast child = operand; child.Parent is { } parent && child is not ScriptBlockAst; child = parent)
        {
            var statements = parent switch
            {
                NamedBlockAst named => named.Statements,
                StatementBlockAst block => block.Statements,
                _ => null
            };
            var assignment = statements?.TakeWhile(statement => !ReferenceEquals(statement, child))
                .OfType<AssignmentStatementAst>().LastOrDefault(candidate => candidate.Operator == TokenKind.Equals &&
                    candidate.Left is VariableExpressionAst target && target.VariablePath.IsUnqualified &&
                    target.VariablePath.UserPath.Equals(variable.VariablePath.UserPath, StringComparison.OrdinalIgnoreCase));
            if (assignment is not null)
                return assignment;
        }
        return null;
    }
}
