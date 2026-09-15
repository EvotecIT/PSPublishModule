namespace PowerForge;

/// <summary>
/// Gives a command with implicit non-terminal records one success-stream return contract.
/// A returned record is written before unwinding finally blocks; the CLR return only exits.
/// </summary>
internal sealed class PowerShellImplicitOutputPass : IPowerShellSemanticPass
{
    public string Id => "06-implicit-success-output";

    public PowerShellBoundProgram Run(PowerShellBoundProgram program)
    {
        var commandHost = program.TargetCapabilities.HasFlag(PowerShellCompilationCapability.PowerShellStreams) &&
            program.TargetCapabilities.HasFlag(PowerShellCompilationCapability.PipelineParameterBinding);
        var selected = program.Functions.Where(function =>
                function.NativeFunctionBinding is not null || function.Capabilities.HasFlag(PowerShellRequiredCapability.PowerShellStatementErrors) ||
                PowerShellSemanticAnalyzer.EnumerateStatements(function.Body).Any(static statement =>
                    statement is PowerShellBoundStreamWriteStatement { Provider: null }) ||
                commandHost && PowerShellSemanticAnalyzer.EnumerateStatements(function.Body).Any(static statement =>
                    PowerShellSemanticAnalyzer.GetSuccessOutputExpression(statement) is { } output &&
                    output is not PowerShellBoundInvocationExpression && output.Type.ClrType != typeof(void) &&
                    !PowerShellStableScalarTypePolicy.IsSupported(output.Type)) ||
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
        var documents = program.Documents.ToDictionary(static document => document.DocumentId, StringComparer.Ordinal);
        var commandEnumeration = program.TargetCapabilities.HasFlag(PowerShellCompilationCapability.PowerShellStatementErrors);
        return program.WithFunctions(program.Functions.Select(function => selected.Contains(function.Symbol.StableKey)
            ? function.WithBody(RewriteBlock(function.Body, function.NativeFunctionBinding is not null,
                commandEnumeration, documents[function.Symbol.DocumentId]))
            : function).ToArray());
    }

    private static PowerShellBoundBlock RewriteBlock(PowerShellBoundBlock block, bool usesNativeInvocation,
        bool commandEnumeration, PowerShellBoundSourceDocument document, bool alreadyProtected = false)
    {
        var statements = new List<PowerShellBoundStatement>();
        foreach (var statement in block.Statements)
        {
            if (statement is PowerShellBoundStatementErrorBoundary boundary)
            {
                statements.Add(new PowerShellBoundStatementErrorBoundary(
                    RewriteBlock(boundary.Body, usesNativeInvocation, commandEnumeration, document, true),
                    boundary.SourcePath, boundary.SourceText, boundary.NativeSuccessStatus, boundary.NativeSequencePoint));
            }
            else if (statement is PowerShellBoundReturnStatement { EmitsSuccessOutput: true, Expression: not null } returned)
            {
                var output = Output(returned.Span, returned.Expression, usesNativeInvocation, commandEnumeration);
                var exit = new PowerShellBoundReturnStatement(returned.Span, null);
                if (output.UsesCommandHostEnumeration && !alreadyProtected)
                    statements.Add(Protect(new PowerShellBoundStatement[] { output, exit }, returned.Span, document));
                else
                {
                    statements.Add(output);
                    statements.Add(exit);
                }
            }
            else if (statement is PowerShellBoundExpressionStatement { EmitsOutput: true } expression)
            {
                statements.Add(Output(expression.Span, expression.Expression, usesNativeInvocation, commandEnumeration));
            }
            else if (statement is PowerShellBoundStreamWriteStatement { Provider: null } stream)
            {
                statements.Add(Output(stream.Span, stream.Message, usesNativeInvocation, commandEnumeration));
            }
            else
            {
                statements.Add(PowerShellBoundStatementRewriter.RewriteNestedBlocks(statement,
                    nested => RewriteBlock(nested, usesNativeInvocation, commandEnumeration, document)));
            }
        }
        return new PowerShellBoundBlock(block.Span, statements.ToArray());
    }

    private static PowerShellBoundStreamWriteStatement Output(SourceSpan span, PowerShellBoundExpression expression,
        bool usesNativeInvocation, bool commandEnumeration)
        => new(span, PowerShellStreamCommandKind.Success, provider: null, expression, usesNativeInvocation: usesNativeInvocation,
            usesCommandHostEnumeration: !usesNativeInvocation && commandEnumeration &&
                expression.Type.ClrType != typeof(void) &&
                !PowerShellStableScalarTypePolicy.IsSupported(expression.Type));

    // A failed returned output must resume after the authored return statement,
    // rather than execute the CLR exit after its output operation has failed.
    private static PowerShellBoundStatementErrorBoundary Protect(PowerShellBoundStatement[] statements,
        SourceSpan span, PowerShellBoundSourceDocument document)
        => new(new PowerShellBoundBlock(span, statements), document.Path,
            string.Join("\n", document.SourceText.Replace("\r\n", "\n").Split('\n')
                .Skip(span.StartLine - 1).Take(span.EndLine - span.StartLine + 1)));

}
