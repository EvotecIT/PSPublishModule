using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    private static IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> RefineShortCircuitRightOperandSymbols(
        BinaryExpressionAst expression,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols)
    {
        var operation = expression.Operator.ToString();
        if (operation is not ("And" or "Or") ||
            UnwrapExpression(expression.Left) is not BinaryExpressionAst predicate ||
            !RightOperandRequiresNonNull(operation, predicate.Operator.ToString()) ||
            !TryGetNullComparedVariable(predicate, out var variable, out var variableIsLeft) ||
            !symbols.TryGetValue(variable.VariablePath.UserPath, out var binding) ||
            binding.Type.ClrType.IsValueType ||
            !PowerShellNullComparisonSemanticPolicy.IsScalar(binding.Type.ClrType, variableIsLeft))
            return symbols;

        var refined = CloneSymbols(symbols);
        refined[variable.VariablePath.UserPath].Refine(binding.Type, PowerShellValueState.Known);
        return refined;
    }

    private static bool RightOperandRequiresNonNull(string logicalOperation, string comparisonOperation)
        => logicalOperation == "And"
            ? comparisonOperation is "Ine" or "Cne"
            : comparisonOperation is "Ieq" or "Ceq";

    private static bool TryGetNullComparedVariable(
        BinaryExpressionAst predicate,
        out VariableExpressionAst variable,
        out bool variableIsLeft)
    {
        var left = UnwrapExpression(predicate.Left);
        var right = UnwrapExpression(predicate.Right);
        if (IsNullVariable(left) && right is VariableExpressionAst rightVariable && !IsNullVariable(rightVariable))
        {
            variable = rightVariable;
            variableIsLeft = false;
            return true;
        }
        if (IsNullVariable(right) && left is VariableExpressionAst leftVariable && !IsNullVariable(leftVariable))
        {
            variable = leftVariable;
            variableIsLeft = true;
            return true;
        }

        variable = null!;
        variableIsLeft = false;
        return false;
    }

    private static bool IsNullVariable(Ast syntax)
        => syntax is VariableExpressionAst variable &&
           variable.VariablePath.UserPath.Equals("null", StringComparison.OrdinalIgnoreCase);
}
