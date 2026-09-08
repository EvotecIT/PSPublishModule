namespace PowerForge;

internal sealed partial class PowerShellSemanticAnalyzer
{
    /// <summary>Closes existing rejections over callers without imposing whole-function eligibility on regions.</summary>
    private static PowerShellBoundProgram PropagateFallbackCalls(PowerShellBoundProgram program)
    {
        var functions = program.Functions.ToDictionary(static function => function.Symbol.StableKey, StringComparer.Ordinal);
        RunFixedPoint(functions, (function, lookup) => ApplyFallbackCallDisposition(function, lookup, program.Diagnostics),
            static (left, right) => left.Disposition.Kind == right.Disposition.Kind && left.Disposition.ReasonCode == right.Disposition.ReasonCode);
        return program.WithFunctions(functions.Values.OrderBy(static function => function.Symbol.StableKey, StringComparer.Ordinal).ToArray());
    }

    private static PowerShellBoundFunction ApplyFallbackCallDisposition(
        PowerShellBoundFunction function,
        IReadOnlyDictionary<string, PowerShellBoundFunction> functions,
        IEnumerable<PowerShellSemanticDiagnostic> diagnostics)
    {
        if (function.Disposition.Kind != PowerShellExecutionDispositionKind.Typed) return function;
        var unresolvedCall = EnumerateStatements(function.Body)
            .SelectMany(EnumerateDirectExpressions)
            .SelectMany(EnumerateInvocations)
            .FirstOrDefault(invocation => !functions.ContainsKey(invocation.Target.StableKey));
        if (unresolvedCall is not null)
            return function.WithAnalysis(disposition: new PowerShellExecutionDisposition(
                PowerShellExecutionDispositionKind.Fallback, "call.binding.unavailable",
                $"Local function '{unresolvedCall.Target.Name}' did not produce a bound function contract."));
        var blocked = GetCallees(function, functions).FirstOrDefault(callee =>
            callee.Disposition.Kind != PowerShellExecutionDispositionKind.Typed || GetBlockingDiagnostic(callee, diagnostics) is not null);
        return blocked is null
            ? function
            : function.WithAnalysis(disposition: new PowerShellExecutionDisposition(
                PowerShellExecutionDispositionKind.Fallback, "call.fallback", $"Local function '{blocked.Symbol.Name}' requires fallback."));
    }

    private static PowerShellSemanticDiagnostic? GetBlockingDiagnostic(PowerShellBoundFunction function,
        IEnumerable<PowerShellSemanticDiagnostic> diagnostics)
        => diagnostics.FirstOrDefault(diagnostic =>
            diagnostic.Span.DocumentId.Equals(function.Symbol.DocumentId, StringComparison.Ordinal) &&
            diagnostic.Span.StartOffset >= function.Symbol.Declaration.StartOffset &&
            diagnostic.Span.StartOffset <= function.Symbol.Declaration.EndOffset);
}
