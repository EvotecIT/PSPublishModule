namespace PowerForge;

/// <summary>Propagates error-host requirements and protects authored local-call statements before output rewriting.</summary>
internal sealed class PowerShellStatementErrorCallPass : IPowerShellSemanticPass
{
    public string Id => "07-statement-error-calls";

    public PowerShellBoundProgram Run(PowerShellBoundProgram program)
    {
        var selected = program.Functions.Where(function =>
                function.Capabilities.HasFlag(PowerShellRequiredCapability.PowerShellStatementErrors))
            .Select(static function => function.Symbol.StableKey).ToHashSet(StringComparer.Ordinal);
        if (selected.Count == 0) return program;
        var calls = program.Functions.ToDictionary(static function => function.Symbol.StableKey,
            static function => PowerShellSemanticAnalyzer.EnumerateStatements(function.Body)
                .SelectMany(PowerShellSemanticAnalyzer.EnumerateDirectExpressions)
                .SelectMany(PowerShellSemanticAnalyzer.EnumerateExpressions)
                .OfType<PowerShellBoundInvocationExpression>()
                .Select(static invocation => invocation.Target.StableKey).Distinct(StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);
        PropagateCallers(selected, calls);
        var snapshots = program.Functions.Where(function => PowerShellSemanticAnalyzer.EnumerateStatements(function.Body)
                .SelectMany(PowerShellSemanticAnalyzer.EnumerateDirectExpressions)
                .SelectMany(PowerShellSemanticAnalyzer.EnumerateExpressions)
                .OfType<PowerShellBoundRuntimeStateExpression>().Any(IsErrorStateSnapshot))
            .Select(static function => function.Symbol.StableKey).ToHashSet(StringComparer.Ordinal);
        PropagateCallers(snapshots, calls);
        var diagnostics = program.Diagnostics.ToArray().ToList();
        foreach (var function in program.Functions.Where(function => selected.Contains(function.Symbol.StableKey) && snapshots.Contains(function.Symbol.StableKey)))
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSE2401",
                "Statement errors can change $Error and ErrorActionPreference during execution. An invocation-entry runtime-state snapshot cannot preserve those reads through a local-call closure; this function remains on the PowerShell runtime path.",
                function.Symbol.Declaration));

        var documents = program.Documents.ToDictionary(static document => document.DocumentId, StringComparer.Ordinal);
        PowerShellBoundBlock RewriteBlock(PowerShellBoundBlock block, bool alreadyProtected = false)
        {
            return new PowerShellBoundBlock(block.Span, block.Statements.Select(statement =>
            {
                if (statement is PowerShellBoundStatementErrorBoundary boundary)
                    return (PowerShellBoundStatement)new PowerShellBoundStatementErrorBoundary(
                        RewriteBlock(boundary.Body, true), boundary.SourcePath, boundary.SourceText);
                // A capture's top-level RHS is part of its assignment, not a
                // separately resumable statement. Preserve inner body boundaries
                // without inserting an error handler that would complete the RHS.
                var rewritten = statement is PowerShellBoundOutputCaptureStatement capture
                    ? new PowerShellBoundOutputCaptureStatement(capture.Span, capture.Target, RewriteBlock(capture.Body, true))
                    : PowerShellBoundStatementRewriter.RewriteNestedBlocks(statement, nested => RewriteBlock(nested));
                if (alreadyProtected) return rewritten;
                var callsErrorHost = PowerShellSemanticAnalyzer.EnumerateDirectExpressions(rewritten)
                    .SelectMany(PowerShellSemanticAnalyzer.EnumerateExpressions)
                    .OfType<PowerShellBoundInvocationExpression>().Any(call => selected.Contains(call.Target.StableKey));
                if (!callsErrorHost && !rewritten.Capabilities.HasFlag(PowerShellRequiredCapability.PowerShellStatementErrors)) return rewritten;
                var document = documents[rewritten.Span.DocumentId];
                var text = string.Join("\n", document.SourceText.Replace("\r\n", "\n").Split('\n')
                    .Skip(rewritten.Span.StartLine - 1).Take(rewritten.Span.EndLine - rewritten.Span.StartLine + 1));
                return new PowerShellBoundStatementErrorBoundary(new PowerShellBoundBlock(rewritten.Span, new[] { rewritten }), document.Path, text);
            }).ToArray());
        }
        return program.WithFunctions(program.Functions.Select(function => selected.Contains(function.Symbol.StableKey)
            ? function.WithBody(RewriteBlock(function.Body)) : function).ToArray()).WithDiagnostics(diagnostics.ToArray());
    }

    private static bool IsErrorStateSnapshot(PowerShellBoundRuntimeStateExpression state)
        => state.Kind == PowerShellRuntimeStateIntrinsicKind.ErrorCollection ||
           state.Kind == PowerShellRuntimeStateIntrinsicKind.ActionPreference &&
           state.Arguments.Any(argument => argument is PowerShellBoundLiteralExpression { Value: string name } &&
               name.Equals("ErrorActionPreference", StringComparison.OrdinalIgnoreCase));

    private static void PropagateCallers(HashSet<string> selected, IReadOnlyDictionary<string, string[]> calls)
    {
        bool changed;
        do
        {
            changed = false;
            foreach (var pair in calls)
                if (pair.Value.Any(selected.Contains)) changed |= selected.Add(pair.Key);
        } while (changed);
    }
}
