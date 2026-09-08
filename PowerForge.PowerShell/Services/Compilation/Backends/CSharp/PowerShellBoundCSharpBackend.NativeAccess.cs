namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private static string EmitNativeTypeConstraint(Type? type)
        => type is null ? "null" : "typeof(" + PowerShellCSharpSymbolRenderer.TypeName(type) + ")";

    private string EmitNativeMember(PowerShellLoweredNativeMemberExpression member)
        => "__nativeFunction.ReadMember(" + EmitExpression(member.Receiver) + ", " + PowerShellCSharpLiteral.QuoteString(member.Name) + ")";

    private string EmitNativeIndex(PowerShellLoweredNativeIndexExpression index)
        => "__nativeFunction.ReadIndex(" + EmitExpression(index.Receiver) + ", new object[] { " +
           string.Join(", ", index.Arguments.Select(EmitExpression)) + " }, " +
           EmitNativeTypeConstraint(index.TargetConstraint) + ", " + EmitNativeTypeConstraint(index.IndexConstraint) + ")";

    private string EmitNativeInvocation(PowerShellLoweredNativeInvocationExpression invocation)
        => "__nativeFunction.InvokeMember(" +
           (invocation.Receiver is null ? EmitNativeTypeConstraint(invocation.LiteralTargetType) : EmitExpression(invocation.Receiver)) +
           ", " + PowerShellCSharpLiteral.QuoteString(invocation.Name) + ", " + (invocation.IsStatic ? "true" : "false") +
           ", new object[] { " + string.Join(", ", invocation.Arguments.Select(EmitExpression)) + " }, " +
           EmitNativeTypeConstraint(invocation.TargetConstraint) + ", new global::System.Type[] { " +
           string.Join(", ", invocation.ArgumentConstraints.Select(EmitNativeTypeConstraint)) + " })";
}
