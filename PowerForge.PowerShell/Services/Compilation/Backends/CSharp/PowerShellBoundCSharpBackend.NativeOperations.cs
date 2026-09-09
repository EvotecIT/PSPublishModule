namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private string EmitNativeBinary(PowerShellLoweredBinaryExpression expression, string left, string right)
    {
        if (expression.Operation is PowerShellBoundBinaryOperator.NativeLike or PowerShellBoundBinaryOperator.NativeNotLike or PowerShellBoundBinaryOperator.NativeSplit)
        {
            var function = _sourceFunction!;
            var source = string.Join("\n", function.SourceText.Split('\n').Skip(expression.Span.StartLine - function.Span.StartLine)
                .Take(expression.Span.EndLine - expression.Span.StartLine + 1));
            return "__nativeFunction.EvaluatePattern(" + PowerShellCSharpLiteral.QuoteString(expression.Operation.ToString()) + ", " +
                (expression.NativeIgnoreCase ? "true" : "false") + ", " + left + ", " + right + ", " +
                PowerShellCSharpLiteral.QuoteString(function.SourcePath) + ", " + expression.Span.StartLine + ", " + expression.Span.StartColumn + ", " +
                expression.Span.EndLine + ", " + expression.Span.EndColumn + ", " + PowerShellCSharpLiteral.QuoteString(source) + ")";
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
