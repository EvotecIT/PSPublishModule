namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private void EmitNativeEntrySequencePoint(System.Text.StringBuilder builder, PowerShellLoweredFunction function)
        => builder.Append("            __statementErrors.SetNativeSequencePoint(")
            .Append(QuotePortableSourcePath(function.SourcePath)).Append(", ")
            .Append(function.Span.StartLine).Append(", ").Append(function.Span.StartColumn).Append(", ")
            .Append(function.Span.EndLine).Append(", ").Append(function.Span.EndColumn).Append(", ")
            .Append(PowerShellCSharpLiteral.QuoteString(function.SourceText)).AppendLine(");");

    private string EmitNativeMutation(PowerShellLoweredNativeVariableExpression target,
        PowerShellBoundMutationOperator operation, PowerShellLoweredExpression? value, SourceSpan span, string sourceText, PowerShellNativeAssignmentTarget? assignmentTarget = null)
        => EmitNativeExpressionPosition(EmitNativeMutationValue(target, operation, value, assignmentTarget), span, target.SourcePath, sourceText);

    private string EmitNativeExpressionPosition(string value, SourceSpan span, string sourcePath, string sourceText, bool postTestCondition = false)
        => "__statementErrors.EvaluateNativeExpression(() => " + value + ", " +
            QuotePortableSourcePath(sourcePath) + ", " + span.StartLine + ", " + span.StartColumn + ", " +
            span.EndLine + ", " + span.EndColumn + ", " + PowerShellCSharpLiteral.QuoteString(sourceText) + (postTestCondition ? ", true)" : ")");

    private string EmitNativeMutationValue(PowerShellLoweredNativeVariableExpression target,
        PowerShellBoundMutationOperator operation, PowerShellLoweredExpression? value, PowerShellNativeAssignmentTarget? assignmentTarget = null)
    {
        if (assignmentTarget is not null)
        {
            if (value is null) throw new InvalidOperationException("A native assignment requires its compiled value expression.");
            return EmitNativeAssignmentTarget(assignmentTarget, operation, value);
        }
        var name = PowerShellCSharpLiteral.QuoteString(target.Name);
        if (operation is PowerShellBoundMutationOperator.Increment or PowerShellBoundMutationOperator.Decrement or
            PowerShellBoundMutationOperator.PostIncrement or PowerShellBoundMutationOperator.PostDecrement)
        {
            var decrement = operation is PowerShellBoundMutationOperator.Decrement or PowerShellBoundMutationOperator.PostDecrement;
            var postfix = operation is PowerShellBoundMutationOperator.PostIncrement or PowerShellBoundMutationOperator.PostDecrement;
            return $"__nativeFunction.MutateVariable({name}, {EmitNativeVariableRead(target)}, {(decrement ? "true" : "false")}, {(postfix ? "true" : "false")})";
        }
        throw new InvalidOperationException($"Native mutation '{operation}' requires its authored assignment target.");
    }

    private string EmitNativeAssignmentTarget(PowerShellNativeAssignmentTarget target,
        PowerShellBoundMutationOperator operation, PowerShellLoweredExpression value)
        => EmitNativeAssignmentStart(target, operation) + "() => (object?)(" + EmitExpression(value) + ")" +
            EmitNativeAssignmentLocation(target);

    private static string EmitNativeAssignmentStart(PowerShellNativeAssignmentTarget target, PowerShellBoundMutationOperator operation)
        => "__nativeFunction.AssignTarget(" + PowerShellCSharpLiteral.QuoteString(target.Text) + ", " +
            PowerShellCSharpLiteral.QuoteString(operation.ToString()) + ", ";

    private string EmitNativeAssignmentLocation(PowerShellNativeAssignmentTarget target)
        => ", " +
            QuotePortableSourcePath(target.SourcePath) + ", " + target.Span.StartLine + ", " +
            target.Span.StartColumn + ", " + PowerShellCSharpLiteral.QuoteString(target.SourceDocument) + ", " +
            target.StartOffset + ", " + target.EndOffset + ")";
}
