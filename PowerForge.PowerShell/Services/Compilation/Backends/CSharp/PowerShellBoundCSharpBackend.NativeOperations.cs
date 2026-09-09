namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private string EmitNativeBinary(PowerShellLoweredBinaryExpression expression, string left, string right)
    {
        if (expression.Operation is PowerShellBoundBinaryOperator.NativeLike or PowerShellBoundBinaryOperator.NativeNotLike or
            PowerShellBoundBinaryOperator.NativeSplit or PowerShellBoundBinaryOperator.NativeReplace)
        {
            var function = _sourceFunction!;
            var extent = expression.OperatorSpan ?? throw new InvalidOperationException("Native pattern operator extent is missing.");
            var source = expression.OperatorSourceText ?? throw new InvalidOperationException("Native pattern operator source is missing.");
            return "__nativeFunction.EvaluatePattern(" + PowerShellCSharpLiteral.QuoteString(expression.Operation.ToString()) + ", " +
                (expression.NativeIgnoreCase ? "true" : "false") + ", " + left + ", " + right + ", " +
                PowerShellCSharpLiteral.QuoteString(function.SourcePath) + ", " + extent.StartLine + ", " + extent.StartColumn + ", " +
                extent.EndLine + ", " + extent.EndColumn + ", " + PowerShellCSharpLiteral.QuoteString(source) + ")";
        }
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
