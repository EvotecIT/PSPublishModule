namespace PowerForge;

internal sealed partial class PowerShellSemanticAnalyzer
{
    private static IEnumerable<(PowerShellSymbolId Target, SourceSpan Span)> EnumerateFunctionReferences(
        PowerShellBoundExpression root)
    {
        foreach (var expression in EnumerateExpressions(root))
        {
            if (expression is PowerShellBoundInvocationExpression
                { ResultProjection: PowerShellLocalCallResultProjection.None } call)
                yield return (call.Target, call.Span);
            else if (expression is PowerShellBoundNativeScriptBlockExpression block)
                yield return (block.Target, block.Span);
        }
    }
}
