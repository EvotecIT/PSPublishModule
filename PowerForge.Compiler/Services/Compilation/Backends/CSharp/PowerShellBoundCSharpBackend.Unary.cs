namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private string EmitUnary(PowerShellLoweredUnaryExpression expression)
    {
        if (PowerShellBoundUnaryExpression.IsNative(expression.Operation))
        {
            var operation = expression.Operation switch {
                PowerShellBoundUnaryOperator.NativeIdentity => "UnaryPlus",
                PowerShellBoundUnaryOperator.NativeNegate => "Negate",
                _ => "OnesComplement"
            };
            return $"__nativeFunction.EvaluateUnary(global::System.Linq.Expressions.ExpressionType.{operation}, {EmitExpression(expression.Operand)})";
        }
        if (expression.Operation == PowerShellBoundUnaryOperator.NormalizeCommandArgument)
            return $"global::PowerForge.Generated.Runtime.PowerShellNativeLanguageOperations.NormalizeCommandArgument({EmitExpression(expression.Operand)})";
        var symbol = expression.Operation switch
        {
            PowerShellBoundUnaryOperator.Identity => "+",
            PowerShellBoundUnaryOperator.Negate => "-",
            PowerShellBoundUnaryOperator.LogicalNot => "!",
            PowerShellBoundUnaryOperator.BitwiseNot => "~",
            _ => throw new InvalidOperationException($"Lowered unary operator '{expression.Operation}' has no C# rendering owner.")
        };
        return $"({symbol}{EmitExpression(expression.Operand)})";
    }
}
