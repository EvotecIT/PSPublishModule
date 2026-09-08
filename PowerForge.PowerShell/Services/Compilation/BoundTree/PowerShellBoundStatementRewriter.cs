namespace PowerForge;

/// <summary>Rebuilds statement-owned blocks while preserving the owning statement contract.</summary>
internal static class PowerShellBoundStatementRewriter
{
    internal static PowerShellBoundStatement RewriteNestedBlocks(PowerShellBoundStatement statement, Func<PowerShellBoundBlock, PowerShellBoundBlock> rewriteBlock)
        => statement switch
        {
            PowerShellBoundOutputCaptureStatement capture => new PowerShellBoundOutputCaptureStatement(
                capture.Span, capture.Target, rewriteBlock(capture.Body)),
            PowerShellBoundStatementErrorBoundary boundary => new PowerShellBoundStatementErrorBoundary(
                rewriteBlock(boundary.Body), boundary.SourcePath, boundary.SourceText, boundary.NativeSuccessStatus),
            PowerShellBoundIfStatement conditional => new PowerShellBoundIfStatement(
                conditional.Span,
                conditional.Clauses.Select(clause => new PowerShellBoundConditionalClause(clause.Condition, rewriteBlock(clause.Body))).ToArray(),
                conditional.ElseBlock is null ? null : rewriteBlock(conditional.ElseBlock)),
            PowerShellBoundWhileStatement loop => new PowerShellBoundWhileStatement(
                loop.Span, loop.Kind, loop.Condition, rewriteBlock(loop.Body), loop.CheckHostInterrupts),
            PowerShellBoundForStatement loop => new PowerShellBoundForStatement(
                loop.Span, loop.Initializer, loop.Condition, loop.Iterator, rewriteBlock(loop.Body), loop.CheckHostInterrupts),
            PowerShellBoundForEachStatement loop => new PowerShellBoundForEachStatement(
                loop.Span, loop.Variable, loop.ElementType, loop.Collection, loop.EnumerationKind,
                rewriteBlock(loop.Body), loop.DeclareVariable, loop.NullCollectionElement, loop.CheckHostInterrupts),
            PowerShellBoundSwitchStatement selection => new PowerShellBoundSwitchStatement(
                selection.Span, selection.Value,
                selection.Clauses.Select(clause => new PowerShellBoundSwitchClause(clause.Value, rewriteBlock(clause.Body))).ToArray(),
                selection.DefaultBlock is null ? null : rewriteBlock(selection.DefaultBlock),
                selection.MatchMode, selection.CaseSensitive),
            PowerShellBoundTryStatement attempted => new PowerShellBoundTryStatement(
                attempted.Span, rewriteBlock(attempted.Body),
                attempted.Catches.Select(clause => new PowerShellBoundCatchClause(clause.ExceptionTypes.ToArray(), rewriteBlock(clause.Body))).ToArray(),
                attempted.FinallyBlock is null ? null : rewriteBlock(attempted.FinallyBlock), attempted.SuspendHostStopping),
            _ => statement
        };
}
