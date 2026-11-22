using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Describes explicit execution-status writes after a successfully completed native-hosted statement.</summary>
internal static class PowerShellNativeStatementStatusPolicy
{
    internal static bool? OnCompletion(StatementAst syntax, PowerShellBoundStatement bound, bool windowsPowerShell)
    {
        // These commands use emitted stream sinks instead of a native command pipeline.
        if (bound is PowerShellBoundStreamWriteStatement { Provider: not null } stream)
            return stream.Kind != PowerShellStreamCommandKind.Error;
        return NeedsSuccessWrite(syntax, windowsPowerShell) ? true : null;
    }

    internal static bool NeedsSuccessWrite(StatementAst syntax, bool windowsPowerShell)
    {
        while (syntax is AssignmentStatementAst assignment) syntax = assignment.Right;
        return syntax switch
        {
            CommandExpressionAst expression => NeedsSuccessWrite(expression.Expression, windowsPowerShell),
            PipelineAst { PipelineElements.Count: 1 } pipeline when pipeline.PipelineElements[0] is CommandExpressionAst expression
                => NeedsSuccessWrite(expression.Expression, windowsPowerShell),
            _ => false
        };
    }

    private static bool NeedsSuccessWrite(ExpressionAst expression, bool windowsPowerShell)
    {
        // Windows PowerShell resets status for expression wrappers; PowerShell 7 preserves nested pipeline status.
        if (windowsPowerShell) return true;
        return expression switch
        {
            ParenExpressionAst parenthesized => NeedsSuccessWrite(parenthesized.Pipeline, windowsPowerShell),
            SubExpressionAst subexpression => subexpression.SubExpression.Statements.Count == 0,
            ArrayExpressionAst array when array.SubExpression.Statements.Count == 0 => true,
            ArrayExpressionAst array => array.SubExpression.Statements.Count == 1 && array.SubExpression.Traps is null &&
                NeedsSuccessWrite(array.SubExpression.Statements[0], windowsPowerShell),
            _ => true
        };
    }
}
