using System.Text;

namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private Func<string, string> _getTemporaryIdentifier = null!;
    private readonly Dictionary<(Type, Type, PowerShellBoundMutationOperator, PowerShellIntegralMutationSemantics), (string Name, string Source)> _numericHelpers = new();

    private string? EmitNumericUnionBinary(PowerShellLoweredBinaryExpression expression, string left, string right)
    {
        if (expression.Operation is PowerShellBoundBinaryOperator.PromotingAdd or
            PowerShellBoundBinaryOperator.PromotingSubtract or PowerShellBoundBinaryOperator.PromotingMultiply or
            PowerShellBoundBinaryOperator.NumericUnionNonzeroDivide or PowerShellBoundBinaryOperator.NumericUnionNonzeroRemainder)
        {
            var arithmetic = expression.Operation switch
            {
                PowerShellBoundBinaryOperator.PromotingAdd => PowerShellBoundMutationOperator.Add,
                PowerShellBoundBinaryOperator.PromotingSubtract => PowerShellBoundMutationOperator.Subtract,
                PowerShellBoundBinaryOperator.NumericUnionNonzeroDivide => PowerShellBoundMutationOperator.Divide,
                PowerShellBoundBinaryOperator.NumericUnionNonzeroRemainder => PowerShellBoundMutationOperator.Remainder,
                _ => PowerShellBoundMutationOperator.Multiply
            };
            var helper = GetIntegralArithmeticHelper(typeof(object), typeof(object), arithmetic,
                PowerShellIntegralMutationSemantics.UnconstrainedInt32OrDouble);
            var invocation = $"{helper}({left}, {right})";
            return expression.ClrType == typeof(double) ? $"global::System.Convert.ToDouble({invocation})" : invocation;
        }
        var symbol = expression.Operation switch
        {
            PowerShellBoundBinaryOperator.NumericUnionEqual => "==",
            PowerShellBoundBinaryOperator.NumericUnionNotEqual => "!=",
            PowerShellBoundBinaryOperator.NumericUnionLessThan => "<",
            PowerShellBoundBinaryOperator.NumericUnionLessThanOrEqual => "<=",
            PowerShellBoundBinaryOperator.NumericUnionGreaterThan => ">",
            PowerShellBoundBinaryOperator.NumericUnionGreaterThanOrEqual => ">=",
            PowerShellBoundBinaryOperator.NumericUnionFloatingDivide => "/",
            PowerShellBoundBinaryOperator.NumericUnionFloatingRemainder => "%",
            _ => null
        };
        return symbol is null ? null : $"(global::System.Convert.ToDouble({left}) {symbol} global::System.Convert.ToDouble({right}))";
    }

    private string EmitIntegralMutation(
        string target,
        Type targetType,
        PowerShellBoundMutationOperator operation,
        PowerShellLoweredExpression? value,
        PowerShellIntegralMutationSemantics semantics)
    {
        var arithmetic = operation switch
        {
            PowerShellBoundMutationOperator.Increment or PowerShellBoundMutationOperator.PostIncrement => PowerShellBoundMutationOperator.Add,
            PowerShellBoundMutationOperator.Decrement or PowerShellBoundMutationOperator.PostDecrement => PowerShellBoundMutationOperator.Subtract,
            _ => operation
        };
        var rightType = value?.ClrType ?? targetType;
        var helper = GetIntegralArithmeticHelper(targetType, rightType, arithmetic, semantics);
        // Evaluate the RHS before entering the conversion helper. Exceptions from
        // user expressions must not be mistaken for a failed numeric conversion.
        var right = value is null ? "1" : EmitExpression(value);
        return $"{target} = {helper}({target}, {right})";
    }

    private string GetIntegralArithmeticHelper(Type targetType, Type rightType, PowerShellBoundMutationOperator arithmetic,
        PowerShellIntegralMutationSemantics semantics)
    {
        var key = (targetType, rightType, arithmetic, semantics);
        if (!_numericHelpers.TryGetValue(key, out var helper))
        {
            var name = _getTemporaryIdentifier("typedNumericUpdate");
            helper = (name, RenderIntegralMutationHelper(name, targetType, rightType, arithmetic, semantics));
            _numericHelpers.Add(key, helper);
        }
        return helper.Name;
    }

    private string RenderIntegralMutationHelper(string name, Type targetType, Type rightType, PowerShellBoundMutationOperator operation,
        PowerShellIntegralMutationSemantics semantics)
    {
        var type = PowerShellCSharpSymbolRenderer.TypeName(targetType);
        var rightTypeName = PowerShellCSharpSymbolRenderer.TypeName(rightType);
        var left = _getTemporaryIdentifier("numericLeft");
        var right = _getTemporaryIdentifier("numericRight");
        var result = _getTemporaryIdentifier("numericResult");
        var error = _getTemporaryIdentifier("numericError");
        var promotedType = semantics == PowerShellIntegralMutationSemantics.PromotedBigIntegerProduct
            ? "global::System.Numerics.BigInteger" : "decimal";
        var symbol = operation switch
        {
            PowerShellBoundMutationOperator.Add => "+",
            PowerShellBoundMutationOperator.Subtract => "-",
            PowerShellBoundMutationOperator.Multiply => "*",
            PowerShellBoundMutationOperator.Divide => "/",
            PowerShellBoundMutationOperator.Remainder => "%",
            _ => throw new InvalidOperationException($"Unsupported typed integral update '{operation}'.")
        };
        if (semantics == PowerShellIntegralMutationSemantics.UnconstrainedInt32OrDouble)
        {
            var builder = new StringBuilder()
                .Append("            static object ").Append(name).Append("(object? ").Append(left).Append(", object? ").Append(right).AppendLine(")")
                .AppendLine("            {")
                .Append("                ").Append(left).AppendLine(" ??= 0;")
                .Append("                ").Append(right).AppendLine(" ??= 0;")
                .Append("                if (").Append(left).Append(" is int && ").Append(right).AppendLine(" is int)")
                .AppendLine("                {");
            // A fractional integer quotient becomes Double. Use Int64 for the
            // exact branch so Int32.MinValue / -1 and % -1 cannot overflow.
            if (operation == PowerShellBoundMutationOperator.Divide)
                builder.Append("                    if ((long)(int)").Append(left).Append(" % (long)(int)").Append(right).AppendLine(" != 0)")
                    .Append("                        return (double)(int)").Append(left).Append(" / (double)(int)").Append(right).AppendLine(";");
            return builder
                .Append("                    long ").Append(result).Append(" = (long)(int)").Append(left).Append(' ').Append(symbol).Append(" (long)(int)").Append(right).AppendLine(";")
                .Append("                    if (").Append(result).Append(" >= int.MinValue && ").Append(result).AppendLine(" <= int.MaxValue)")
                .Append("                        return (object)(int)").Append(result).AppendLine(";")
                .Append("                    return (double)").Append(result).AppendLine(";")
                .AppendLine("                }")
                .Append("                if ((").Append(left).Append(" is int || ").Append(left).Append(" is double) && (")
                .Append(right).Append(" is int || ").Append(right).AppendLine(" is double))")
                .Append("                    return global::System.Convert.ToDouble(").Append(left).Append(") ").Append(symbol)
                .Append(" global::System.Convert.ToDouble(").Append(right).AppendLine(");")
                .AppendLine("                throw new global::System.InvalidOperationException(\"The numeric value left its bound Int32/Double contract.\");")
                .AppendLine("            }").ToString();
        }
        // Keep ordinary constrained updates on the checked CLR fast path.
        // Decimal represents every integral operand exactly and computes signed
        // minimum remainder -1 without the CLR integral remainder overflow trap.
        // An out-of-range integer result is promoted to Double before conversion
        // back to the constrained variable, as in PowerShell numeric assignment.
        // The bound contract selects BigInteger for promoted 64-bit products so
        // its exact Double conversion remains the selected runtime behavior.
        return new StringBuilder()
            .Append("            static ").Append(type).Append(' ').Append(name)
            .Append('(').Append(type).Append(' ').Append(left).Append(", ").Append(rightTypeName).Append(' ').Append(right).AppendLine(")")
            .AppendLine("            {")
            .Append("                try { return checked((").Append(type).Append(")(").Append(left).Append(' ').Append(symbol).Append(' ').Append(right).AppendLine(")); }")
            .AppendLine("                catch (global::System.OverflowException) { }")
            .AppendLine("                try")
            .AppendLine("                {")
            .Append("                    ").Append(promotedType).Append(' ').Append(result)
            .Append(" = (").Append(promotedType).Append(')').Append(left).Append(' ').Append(symbol)
            .Append(" (").Append(promotedType).Append(')').Append(right).AppendLine(";")
            .Append("                    if (").Append(result).Append(" >= ").Append(type).Append(".MinValue && ").Append(result).Append(" <= ").Append(type).AppendLine(".MaxValue)")
            .Append("                        return checked((").Append(type).Append(')').Append(result).AppendLine(");")
            .Append("                    return checked((").Append(type).Append(")(double)").Append(result).AppendLine(");")
            .AppendLine("                }")
            .Append("                catch (global::System.OverflowException ").Append(error).AppendLine(")")
            .AppendLine("                {")
            .Append("                    throw new global::System.InvalidCastException(\"The numeric result cannot be converted to the variable's constrained integral type.\", ").Append(error).AppendLine(");")
            .AppendLine("                }")
            .AppendLine("            }")
            .ToString();
    }
}
