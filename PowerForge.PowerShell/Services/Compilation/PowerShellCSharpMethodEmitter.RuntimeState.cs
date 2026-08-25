using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellCSharpMethodEmitter
{
    private bool TryGetRuntimeStateIntrinsic(Ast ast, out PowerShellRuntimeStateIntrinsicKind kind)
        => PowerShellRuntimeStateIntrinsicPolicy.TryClassify(ast, _body, _targetFramework, _capabilities, out kind);

    private string EmitRuntimeStateIntrinsic(Ast ast)
    {
        if (!TryGetRuntimeStateIntrinsic(ast, out var kind))
            throw Error(ast, "The runtime-state expression is outside the bounded intrinsic contract.");
        if (kind is PowerShellRuntimeStateIntrinsicKind.ShouldProcessTarget or PowerShellRuntimeStateIntrinsicKind.ShouldProcessAction)
        {
            var invocation = (InvokeMemberExpressionAst)ast;
            if (invocation.Arguments.Any(argument => InferExpressionType(argument) != typeof(string)))
                throw Error(invocation, "$PSCmdlet.ShouldProcess currently requires one or two scalar String arguments.");
            var arguments = invocation.Arguments.Select(EmitExpression).ToArray();
            return kind == PowerShellRuntimeStateIntrinsicKind.ShouldProcessTarget
                ? $"__shouldProcessTarget({arguments[0]})"
                : $"__shouldProcessAction({arguments[0]}, {arguments[1]})";
        }
        if (string.IsNullOrWhiteSpace(_targetFramework))
            throw Error(ast, "Runtime-state intrinsic emission requires an explicit target framework.");
        return PowerShellRuntimeStateIntrinsicPolicy.EmitStatic(kind, _targetFramework!);
    }
}
