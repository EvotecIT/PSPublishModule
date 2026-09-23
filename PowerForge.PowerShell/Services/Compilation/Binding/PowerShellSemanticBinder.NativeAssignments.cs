using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    // These targets use native storage semantics without introducing another command or body.
    // More complex receiver/index expressions require a separately bound evaluation contract.
    private static VariableExpressionAst? NativeAssignmentReceiver(ExpressionAst target)
        => target switch
        {
            MemberExpressionAst { Static: false } member => NativeReceiverRoot(member.Expression),
            IndexExpressionAst index when IsNativeAssignmentIndex(index.Index) => NativeReceiverRoot(index.Target),
            _ => null
        };

    // The PowerShell host evaluates this authored key after the compiled RHS. A direct
    // property read can participate without inventing a CLR/ETS member contract.
    private static bool IsNativeAssignmentIndex(ExpressionAst index)
        => index is ConstantExpressionAst or StringConstantExpressionAst or VariableExpressionAst ||
           index is MemberExpressionAst { Static: false, Expression: VariableExpressionAst receiver, Member: StringConstantExpressionAst } member &&
           member is not InvokeMemberExpressionAst &&
           member.GetType().GetProperty("NullConditional")?.GetValue(member) is not true &&
           receiver.VariablePath.IsUnqualified;

    private static VariableExpressionAst? NativeReceiverRoot(ExpressionAst receiver)
        => receiver as VariableExpressionAst ?? NativeAssignmentReceiver(receiver);
}
