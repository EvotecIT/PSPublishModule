namespace PowerForge;

/// <summary>Classifies closed CLR primitive operations whose normal evaluation has no exception route.</summary>
internal static class PowerShellLoweredPrimitiveErrorPolicy
{
    internal static bool IsNonThrowing(PowerShellLoweredExpression expression)
        => expression switch
        {
            PowerShellLoweredConversionExpression conversion => conversion.NativeSourcePath is null && !conversion.UseNativeConversion && (conversion.NormalizeNullString || !conversion.UsePowerShellLanguageRuntime &&
                !conversion.UsePowerShellTruthiness && conversion.ClrType == typeof(double) &&
                (conversion.Operand.ClrType == typeof(double) || conversion.Operand.ClrType == typeof(int))),
            PowerShellLoweredMutationExpression mutation => mutation.NativeTargetRead is null &&
                (mutation.Operation == PowerShellBoundMutationOperator.Assign ||
                IsNonThrowingNumericMutation(mutation.TargetClrType, mutation.IntegralSemantics)),
            PowerShellLoweredBinaryExpression binary => IsNonThrowingBinary(binary),
            PowerShellLoweredUnaryExpression unary => unary.Operand.ClrType == typeof(double) &&
                unary.Operation is PowerShellBoundUnaryOperator.Identity or PowerShellBoundUnaryOperator.Negate ||
                unary.Operand.ClrType == typeof(bool) && unary.Operation == PowerShellBoundUnaryOperator.LogicalNot,
            PowerShellLoweredClrInvocationExpression invocation => PowerShellClrPrimitiveInvocationPolicy.IsNonThrowing(
                invocation.DeclaringType, invocation.MemberName, invocation.InvocationKind, invocation.ClrType, invocation.ParameterTypes),
            PowerShellLoweredClrMemberExpression member => IsNonThrowingMemberRead(member),
            // A closed literal map has no dynamic key/value evaluation or conversion route.
            // The binder already rejects duplicate literal keys. Ordinary allocation failure is
            // outside the modeled language-error contract, as it is for other generated containers.
            PowerShellLoweredDictionaryExpression dictionary => IsClosedLiteralMap(dictionary),
            _ => false
        };

    private static bool IsClosedLiteralMap(PowerShellLoweredDictionaryExpression dictionary)
        => PowerShellRegionTransferTypePolicy.IsAtomicDictionaryReference(dictionary.ClrType) &&
           dictionary.Entries.All(static entry => IsClosedLiteralKey(entry.Key) && IsClosedLiteralValue(entry.Value));

    private static bool IsClosedLiteralKey(PowerShellLoweredExpression expression)
        => expression is PowerShellLoweredLiteralExpression { Value: not null } literal &&
           PowerShellStableScalarTypePolicy.IsSupported(literal.ClrType);

    private static bool IsClosedLiteralValue(PowerShellLoweredExpression expression)
        => expression is PowerShellLoweredLiteralExpression literal &&
               (literal.Value is null || PowerShellStableScalarTypePolicy.IsSupported(literal.ClrType)) ||
           expression is PowerShellLoweredDictionaryExpression dictionary && IsClosedLiteralMap(dictionary);

    private static bool IsNonThrowingMemberRead(PowerShellLoweredClrMemberExpression member)
        => !member.IsStatic && member.ClrType == typeof(int) && member.MemberName == "Length" &&
           (member.DeclaringType == typeof(Array) &&
                member.ReceiverBehavior == PowerShellClrReceiverBehavior.NormalizeNullCount ||
            member.DeclaringType.IsArray &&
                member.ReceiverBehavior == PowerShellClrReceiverBehavior.NormalizeNullArrayLength);

    internal static bool IsNonThrowingNumericMutation(Type type, PowerShellIntegralMutationSemantics semantics)
        => type == typeof(double) && semantics == PowerShellIntegralMutationSemantics.None ||
           semantics == PowerShellIntegralMutationSemantics.UnconstrainedInt32OrDouble;

    private static bool IsNonThrowingBinary(PowerShellLoweredBinaryExpression binary)
    {
        if (binary.UsesNativeInvocation) return false;
        if (binary.Operation is PowerShellBoundBinaryOperator.PromotingAdd or PowerShellBoundBinaryOperator.PromotingSubtract or
            PowerShellBoundBinaryOperator.PromotingMultiply or PowerShellBoundBinaryOperator.NumericUnionFloatingDivide or
            PowerShellBoundBinaryOperator.NumericUnionFloatingRemainder or PowerShellBoundBinaryOperator.NumericUnionNonzeroDivide or
            PowerShellBoundBinaryOperator.NumericUnionNonzeroRemainder or PowerShellBoundBinaryOperator.NumericUnionEqual or
            PowerShellBoundBinaryOperator.NumericUnionNotEqual or PowerShellBoundBinaryOperator.NumericUnionLessThan or
            PowerShellBoundBinaryOperator.NumericUnionLessThanOrEqual or PowerShellBoundBinaryOperator.NumericUnionGreaterThan or
            PowerShellBoundBinaryOperator.NumericUnionGreaterThanOrEqual) return true;
        if (binary.Left.ClrType == typeof(double) && binary.Right.ClrType == typeof(double))
            return binary.Operation is PowerShellBoundBinaryOperator.Add or PowerShellBoundBinaryOperator.Subtract or
                PowerShellBoundBinaryOperator.Multiply or PowerShellBoundBinaryOperator.Divide or PowerShellBoundBinaryOperator.Remainder or
                PowerShellBoundBinaryOperator.Equal or PowerShellBoundBinaryOperator.NotEqual or PowerShellBoundBinaryOperator.LessThan or
                PowerShellBoundBinaryOperator.LessThanOrEqual or PowerShellBoundBinaryOperator.GreaterThan or PowerShellBoundBinaryOperator.GreaterThanOrEqual;
        if (binary.Left.ClrType == typeof(int) && binary.Right.ClrType == typeof(int))
            return binary.Operation is PowerShellBoundBinaryOperator.Equal or PowerShellBoundBinaryOperator.NotEqual or
                PowerShellBoundBinaryOperator.LessThan or PowerShellBoundBinaryOperator.LessThanOrEqual or
                PowerShellBoundBinaryOperator.GreaterThan or PowerShellBoundBinaryOperator.GreaterThanOrEqual or
                PowerShellBoundBinaryOperator.BitwiseAnd or PowerShellBoundBinaryOperator.BitwiseOr or
                PowerShellBoundBinaryOperator.BitwiseExclusiveOr;
        return binary.Left.ClrType == typeof(bool) && binary.Right.ClrType == typeof(bool) &&
            binary.Operation is PowerShellBoundBinaryOperator.Equal or PowerShellBoundBinaryOperator.NotEqual or
                PowerShellBoundBinaryOperator.LogicalAnd or PowerShellBoundBinaryOperator.LogicalOr;
    }
}
