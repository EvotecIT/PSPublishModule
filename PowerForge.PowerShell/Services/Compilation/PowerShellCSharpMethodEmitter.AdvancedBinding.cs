using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellCSharpMethodEmitter
{
    private Type EnsureBoundParametersAvailable(InvokeMemberExpressionAst invocation)
    {
        if (!_capabilities.HasFlag(PowerShellCompilationCapability.BoundParameters))
            throw Error(invocation, "$PSBoundParameters requires a generated host that preserves explicit binding metadata.");
        if (!PowerShellBoundParametersPolicy.TryGetContainsKey(invocation, out var parameterName) ||
            !_parameterMetadata.ContainsKey(parameterName))
            throw Error(invocation, "$PSBoundParameters.ContainsKey() requires the literal canonical name of a parameter declared by this compiled unit.");
        return typeof(bool);
    }

    private string EmitBoundParameterContainsKey(InvokeMemberExpressionAst invocation, string parameterName)
    {
        _ = EnsureBoundParametersAvailable(invocation);
        return $"__boundParameters.Contains({PowerShellCSharpLiteral.QuoteString(parameterName)})";
    }
}
