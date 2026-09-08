namespace PowerForge;

/// <summary>
/// Gives a command with implicit non-terminal records one success-stream return contract.
/// A returned record is written before unwinding finally blocks; the CLR return only exits.
/// </summary>
internal sealed class PowerShellImplicitOutputPass : IPowerShellSemanticPass
{
    public string Id => "08-implicit-success-output";

    public PowerShellBoundProgram Run(PowerShellBoundProgram program)
    {
        var commandHost = program.TargetCapabilities.HasFlag(PowerShellCompilationCapability.PowerShellStreams) &&
            program.TargetCapabilities.HasFlag(PowerShellCompilationCapability.PipelineParameterBinding);
        var selected = program.Functions.Where(function =>
                function.NativeFunctionBinding is not null || function.Capabilities.HasFlag(PowerShellRequiredCapability.PowerShellStatementErrors) ||
                PowerShellSemanticAnalyzer.EnumerateStatements(function.Body).Any(static statement =>
                    statement is PowerShellBoundStreamWriteStatement { Provider: null }) ||
                commandHost && function.Body.Effects.HasFlag(PowerShellSemanticEffect.SuccessOutput) &&
                PowerShellSemanticAnalyzer.EnumerateStatements(function.Body).Any(static statement =>
                    statement is PowerShellBoundTryStatement { FinallyBlock: not null }))
            .Select(static function => function.Symbol.StableKey).ToHashSet(StringComparer.Ordinal);
        var calls = program.Functions.ToDictionary(static function => function.Symbol.StableKey,
            static function => PowerShellSemanticAnalyzer.EnumerateStatements(function.Body)
                .SelectMany(PowerShellSemanticAnalyzer.EnumerateDirectExpressions)
                .SelectMany(PowerShellSemanticAnalyzer.EnumerateExpressions)
                .OfType<PowerShellBoundInvocationExpression>()
                .Select(static invocation => invocation.Target.StableKey).Distinct(StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);
        // A caller must use the same output contract before its own return can unwind.
        // Compute this from canonical bound calls before CLR return-type propagation.
        bool changed;
        do
        {
            changed = false;
            foreach (var function in program.Functions)
                if (!selected.Contains(function.Symbol.StableKey) && calls[function.Symbol.StableKey].Any(selected.Contains))
                    changed |= selected.Add(function.Symbol.StableKey);
        } while (changed);
        return program.WithFunctions(program.Functions.Select(function => selected.Contains(function.Symbol.StableKey)
            ? function.WithBody(RewriteBlock(function.Body, function.NativeFunctionBinding is not null))
            : function).ToArray());
    }

    private static PowerShellBoundBlock RewriteBlock(PowerShellBoundBlock block, bool usesNativeInvocation)
    {
        var statements = new List<PowerShellBoundStatement>();
        foreach (var statement in block.Statements)
        {
            if (statement is PowerShellBoundReturnStatement { EmitsValue: true, Expression: not null } returned)
            {
                statements.Add(Output(returned.Span, returned.Expression, usesNativeInvocation));
                statements.Add(new PowerShellBoundReturnStatement(returned.Span, null));
            }
            else if (statement is PowerShellBoundExpressionStatement { EmitsOutput: true } expression)
            {
                statements.Add(Output(expression.Span, expression.Expression, usesNativeInvocation));
            }
            else if (usesNativeInvocation && statement is PowerShellBoundStreamWriteStatement { Provider: null } stream)
            {
                statements.Add(Output(stream.Span, stream.Message, usesNativeInvocation));
            }
            else
            {
                statements.Add(PowerShellBoundStatementRewriter.RewriteNestedBlocks(statement, nested => RewriteBlock(nested, usesNativeInvocation)));
            }
        }
        return new PowerShellBoundBlock(block.Span, statements.ToArray());
    }

    private static PowerShellBoundStreamWriteStatement Output(SourceSpan span, PowerShellBoundExpression expression, bool usesNativeInvocation)
        => new(span, PowerShellStreamCommandKind.Success, provider: null, expression, usesNativeInvocation: usesNativeInvocation);

}
