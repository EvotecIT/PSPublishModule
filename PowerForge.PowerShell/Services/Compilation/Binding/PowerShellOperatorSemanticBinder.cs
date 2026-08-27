using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Binds the conservative direct-CLR operator subset once, before lowering.</summary>
internal static class PowerShellOperatorSemanticBinder
{
    internal static PowerShellBoundExpression? BindBinary(
        BinaryExpressionAst syntax,
        SourceSpan span,
        Func<Ast, PowerShellBoundExpression?> bindOperand,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        string? targetFramework = null)
    {
        var operation = syntax.Operator.ToString();
        if (operation is "Is" or "IsNot")
            return BindTypeTest(syntax, span, operation == "IsNot", bindOperand, diagnostics, targetFramework);
        if (operation is "Match" or "Imatch" or "Cmatch" or "Notmatch" or "Inotmatch" or "Cnotmatch")
            return BindRegexMatch(syntax, span, operation, bindOperand, diagnostics);
        if (operation is "Replace" or "Ireplace" or "Creplace")
            return BindRegexReplace(syntax, span, operation, bindOperand, diagnostics);

        var left = bindOperand(syntax.Left);
        var right = bindOperand(syntax.Right);
        if (left is null || right is null) return null;
        var leftType = left.Type.ClrType;
        var rightType = right.Type.ClrType;

        if (operation is "And" or "Or")
        {
            if (leftType != typeof(bool) || rightType != typeof(bool))
                return Reject(diagnostics, span, "PSB2201", $"Operator '-{operation.ToLowerInvariant()}' requires Boolean operands on the typed compilation path.");
            return Binary(span, operation == "And" ? PowerShellBoundBinaryOperator.LogicalAnd : PowerShellBoundBinaryOperator.LogicalOr, left, right, typeof(bool));
        }

        if (operation is "Band" or "Bor" or "Bxor")
        {
            if (!PowerShellClrTypeSemantics.IsIntegral(leftType) || leftType != rightType)
                return Reject(diagnostics, span, "PSB2202", $"Operator '-{operation.ToLowerInvariant()}' requires integral operands of the same static type.");
            var bound = operation switch
            {
                "Band" => PowerShellBoundBinaryOperator.BitwiseAnd,
                "Bor" => PowerShellBoundBinaryOperator.BitwiseOr,
                _ => PowerShellBoundBinaryOperator.BitwiseExclusiveOr
            };
            return Binary(span, bound, left, right, PowerShellClrTypeSemantics.PromoteIntegral(leftType));
        }

        if (operation is "Shl" or "Shr")
        {
            if (!PowerShellClrTypeSemantics.IsIntegral(leftType) || !PowerShellClrTypeSemantics.IsIntegral(rightType))
                return Reject(diagnostics, span, "PSB2203", $"Operator '-{operation.ToLowerInvariant()}' requires integral operands.");
            return Binary(span, operation == "Shl" ? PowerShellBoundBinaryOperator.ShiftLeft : PowerShellBoundBinaryOperator.ShiftRight, left, right, PowerShellClrTypeSemantics.PromoteIntegral(leftType));
        }

        if (operation is "Plus" or "Minus" or "Multiply" or "Divide" or "Rem")
        {
            if (operation == "Plus" && leftType == typeof(string) && rightType == typeof(string))
                return Binary(span, PowerShellBoundBinaryOperator.Add, left, right, typeof(string));
            if (!PowerShellClrTypeSemantics.IsNumeric(leftType) || !PowerShellClrTypeSemantics.IsNumeric(rightType))
                return Reject(diagnostics, span, "PSB2204", $"Arithmetic operator '{syntax.Operator}' requires two numeric operands of known compatible types.");
            if ((leftType == typeof(decimal)) != (rightType == typeof(decimal)))
                return Reject(diagnostics, span, "PSB2205", "Mixed decimal and non-decimal arithmetic relies on PowerShell coercion and is not supported.");
            if (operation == "Divide" && PowerShellClrTypeSemantics.IsIntegral(leftType) && PowerShellClrTypeSemantics.IsIntegral(rightType))
                return Reject(diagnostics, span, "PSB2206", "PowerShell integral division changes runtime result type based on the quotient and is not supported by one static CLR return type.");
            if (operation != "Divide" && PowerShellClrTypeSemantics.IsIntegral(leftType) && PowerShellClrTypeSemantics.IsIntegral(rightType))
                return Reject(diagnostics, span, "PSB2207", "Unconstrained integral arithmetic can promote on overflow in PowerShell; use an explicitly typed accumulator with compound assignment.");
            var resultType = operation is "Divide" or "Rem"
                ? leftType == typeof(decimal) && rightType == typeof(decimal) ? typeof(decimal) : typeof(double)
                : TryUnify(leftType, rightType, diagnostics, span);
            if (resultType is null) return null;
            var bound = operation switch
            {
                "Plus" => PowerShellBoundBinaryOperator.Add,
                "Minus" => PowerShellBoundBinaryOperator.Subtract,
                "Multiply" => PowerShellBoundBinaryOperator.Multiply,
                "Divide" => PowerShellBoundBinaryOperator.Divide,
                _ => PowerShellBoundBinaryOperator.Remainder
            };
            return Binary(span, bound, left, right, resultType);
        }

        if (operation is "Ieq" or "Ceq" or "Ine" or "Cne" or "Ilt" or "Clt" or "Ile" or "Cle" or "Igt" or "Cgt" or "Ige" or "Cge")
        {
            if (leftType.IsArray || rightType.IsArray)
                return Reject(diagnostics, span, "PSB2208", "PowerShell array comparison is element-wise and is not supported by the scalar typed compiler.");
            if ((left.ValueState == PowerShellValueState.Null && PowerShellClrTypeSemantics.IsNonNullableValueType(rightType)) ||
                (right.ValueState == PowerShellValueState.Null && PowerShellClrTypeSemantics.IsNonNullableValueType(leftType)))
                return Reject(diagnostics, span, "PSB2209", "Comparing a non-nullable CLR value to $null requires PowerShell runtime semantics.");
            if (leftType != rightType && left.ValueState != PowerShellValueState.Null && right.ValueState != PowerShellValueState.Null)
                return Reject(diagnostics, span, "PSB2210", "Comparison operands must have the same static CLR type on the conservative compilation path.");
            var relational = operation is "Ilt" or "Clt" or "Ile" or "Cle" or "Igt" or "Cgt" or "Ige" or "Cge";
            var equality = operation is "Ieq" or "Ceq" or "Ine" or "Cne";
            if (equality && leftType == rightType && leftType != typeof(string) &&
                left.ValueState != PowerShellValueState.Null && right.ValueState != PowerShellValueState.Null &&
                !PowerShellCSharpOperatorPolicy.SupportsEquality(leftType))
                return Reject(diagnostics, span, "PSB2216", $"Equality comparison for CLR type '{leftType.FullName}' has no supported static CLR equality operator.");
            if (relational && leftType == typeof(string) && rightType == typeof(string))
                return Reject(diagnostics, span, "PSB2211", "PowerShell string relational comparison uses culture-aware runtime semantics and is not supported by the typed compiler.");
            if (relational && !(PowerShellClrTypeSemantics.IsNumeric(leftType) && PowerShellClrTypeSemantics.IsNumeric(rightType)) && !(leftType == typeof(char) && rightType == typeof(char)))
                return Reject(diagnostics, span, "PSB2212", $"Relational comparison for CLR type '{leftType.FullName}' is not supported by the conservative compiler.");
            var bound = operation switch
            {
                "Ieq" when leftType == typeof(string) => PowerShellBoundBinaryOperator.EqualIgnoreCase,
                "Ine" when leftType == typeof(string) => PowerShellBoundBinaryOperator.NotEqualIgnoreCase,
                "Ceq" when leftType == typeof(string) => PowerShellBoundBinaryOperator.EqualCaseSensitive,
                "Cne" when leftType == typeof(string) => PowerShellBoundBinaryOperator.NotEqualCaseSensitive,
                "Ieq" or "Ceq" => PowerShellBoundBinaryOperator.Equal,
                "Ine" or "Cne" => PowerShellBoundBinaryOperator.NotEqual,
                "Ilt" or "Clt" => PowerShellBoundBinaryOperator.LessThan,
                "Ile" or "Cle" => PowerShellBoundBinaryOperator.LessThanOrEqual,
                "Igt" or "Cgt" => PowerShellBoundBinaryOperator.GreaterThan,
                _ => PowerShellBoundBinaryOperator.GreaterThanOrEqual
            };
            return Binary(span, bound, left, right, typeof(bool));
        }

        return null;
    }

    private static PowerShellBoundExpression? BindTypeTest(
        BinaryExpressionAst syntax,
        SourceSpan span,
        bool negate,
        Func<Ast, PowerShellBoundExpression?> bindOperand,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        string? targetFramework)
    {
        if (syntax.Right is not TypeExpressionAst typeExpression ||
            typeExpression.TypeName.GetReflectionType() is not { } targetType ||
            !PowerShellGeneratedTypePolicy.IsSupported(targetType, targetFramework))
            return Reject(diagnostics, span, "PSB2220", "The right operand of '-is' or '-isnot' must be one statically resolvable target-compatible CLR type.");
        var operand = bindOperand(syntax.Left);
        return operand is null ? null : new PowerShellBoundTypeTestExpression(span, operand, targetType, negate);
    }

    private static PowerShellBoundExpression? BindRegexMatch(
        BinaryExpressionAst syntax,
        SourceSpan span,
        string operation,
        Func<Ast, PowerShellBoundExpression?> bindOperand,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        if (ObservesMatchesAutomaticVariable(syntax))
            return Reject(diagnostics, span, "PSB2221", "Regex matching whose $Matches automatic-variable state is observed requires PowerShell runtime semantics.");
        var input = bindOperand(syntax.Left);
        var pattern = bindOperand(syntax.Right);
        if (input is null || pattern is null) return null;
        if (input.Type.ClrType != typeof(string) || pattern.Type.ClrType != typeof(string))
            return Reject(diagnostics, span, "PSB2222", $"Operator '-{operation.ToLowerInvariant()}' requires scalar String operands.");
        return new PowerShellBoundRegexExpression(
            span,
            operation.Contains("not", StringComparison.OrdinalIgnoreCase) ? PowerShellBoundRegexOperation.NotMatch : PowerShellBoundRegexOperation.Match,
            input,
            pattern,
            null,
            !operation.StartsWith("C", StringComparison.Ordinal));
    }

    private static PowerShellBoundExpression? BindRegexReplace(
        BinaryExpressionAst syntax,
        SourceSpan span,
        string operation,
        Func<Ast, PowerShellBoundExpression?> bindOperand,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var input = bindOperand(syntax.Left);
        if (input is null) return null;
        if (input.Type.ClrType != typeof(string))
            return Reject(diagnostics, span, "PSB2223", $"Operator '-{operation.ToLowerInvariant()}' requires a scalar String input.");

        PowerShellBoundExpression? pattern;
        PowerShellBoundExpression? replacement;
        if (syntax.Right is ArrayLiteralAst { Elements.Count: 2 } arguments)
        {
            pattern = bindOperand(arguments.Elements[0]);
            replacement = bindOperand(arguments.Elements[1]);
        }
        else
        {
            pattern = bindOperand(syntax.Right);
            replacement = new PowerShellBoundLiteralExpression(span, string.Empty, new PowerShellTypeFact(typeof(string), PowerShellTypeFactProvenance.Literal, "Omitted regex replacement binds to the empty string."), PowerShellValueState.Known);
        }
        if (pattern is null || replacement is null) return null;
        if (pattern.Type.ClrType != typeof(string) || replacement.Type.ClrType != typeof(string))
            return Reject(diagnostics, span, "PSB2224", $"Operator '-{operation.ToLowerInvariant()}' requires a String pattern and optional String replacement.");
        return new PowerShellBoundRegexExpression(
            span,
            PowerShellBoundRegexOperation.Replace,
            input,
            pattern,
            replacement,
            !operation.StartsWith("C", StringComparison.Ordinal));
    }

    private static bool ObservesMatchesAutomaticVariable(Ast syntax)
    {
        var root = syntax;
        while (root.Parent is not null && root is not FunctionDefinitionAst) root = root.Parent;
        return root.FindAll(
            static node => node is VariableExpressionAst variable &&
                           variable.VariablePath.UserPath.Equals("Matches", StringComparison.OrdinalIgnoreCase) &&
                           !PowerShellAssignmentTargetPolicy.IsDirectAssignmentTarget(variable),
            searchNestedScriptBlocks: false).Any();
    }

    internal static PowerShellBoundExpression? BindUnary(
        UnaryExpressionAst syntax,
        SourceSpan span,
        Func<Ast, PowerShellBoundExpression?> bindOperand,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var operation = syntax.TokenKind.ToString();
        if (operation is "PlusPlus" or "MinusMinus" or "PostfixPlusPlus" or "PostfixMinusMinus") return null;
        var operand = bindOperand(syntax.Child);
        if (operand is null) return null;
        var type = operand.Type.ClrType;
        if (operation is "Not" or "Exclaim")
        {
            if (type != typeof(bool)) return Reject(diagnostics, span, "PSB2213", "Typed logical negation requires a Boolean operand.");
            return Unary(span, PowerShellBoundUnaryOperator.LogicalNot, operand, typeof(bool));
        }
        if (operation == "Bnot")
        {
            if (!PowerShellClrTypeSemantics.IsIntegral(type)) return Reject(diagnostics, span, "PSB2214", "Typed bitwise negation requires an integral operand.");
            return Unary(span, PowerShellBoundUnaryOperator.BitwiseNot, operand, PowerShellClrTypeSemantics.PromoteIntegral(type));
        }
        if (operation is "Plus" or "Minus")
        {
            if (!PowerShellClrTypeSemantics.IsNumeric(type) || PowerShellClrTypeSemantics.IsIntegral(type)) return null;
            return Unary(span, operation == "Plus" ? PowerShellBoundUnaryOperator.Identity : PowerShellBoundUnaryOperator.Negate, operand, type);
        }
        return null;
    }

    private static PowerShellBoundExpression Binary(SourceSpan span, PowerShellBoundBinaryOperator operation, PowerShellBoundExpression left, PowerShellBoundExpression right, Type type)
        => new PowerShellBoundBinaryExpression(span, operation, left, right, Fact(type, $"Operator '{operation}' selects a direct CLR operation."));

    private static PowerShellBoundExpression Unary(SourceSpan span, PowerShellBoundUnaryOperator operation, PowerShellBoundExpression operand, Type type)
        => new PowerShellBoundUnaryExpression(span, operation, operand, Fact(type, $"Operator '{operation}' selects a direct CLR operation."));

    private static PowerShellTypeFact Fact(Type type, string explanation)
        => new(type, PowerShellTypeFactProvenance.Inferred, explanation);

    private static Type? TryUnify(Type left, Type right, ICollection<PowerShellSemanticDiagnostic> diagnostics, SourceSpan span)
    {
        if (PowerShellClrTypeSemantics.TryUnify(left, right, out var result)) return result;
        Reject(diagnostics, span, "PSB2215", $"Types '{left.FullName}' and '{right.FullName}' cannot be unified without dynamic PowerShell coercion.");
        return null;
    }

    private static PowerShellBoundExpression? Reject(ICollection<PowerShellSemanticDiagnostic> diagnostics, SourceSpan span, string code, string message)
    {
        diagnostics.Add(new PowerShellSemanticDiagnostic(code, message, span));
        return null;
    }
}
