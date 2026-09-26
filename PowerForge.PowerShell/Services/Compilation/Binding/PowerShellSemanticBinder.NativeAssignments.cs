using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    // Keep conditional output capture on a direct receiver. The existing native
    // assignment owner retains authored target evaluation and storage semantics.
    internal static bool IsNativeConditionalAccessCaptureTarget(ExpressionAst target)
        => IsNativeConditionalIndexCaptureTarget(target) || target is MemberExpressionAst
        {
            Static: false,
            Expression: VariableExpressionAst { VariablePath.IsUnqualified: true } receiver,
            Member: StringConstantExpressionAst
        } member && member is not InvokeMemberExpressionAst &&
           member.GetType().GetProperty("NullConditional")?.GetValue(member) is not true &&
           !PowerShellAssignmentTargetPolicy.IsAutomaticVariable(receiver.VariablePath.UserPath);

    private static bool IsNativeConditionalIndexCaptureTarget(ExpressionAst target)
        => target is IndexExpressionAst
        {
            Target: VariableExpressionAst { VariablePath.IsUnqualified: true } receiver
        } index && index.GetType().GetProperty("NullConditional")?.GetValue(index) is not true &&
            !PowerShellAssignmentTargetPolicy.IsAutomaticVariable(receiver.VariablePath.UserPath) &&
            IsNativeAssignmentIndex(index.Index);

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
        => Generated.Runtime.PowerShellNativeFunctionContext.IsNativeAssignmentIndex(index);

    private static VariableExpressionAst? NativeReceiverRoot(ExpressionAst receiver)
        => receiver as VariableExpressionAst ?? NativeAssignmentReceiver(receiver);
}
