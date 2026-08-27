using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Recognizes the binding-presence query that can be preserved without PowerShell runtime scope.
/// </summary>
internal static class PowerShellBoundParametersPolicy
{
    internal const string VariableName = "PSBoundParameters";

    internal static bool IsReference(VariableExpressionAst variable)
        => variable.VariablePath.UserPath.Equals(VariableName, StringComparison.OrdinalIgnoreCase);

    internal static bool IsSupportedReference(VariableExpressionAst variable)
        => variable.Parent is InvokeMemberExpressionAst invocation &&
           ReferenceEquals(invocation.Expression, variable) &&
           TryGetContainsKey(invocation, out _);

    internal static bool TryGetContainsKey(InvokeMemberExpressionAst invocation, out string parameterName)
    {
        parameterName = string.Empty;
        if (invocation.Static ||
            invocation.Expression is not VariableExpressionAst variable ||
            !IsReference(variable) ||
            invocation.Member is not StringConstantExpressionAst member ||
            !member.Value.Equals("ContainsKey", StringComparison.OrdinalIgnoreCase) ||
            invocation.Arguments is not { Count: 1 } ||
            invocation.Arguments[0] is not StringConstantExpressionAst parameter ||
            string.IsNullOrWhiteSpace(parameter.Value))
            return false;
        parameterName = parameter.Value;
        return true;
    }
}
