namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private static void EmitNativeEntrySequencePoint(System.Text.StringBuilder builder, PowerShellLoweredFunction function)
        => builder.Append("            __statementErrors.SetNativeSequencePoint(")
            .Append(PowerShellCSharpLiteral.QuoteString(function.SourcePath)).Append(", ")
            .Append(function.Span.StartLine).Append(", ").Append(function.Span.StartColumn).Append(", ")
            .Append(function.Span.EndLine).Append(", ").Append(function.Span.EndColumn).Append(", ")
            .Append(PowerShellCSharpLiteral.QuoteString(function.SourceText)).AppendLine(");");

    private string EmitNativeMutation(PowerShellLoweredNativeVariableExpression target,
        PowerShellBoundMutationOperator operation, PowerShellLoweredExpression? value, SourceSpan span, string sourceText)
        => EmitNativeExpressionPosition(EmitNativeMutationValue(target, operation, value), span, target.SourcePath, sourceText);

    private static string EmitNativeExpressionPosition(string value, SourceSpan span, string sourcePath, string sourceText, bool postTestCondition = false)
        => "__statementErrors.EvaluateNativeExpression(() => " + value + ", " +
            PowerShellCSharpLiteral.QuoteString(sourcePath) + ", " + span.StartLine + ", " + span.StartColumn + ", " +
            span.EndLine + ", " + span.EndColumn + ", " + PowerShellCSharpLiteral.QuoteString(sourceText) + (postTestCondition ? ", true)" : ")");

    private string EmitNativeMutationValue(PowerShellLoweredNativeVariableExpression target,
        PowerShellBoundMutationOperator operation, PowerShellLoweredExpression? value)
    {
        var name = PowerShellCSharpLiteral.QuoteString(target.Name);
        if (operation is PowerShellBoundMutationOperator.Increment or PowerShellBoundMutationOperator.Decrement or
            PowerShellBoundMutationOperator.PostIncrement or PowerShellBoundMutationOperator.PostDecrement)
        {
            var decrement = operation is PowerShellBoundMutationOperator.Decrement or PowerShellBoundMutationOperator.PostDecrement;
            var postfix = operation is PowerShellBoundMutationOperator.PostIncrement or PowerShellBoundMutationOperator.PostDecrement;
            return $"__nativeFunction.MutateVariable({name}, {EmitNativeVariableRead(target)}, {(decrement ? "true" : "false")}, {(postfix ? "true" : "false")})";
        }
        if (value is null) throw new InvalidOperationException($"Native mutation '{operation}' requires a value.");
        var right = EmitExpression(value);
        if (operation != PowerShellBoundMutationOperator.Assign)
        {
            var binary = operation switch
            {
                PowerShellBoundMutationOperator.Add => "Add",
                PowerShellBoundMutationOperator.Subtract => "Subtract",
                PowerShellBoundMutationOperator.Multiply => "Multiply",
                PowerShellBoundMutationOperator.Divide => "Divide",
                PowerShellBoundMutationOperator.Remainder => "Modulo",
                _ => throw new InvalidOperationException($"Native mutation '{operation}' has no operation owner.")
            };
            right = $"__nativeFunction.EvaluateBinary(global::System.Linq.Expressions.ExpressionType.{binary}, true, {EmitNativeVariableRead(target)}, {right})";
        }
        return $"__nativeFunction.AssignVariable({name}, {right}, {(target.DirectLocal ? "true" : "false")})";
    }
}
