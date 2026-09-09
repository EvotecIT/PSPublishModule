using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    // These targets use native storage semantics without introducing another command or body.
    // More complex receiver/index expressions require a separately bound evaluation contract.
    private static VariableExpressionAst? NativeAssignmentReceiver(ExpressionAst target)
        => target switch
        {
            MemberExpressionAst { Static: false, Member: StringConstantExpressionAst } member => NativeReceiverRoot(member.Expression),
            IndexExpressionAst { Index: ConstantExpressionAst or StringConstantExpressionAst or VariableExpressionAst } index => NativeReceiverRoot(index.Target),
            _ => null
        };

    private static VariableExpressionAst? NativeReceiverRoot(ExpressionAst receiver)
        => receiver as VariableExpressionAst ?? NativeAssignmentReceiver(receiver);
}
