using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    internal static VariableExpressionAst? NativeAccessMutationReceiver(ExpressionAst target)
    {
        if (target is not MemberExpressionAst and not IndexExpressionAst) return null;
        var root = Generated.Runtime.PowerShellNativeFunctionContext.FindNativeAccessMutationReceiver(target);
        return root is not null && !PowerShellAssignmentTargetPolicy.IsAutomaticVariable(root.VariablePath.UserPath)
            ? root : null;
    }

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
