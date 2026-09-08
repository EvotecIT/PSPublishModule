namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private static string EmitNativeBinary(PowerShellLoweredBinaryExpression expression, string left, string right)
    {
        var operation = expression.Operation switch
        {
            PowerShellBoundBinaryOperator.Add => "Add",
            PowerShellBoundBinaryOperator.Subtract => "Subtract",
            PowerShellBoundBinaryOperator.Multiply => "Multiply",
            PowerShellBoundBinaryOperator.Divide => "Divide",
            PowerShellBoundBinaryOperator.Remainder => "Modulo",
            PowerShellBoundBinaryOperator.Equal => "Equal",
            PowerShellBoundBinaryOperator.NotEqual => "NotEqual",
            PowerShellBoundBinaryOperator.LessThan => "LessThan",
            PowerShellBoundBinaryOperator.LessThanOrEqual => "LessThanOrEqual",
            PowerShellBoundBinaryOperator.GreaterThan => "GreaterThan",
            PowerShellBoundBinaryOperator.GreaterThanOrEqual => "GreaterThanOrEqual",
            PowerShellBoundBinaryOperator.BitwiseAnd => "And",
            PowerShellBoundBinaryOperator.BitwiseOr => "Or",
            PowerShellBoundBinaryOperator.BitwiseExclusiveOr => "ExclusiveOr",
            PowerShellBoundBinaryOperator.ShiftLeft => "LeftShift",
            PowerShellBoundBinaryOperator.ShiftRight => "RightShift",
            _ => throw new InvalidOperationException("No native binary operation was selected for " + expression.Operation + ".")
        };
        return "__nativeFunction.EvaluateBinary(global::System.Linq.Expressions.ExpressionType." + operation + ", " +
            (expression.NativeIgnoreCase ? "true" : "false") + ", " + left + ", " + right + ")";
    }
}
