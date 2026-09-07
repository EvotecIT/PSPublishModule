namespace PowerForge;

/// <summary>
/// Gives a command with implicit non-terminal records one success-stream return contract.
/// A returned record is written before unwinding finally blocks; the CLR return only exits.
/// </summary>
internal sealed class PowerShellImplicitOutputPass : IPowerShellSemanticPass
{
    public string Id => "08-implicit-success-output";

    public PowerShellBoundProgram Run(PowerShellBoundProgram program)
        => program.WithFunctions(program.Functions.Select(function =>
            PowerShellSemanticAnalyzer.EnumerateStatements(function.Body).Any(static statement =>
                statement is PowerShellBoundStreamWriteStatement { Provider: null })
                ? function.WithBody(RewriteBlock(function.Body))
                : function).ToArray());

    private static PowerShellBoundBlock RewriteBlock(PowerShellBoundBlock block)
    {
        var statements = new List<PowerShellBoundStatement>();
        foreach (var statement in block.Statements)
        {
            if (statement is PowerShellBoundReturnStatement { EmitsValue: true, Expression: not null } returned)
            {
                statements.Add(Output(returned.Span, returned.Expression));
                statements.Add(new PowerShellBoundReturnStatement(returned.Span, null));
            }
            else if (statement is PowerShellBoundExpressionStatement { EmitsOutput: true } expression)
            {
                statements.Add(Output(expression.Span, expression.Expression));
            }
            else
            {
                statements.Add(RewriteNestedBlocks(statement));
            }
        }
        return new PowerShellBoundBlock(block.Span, statements.ToArray());
    }

    private static PowerShellBoundStreamWriteStatement Output(SourceSpan span, PowerShellBoundExpression expression)
        => new(span, PowerShellStreamCommandKind.Success, provider: null, expression);

    private static PowerShellBoundStatement RewriteNestedBlocks(PowerShellBoundStatement statement)
        => statement switch
        {
            PowerShellBoundIfStatement conditional => new PowerShellBoundIfStatement(
                conditional.Span,
                conditional.Clauses.Select(clause => new PowerShellBoundConditionalClause(clause.Condition, RewriteBlock(clause.Body))).ToArray(),
                conditional.ElseBlock is null ? null : RewriteBlock(conditional.ElseBlock)),
            PowerShellBoundWhileStatement loop => new PowerShellBoundWhileStatement(
                loop.Span, loop.Kind, loop.Condition, RewriteBlock(loop.Body)),
            PowerShellBoundForStatement loop => new PowerShellBoundForStatement(
                loop.Span, loop.Initializer, loop.Condition, loop.Iterator, RewriteBlock(loop.Body)),
            PowerShellBoundForEachStatement loop => new PowerShellBoundForEachStatement(
                loop.Span, loop.Variable, loop.ElementType, loop.Collection, loop.ScalarString,
                RewriteBlock(loop.Body), loop.DeclareVariable, loop.NullCollectionElement, loop.SystemArray),
            PowerShellBoundSwitchStatement selection => new PowerShellBoundSwitchStatement(
                selection.Span, selection.Value,
                selection.Clauses.Select(clause => new PowerShellBoundSwitchClause(clause.Value, RewriteBlock(clause.Body))).ToArray(),
                selection.DefaultBlock is null ? null : RewriteBlock(selection.DefaultBlock),
                selection.MatchMode, selection.CaseSensitive),
            PowerShellBoundTryStatement attempted => new PowerShellBoundTryStatement(
                attempted.Span, RewriteBlock(attempted.Body),
                attempted.Catches.Select(clause => new PowerShellBoundCatchClause(clause.ExceptionTypes.ToArray(), RewriteBlock(clause.Body))).ToArray(),
                attempted.FinallyBlock is null ? null : RewriteBlock(attempted.FinallyBlock)),
            _ => statement
        };
}
