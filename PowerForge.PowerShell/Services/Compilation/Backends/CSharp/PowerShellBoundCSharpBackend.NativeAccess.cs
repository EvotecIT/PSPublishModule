namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private static string EmitNativeTypeConstraint(Type? type)
        => type is null ? "null" : "typeof(" + PowerShellCSharpSymbolRenderer.TypeName(type) + ")";

    private string EmitNativeMember(PowerShellLoweredNativeMemberExpression member)
        => member.NameExpression is null
            ? "__nativeFunction.ReadMember(" + EmitExpression(member.Receiver!) + ", " + PowerShellCSharpLiteral.QuoteString(member.Name!) + ")"
            : "__nativeFunction.ReadDynamicMember(" +
              (member.Receiver is null ? EmitNativeTypeConstraint(member.LiteralTargetType) : EmitExpression(member.Receiver)) + ", " +
              EmitExpression(member.NameExpression) + ", " + (member.IsStatic ? "true" : "false") + ")";

    private string EmitNativeIndex(PowerShellLoweredNativeIndexExpression index)
        => "__nativeFunction.ReadIndex(" + EmitExpression(index.Receiver) + ", new object[] { " +
           string.Join(", ", index.Arguments.Select(EmitExpression)) + " }, " +
           EmitNativeTypeConstraint(index.TargetConstraint) + ", " + EmitNativeTypeConstraint(index.IndexConstraint) + ")";

    private string EmitNativeInvocation(PowerShellLoweredNativeInvocationExpression invocation)
    {
        var arguments = "new object[] { " + string.Join(", ", invocation.Arguments.Select(EmitExpression)) + " }";
        var hasReferences = invocation.References.Count > 0;
        if (hasReferences && invocation.References.All(reference =>
                invocation.Arguments[reference.Index] is PowerShellLoweredNativeReferenceExpression))
        {
            var operands = _getTemporaryIdentifier("nativeReferenceOperands");
            var receiver = invocation.Receiver is null ? EmitNativeTypeConstraint(invocation.LiteralTargetType) : EmitExpression(invocation.Receiver);
            // Acquire the receiver before arguments, and let the native binder mutate real variable cells.
            // There is no synthetic value-copy writeback or second constraint application.
            return "__statementErrors.EvaluateNativeInvocation(() => new object[] { " + receiver + ", " + arguments + " }, " +
                operands + " => __nativeFunction.InvokeMember(" + operands + "[0], " + PowerShellCSharpLiteral.QuoteString(invocation.Name) +
                ", " + (invocation.IsStatic ? "true" : "false") + ", (object[])" + operands + "[1], " +
                EmitNativeTypeConstraint(invocation.TargetConstraint) + ", new global::System.Type[] { " +
                string.Join(", ", invocation.ArgumentConstraints.Select(EmitNativeTypeConstraint)) + " }))";
        }
        var argumentTemporary = hasReferences ? _getTemporaryIdentifier("nativeArguments") : null;
        var call = "__nativeFunction." + (hasReferences ? "InvokeMemberWithReferenceWriteback(" : "InvokeMember(") +
           (invocation.Receiver is null ? EmitNativeTypeConstraint(invocation.LiteralTargetType) : EmitExpression(invocation.Receiver)) +
           ", " + PowerShellCSharpLiteral.QuoteString(invocation.Name) + ", " + (invocation.IsStatic ? "true" : "false") +
           ", " + (hasReferences ? argumentTemporary : arguments) + ", " +
           EmitNativeTypeConstraint(invocation.TargetConstraint) + ", new global::System.Type[] { " +
           string.Join(", ", invocation.ArgumentConstraints.Select(EmitNativeTypeConstraint)) + " }" +
           (!hasReferences ? ")" : ", new int[] { " +
            string.Join(", ", invocation.References.Select(static reference => reference.Index)) + " }, new string[] { " +
            string.Join(", ", invocation.References.Select(static reference => PowerShellCSharpLiteral.QuoteString(reference.VariableName))) + " })");
        return hasReferences
            ? "__statementErrors.EvaluateNativeInvocation(() => " + arguments + ", " + argumentTemporary + " => " + call + ")"
            : call;
    }
}
