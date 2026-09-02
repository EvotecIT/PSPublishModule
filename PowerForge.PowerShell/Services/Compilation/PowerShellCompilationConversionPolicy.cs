using System.Management.Automation.Language;

namespace PowerForge;

internal static class PowerShellCompilationConversionPolicy
{
    internal static bool CanLower(
        ConvertExpressionAst conversion,
        string? targetFramework,
        PowerShellCompilationCapability capabilities)
    {
        var targetType = conversion.StaticType;
        if (targetType == typeof(void))
            return IsStatementDiscard(conversion);
        if (!PowerShellCompilationParameterTypePolicy.CanUseInMethod(targetType, targetFramework, capabilities))
            return false;

        return PowerShellCompilationLiteralPolicy.TryResolve(conversion, targetType, targetFramework, out _) ||
               capabilities.HasFlag(PowerShellCompilationCapability.PowerShellLanguageConversions);
    }

    internal static bool IsStatementDiscard(ConvertExpressionAst conversion)
        => conversion.StaticType == typeof(void) &&
           conversion.Parent is CommandExpressionAst commandExpression &&
           commandExpression.Parent is PipelineAst { PipelineElements.Count: 1 } pipeline &&
           ReferenceEquals(pipeline.PipelineElements[0], commandExpression) &&
           IsSupportedStatementBody(pipeline.Parent);

    private static bool IsSupportedStatementBody(Ast? body)
    {
        for (var current = body; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case NamedBlockAst:
                    return true;
                case StatementBlockAst:
                case IfStatementAst:
                case WhileStatementAst:
                case DoWhileStatementAst:
                case DoUntilStatementAst:
                case ForStatementAst:
                case ForEachStatementAst:
                case SwitchStatementAst:
                case TryStatementAst:
                case CatchClauseAst:
                    continue;
                default:
                    return false;
            }
        }

        return false;
    }
}
