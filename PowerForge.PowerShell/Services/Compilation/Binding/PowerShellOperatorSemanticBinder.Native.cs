namespace PowerForge;

internal static partial class PowerShellOperatorSemanticBinder
{
    /// <summary>Keeps native operands on the invocation's language-operation path, including literal arithmetic.</summary>
    private static PowerShellBoundExpression? BindNativeBinary(SourceSpan span, string operation,
        PowerShellBoundExpression left, PowerShellBoundExpression right, PowerShellCompilationCapability capabilities)
    {
        if (!capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding) ||
            left.Type.ClrType == typeof(void) || right.Type.ClrType == typeof(void)) return null;
        PowerShellBoundBinaryOperator? bound = operation switch
        {
            "Plus" => PowerShellBoundBinaryOperator.Add,
            "Minus" => PowerShellBoundBinaryOperator.Subtract,
            "Multiply" => PowerShellBoundBinaryOperator.Multiply,
            "Divide" => PowerShellBoundBinaryOperator.Divide,
            "Rem" => PowerShellBoundBinaryOperator.Remainder,
            "Ieq" or "Ceq" => PowerShellBoundBinaryOperator.Equal,
            "Ine" or "Cne" => PowerShellBoundBinaryOperator.NotEqual,
            "Ilt" or "Clt" => PowerShellBoundBinaryOperator.LessThan,
            "Ile" or "Cle" => PowerShellBoundBinaryOperator.LessThanOrEqual,
            "Igt" or "Cgt" => PowerShellBoundBinaryOperator.GreaterThan,
            "Ige" or "Cge" => PowerShellBoundBinaryOperator.GreaterThanOrEqual,
            "Band" => PowerShellBoundBinaryOperator.BitwiseAnd,
            "Bor" => PowerShellBoundBinaryOperator.BitwiseOr,
            "Bxor" => PowerShellBoundBinaryOperator.BitwiseExclusiveOr,
            "Shl" => PowerShellBoundBinaryOperator.ShiftLeft,
            "Shr" => PowerShellBoundBinaryOperator.ShiftRight,
            _ => null
        };
        return bound is null ? null : new PowerShellBoundBinaryExpression(span, bound.Value, left, right,
            new PowerShellTypeFact(typeof(object), PowerShellTypeFactProvenance.Inferred,
                "The active PowerShell operation preserves its runtime scalar or collection result."),
            usesNativeInvocation: true, nativeIgnoreCase: !operation.StartsWith("C", StringComparison.Ordinal));
    }
}
