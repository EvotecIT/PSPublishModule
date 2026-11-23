using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Describes an output-free Int32 reduction whose entire lifecycle can run after pure upstream filters.
/// Local parameter and body binding still use the ordinary collection ABI.
/// </summary>
internal sealed class PowerShellStrictPipelineReduction
{
    private sealed record Update(PowerShellBoundBinaryOperator Operation, int? Literal);
    private readonly PowerShellInt32Range _initial;
    private readonly Update[] _updates;

    private PowerShellStrictPipelineReduction(int initial, Update[] updates)
    {
        _initial = new(initial, initial);
        _updates = updates;
    }

    internal static PowerShellStrictPipelineReduction? TryDescribe(ScriptBlockAst body, PowerShellLocalCallParameter input)
    {
        if (input.Type != typeof(int) ||
            body.BeginBlock is not { Statements.Count: 1 } begin ||
            begin.Statements[0] is not AssignmentStatementAst
            {
                Operator: TokenKind.Equals,
                Left: ConvertExpressionAst { StaticType: { } accumulatorType, Child: VariableExpressionAst accumulator },
                Right: { } initial
            } ||
            accumulatorType != typeof(int) ||
            !accumulator.VariablePath.IsUnqualified ||
            IsVariable(accumulator, input.Symbol.Name) ||
            GetPureExpression(initial) is not ConstantExpressionAst { Value: int start } ||
            body.ProcessBlock is not { Statements.Count: > 0 } process ||
            body.EndBlock is not { Statements.Count: 1 } end ||
            !IsVariable(GetPureExpression(end.Statements[0] is ReturnStatementAst returned
                ? returned.Pipeline : end.Statements[0]), accumulator.VariablePath.UserPath))
            return null;

        var updates = new List<Update>();
        foreach (var statement in process.Statements)
        {
            if (statement is not AssignmentStatementAst assignment ||
                !IsVariable(assignment.Left, accumulator.VariablePath.UserPath))
                return null;
            PowerShellBoundBinaryOperator? boundOperator = assignment.Operator switch
            {
                TokenKind.PlusEquals => PowerShellBoundBinaryOperator.Add,
                TokenKind.MinusEquals => PowerShellBoundBinaryOperator.Subtract,
                TokenKind.MultiplyEquals => PowerShellBoundBinaryOperator.Multiply,
                _ => null
            };
            if (boundOperator is null) return null;
            var operand = GetPureExpression(assignment.Right);
            if (operand is ConstantExpressionAst { Value: int literal })
                updates.Add(new(boundOperator.Value, literal));
            else if (IsVariable(operand, input.Symbol.Name))
                updates.Add(new(boundOperator.Value, null));
            else
                return null;
        }
        return new(start, updates.ToArray());
    }

    internal bool IsTotal(PowerShellInt32Range recordRange, int maximumRecords)
    {
        var current = _initial;
        // Each prefix could be the complete result after filtering. Check every
        // intermediate operation, including consecutive writes in one process block.
        for (var index = 0; index < maximumRecords; index++)
        {
            var previous = current;
            foreach (var update in _updates)
            {
                var operand = update.Literal is { } literal ? new PowerShellInt32Range(literal, literal) : recordRange;
                if (PowerShellInt32RangePolicy.Apply(update.Operation, current, operand) is not { } next)
                    return false;
                current = next;
            }
            if (current.Minimum == previous.Minimum && current.Maximum == previous.Maximum)
                break;
        }
        return true;
    }

    private static ExpressionAst? GetPureExpression(StatementAst? statement)
        => statement switch
        {
            CommandExpressionAst { Redirections.Count: 0 } command => command.Expression,
            PipelineAst { PipelineElements.Count: 1 } pipeline
                when !PowerShellCommandRegionSemanticBinder.IsBackground(pipeline) && pipeline.PipelineElements[0] is CommandExpressionAst { Redirections.Count: 0 } command => command.Expression,
            _ => null
        };

    private static bool IsVariable(Ast? syntax, string name)
        => syntax is VariableExpressionAst { VariablePath.IsUnqualified: true } variable &&
           variable.VariablePath.UserPath.Equals(name, StringComparison.OrdinalIgnoreCase);
}
