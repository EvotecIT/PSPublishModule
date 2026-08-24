using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellCSharpMethodEmitter
{
    private Type InferMemberInvocationType(InvokeMemberExpressionAst invocation)
    {
        RejectObservableSwitchParameter(invocation.Expression, invocation);
        return _memberEmitter.InferInvocationType(invocation);
    }

    private Type InferMemberAccessType(MemberExpressionAst member)
    {
        RejectObservableSwitchParameter(member.Expression, member);
        return _memberEmitter.InferMemberType(member);
    }

    private string EmitMemberInvocation(InvokeMemberExpressionAst invocation)
    {
        RejectObservableSwitchParameter(invocation.Expression, invocation);
        return _memberEmitter.EmitInvocation(invocation);
    }

    private string EmitMemberAccess(MemberExpressionAst member)
    {
        RejectObservableSwitchParameter(member.Expression, member);
        return _memberEmitter.EmitMember(member);
    }

    private void RejectObservableSwitchParameter(ExpressionAst receiver, Ast observation)
    {
        var expression = UnwrapTransparentExpression(receiver);
        if (expression is not VariableExpressionAst variable)
            return;
        var parameter = _body.ParamBlock?.Parameters.FirstOrDefault(candidate =>
            candidate.Name.VariablePath.UserPath.Equals(variable.VariablePath.UserPath, StringComparison.OrdinalIgnoreCase));
        if (parameter?.StaticType == typeof(System.Management.Automation.SwitchParameter))
            throw Error(observation, $"Switch parameter '${variable.VariablePath.UserPath}' is represented as Boolean by the runtime-independent method and cannot expose SwitchParameter members or CLR type identity.");
    }
}
