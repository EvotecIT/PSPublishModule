using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellCSharpMethodEmitter
{
    private string EmitBinary(BinaryExpressionAst binary)
    {
        var leftType = InferExpressionType(binary.Left);
        var rightType = InferExpressionType(binary.Right);
        var left = EmitExpression(binary.Left);
        var right = EmitExpression(binary.Right);
        var operation = binary.Operator.ToString();
        if (operation is "Isplit" or "Csplit")
        {
            if (leftType != typeof(string) || rightType != typeof(string))
                throw Error(binary, $"Operator '-{operation.ToLowerInvariant()}' currently requires scalar String operands.");
            var options = operation == "Isplit"
                ? "global::System.Text.RegularExpressions.RegexOptions.IgnoreCase"
                : "global::System.Text.RegularExpressions.RegexOptions.None";
            return $"global::System.Text.RegularExpressions.Regex.Split(({left} ?? string.Empty), ({right} ?? string.Empty), {options})";
        }
        if (operation == "Join")
        {
            if (leftType != typeof(string[]) || rightType != typeof(string))
                throw Error(binary, "Operator '-join' currently requires a String array and scalar String separator.");
            return $"global::System.String.Join(({right} ?? string.Empty), ({left} ?? global::System.Array.Empty<string>()))";
        }
        if (operation is "And" or "Or" && (leftType != typeof(bool) || rightType != typeof(bool)))
            throw Error(binary, $"Operator '-{operation.ToLowerInvariant()}' requires Boolean operands on the typed compilation path.");
        if (operation is "Plus" or "Minus" or "Multiply" or "Divide" or "Rem")
        {
            if (operation == "Plus" && leftType == typeof(string) && rightType == typeof(string))
            {
                // C# and constrained PowerShell string concatenation agree for this narrow case.
            }
            else if (!IsNumeric(leftType) || !IsNumeric(rightType))
            {
                throw Error(binary, $"Arithmetic operator '{binary.Operator}' requires two numeric operands of known compatible types.");
            }
            else
            {
                if ((leftType == typeof(decimal)) != (rightType == typeof(decimal)))
                    throw Error(binary, "Mixed decimal and non-decimal arithmetic relies on PowerShell coercion and is not supported.");
                if (operation == "Divide" && IsIntegral(leftType) && IsIntegral(rightType))
                    throw Error(binary, "PowerShell integral division changes runtime result type based on the quotient and is not supported by one static CLR return type.");
                if (operation != "Divide" && IsIntegral(leftType) && IsIntegral(rightType))
                    throw Error(binary, "Unconstrained integral arithmetic can promote on overflow in PowerShell; use an explicitly typed accumulator with compound assignment.");
            }
        }
        if (operation is "Ieq" or "Ceq" or "Ine" or "Cne" or "Ilt" or "Clt" or "Ile" or "Cle" or "Igt" or "Cgt" or "Ige" or "Cge")
        {
            if (leftType.IsArray || rightType.IsArray)
                throw Error(binary, "PowerShell array comparison is element-wise and is not supported by the scalar typed compiler.");
            if (IsNullExpression(binary.Left) && IsNonNullableValueType(rightType) ||
                IsNullExpression(binary.Right) && IsNonNullableValueType(leftType))
                throw Error(binary, "Comparing a non-nullable CLR value to $null requires PowerShell runtime semantics.");
            if (leftType != rightType && !IsNullExpression(binary.Left) && !IsNullExpression(binary.Right))
                throw Error(binary, "Comparison operands must have the same static CLR type on the conservative compilation path.");
        }
        if (operation is "Ieq" or "Ceq" or "Ine" or "Cne" &&
            leftType == rightType && leftType != typeof(string) &&
            !IsNullExpression(binary.Left) && !IsNullExpression(binary.Right) &&
            !PowerShellCSharpOperatorPolicy.SupportsEquality(leftType))
            throw Error(binary, $"Equality comparison for CLR type '{leftType.FullName}' has no supported static CLR equality operator.");
        if ((operation is "Ieq" or "Ine") && leftType == typeof(string) && rightType == typeof(string))
        {
            var comparison = $"global::System.String.Equals({left}, {right}, global::System.StringComparison.InvariantCultureIgnoreCase)";
            return operation == "Ine" ? $"!({comparison})" : comparison;
        }
        if ((operation is "Ceq" or "Cne") && leftType == typeof(string) && rightType == typeof(string))
        {
            var comparison = $"global::System.String.Equals({left}, {right}, global::System.StringComparison.InvariantCulture)";
            return operation == "Cne" ? $"!({comparison})" : comparison;
        }
        if ((operation is "Ilt" or "Clt" or "Ile" or "Cle" or "Igt" or "Cgt" or "Ige" or "Cge") &&
            leftType == typeof(string) && rightType == typeof(string))
            throw Error(binary, "PowerShell string relational comparison uses culture-aware runtime semantics and is not supported by the typed compiler.");
        if (operation is "Ilt" or "Clt" or "Ile" or "Cle" or "Igt" or "Cgt" or "Ige" or "Cge" &&
            !(IsNumeric(leftType) && IsNumeric(rightType)) &&
            !(leftType == typeof(char) && rightType == typeof(char)))
            throw Error(binary, $"Relational comparison for CLR type '{leftType.FullName}' is not supported by the conservative compiler.");

        var symbol = operation switch
        {
            "Plus" => "+", "Minus" => "-", "Multiply" => "*", "Divide" => "/", "Rem" => "%",
            "Ieq" or "Ceq" => "==", "Ine" or "Cne" => "!=",
            "Ilt" or "Clt" => "<", "Ile" or "Cle" => "<=", "Igt" or "Cgt" => ">", "Ige" or "Cge" => ">=",
            "And" => "&&", "Or" => "||",
            _ => throw Error(binary, $"Binary operator '{binary.Operator}' is not implemented.")
        };
        if (operation is "Divide" or "Rem" && InferBinaryType(binary) == typeof(double))
        {
            left = $"((double)({left}))";
            right = $"((double)({right}))";
        }
        return $"({left} {symbol} {right})";
    }

    private string EmitUnary(UnaryExpressionAst unary)
    {
        var child = EmitExpression(unary.Child);
        var operation = unary.TokenKind.ToString();
        var childType = InferExpressionType(unary.Child);
        if ((operation is "Not" or "Exclaim") && childType != typeof(bool))
            throw Error(unary, "Typed logical negation requires a Boolean operand.");
        if ((operation is "Plus" or "Minus") &&
            (!IsNumeric(childType) || IsIntegral(childType) && !IsSafeIntegralIndexLiteral(unary)))
            throw Error(unary, "Integral unary arithmetic can promote dynamically in PowerShell and is not supported.");
        if (operation is "PlusPlus" or "MinusMinus" or "PostfixPlusPlus" or "PostfixMinusMinus")
        {
            var variable = UnwrapExpression(unary.Child) as VariableExpressionAst
                ?? throw Error(unary, "Increment and decrement require a statically typed local variable.");
            if (!PowerShellCSharpOperatorPolicy.SupportsIncrement(childType))
                throw Error(unary, $"Increment and decrement are not defined for CLR type '{childType.FullName}' on the conservative compilation path.");
            if (!_explicitlyTypedVariables.Contains(variable.VariablePath.UserPath))
                throw Error(unary, $"Increment or decrement of untyped local '${variable.VariablePath.UserPath}' can promote dynamically in PowerShell.");
        }
        return operation switch
        {
            "Plus" => $"(+{child})",
            "Minus" => $"(-{child})",
            "Not" or "Exclaim" => $"(!{child})",
            "PlusPlus" => $"++{child}",
            "MinusMinus" => $"--{child}",
            "PostfixPlusPlus" => $"{child}++",
            "PostfixMinusMinus" => $"{child}--",
            _ => throw Error(unary, $"Unary operator '{unary.TokenKind}' is not implemented.")
        };
    }

    private static bool IsSafeIntegralIndexLiteral(UnaryExpressionAst unary)
        => unary.Child is ConstantExpressionAst &&
           unary.Parent is IndexExpressionAst index &&
           ReferenceEquals(index.Index, unary);

    private static bool IsIncrementOrDecrement(UnaryExpressionAst unary)
        => unary.TokenKind.ToString() is "PlusPlus" or "MinusMinus" or "PostfixPlusPlus" or "PostfixMinusMinus";

    private Type InferBinaryType(BinaryExpressionAst binary)
    {
        var operation = binary.Operator.ToString();
        if (operation is "Isplit" or "Csplit")
            return typeof(string[]);
        if (operation == "Join")
            return typeof(string);
        if (operation is "Ieq" or "Ceq" or "Ine" or "Cne" or "Ilt" or "Clt" or "Ile" or "Cle" or "Igt" or "Cgt" or "Ige" or "Cge" or "And" or "Or")
            return typeof(bool);
        if (operation is "Divide" or "Rem")
        {
            var left = InferExpressionType(binary.Left);
            var right = InferExpressionType(binary.Right);
            if (left == typeof(decimal) && right == typeof(decimal)) return typeof(decimal);
            return typeof(double);
        }
        return UnifyTypes(InferExpressionType(binary.Left), InferExpressionType(binary.Right), binary);
    }

    private static bool IsNumeric(Type type)
        => type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) ||
           type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong) ||
           type == typeof(float) || type == typeof(double) || type == typeof(decimal);

    private static bool IsIntegral(Type type)
        => type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) ||
           type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong);
}
