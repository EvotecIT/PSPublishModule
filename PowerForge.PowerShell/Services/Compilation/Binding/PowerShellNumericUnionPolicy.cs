using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Identifies locals whose mutation requires an exact boxed numeric representation.</summary>
internal static class PowerShellNumericUnionPolicy
{
    internal static PowerShellTypeFact Int32OrDouble { get; } = new(typeof(object),
        PowerShellTypeFactProvenance.Int32OrDouble,
        "An unconstrained Int32 local retains its boxed Int32 or promoted Double value; it has no persistent numeric constraint.");

    internal static bool RequiresPromotion(FunctionDefinitionAst function, string name,
        IReadOnlyList<AssignmentStatementAst> assignments)
    {
        var writes = assignments.Where(assignment => PowerShellAssignmentTargetPolicy.FindDirectVariable(assignment.Left)?
            .VariablePath.UserPath.Equals(name, StringComparison.OrdinalIgnoreCase) == true).ToArray();
        // A later declaration can add a persistent constraint. It cannot share the
        // unconstrained union representation without a separate constraint transition.
        if (writes.Any(assignment => assignment.Left is not VariableExpressionAst)) return false;
        return writes.Any(assignment => assignment.Operator is TokenKind.PlusEquals or TokenKind.MinusEquals or TokenKind.MultiplyEquals) ||
            function.Body.FindAll(node => node is UnaryExpressionAst
                { TokenKind: TokenKind.PlusPlus or TokenKind.MinusMinus or TokenKind.PostfixPlusPlus or TokenKind.PostfixMinusMinus,
                  Child: VariableExpressionAst variable } && variable.VariablePath.UserPath.Equals(name, StringComparison.OrdinalIgnoreCase), false).Any();
    }

    internal static bool IsNumeric(PowerShellTypeFact type)
        => type.Provenance == PowerShellTypeFactProvenance.Int32OrDouble || type.ClrType == typeof(int) || type.ClrType == typeof(double);

    internal static PowerShellBoundExpression? BindBinary(SourceSpan span, string operation,
        PowerShellBoundExpression left, PowerShellBoundExpression right)
    {
        if (left.Type.Provenance != PowerShellTypeFactProvenance.Int32OrDouble &&
            right.Type.Provenance != PowerShellTypeFactProvenance.Int32OrDouble || !IsNumeric(left.Type) || !IsNumeric(right.Type)) return null;
        var floating = left.Type.ClrType == typeof(double) || right.Type.ClrType == typeof(double);
        var bound = operation switch
        {
            "Plus" => PowerShellBoundBinaryOperator.PromotingAdd,
            "Minus" => PowerShellBoundBinaryOperator.PromotingSubtract,
            "Multiply" => PowerShellBoundBinaryOperator.PromotingMultiply,
            "Divide" when floating => PowerShellBoundBinaryOperator.NumericUnionFloatingDivide,
            "Rem" when floating => PowerShellBoundBinaryOperator.NumericUnionFloatingRemainder,
            "Ieq" or "Ceq" => PowerShellBoundBinaryOperator.NumericUnionEqual,
            "Ine" or "Cne" => PowerShellBoundBinaryOperator.NumericUnionNotEqual,
            "Ilt" or "Clt" => PowerShellBoundBinaryOperator.NumericUnionLessThan,
            "Ile" or "Cle" => PowerShellBoundBinaryOperator.NumericUnionLessThanOrEqual,
            "Igt" or "Cgt" => PowerShellBoundBinaryOperator.NumericUnionGreaterThan,
            "Ige" or "Cge" => PowerShellBoundBinaryOperator.NumericUnionGreaterThanOrEqual,
            _ => (PowerShellBoundBinaryOperator?)null
        };
        return bound is null ? null : new PowerShellBoundBinaryExpression(span, bound.Value, left, right,
            operation is "Plus" or "Minus" or "Multiply" or "Divide" or "Rem" ?
                floating ? new PowerShellTypeFact(typeof(double), PowerShellTypeFactProvenance.Inferred,
                    "A known Double operand makes closed numeric-union arithmetic produce Double.") : Int32OrDouble :
                new PowerShellTypeFact(typeof(bool), PowerShellTypeFactProvenance.Inferred, "A comparison observes the numeric value of a closed Int32/Double union."));
    }
}
