namespace PowerForge;

internal sealed partial class PowerShellSemanticAnalyzer
{
    /// <summary>Checks value uses after local-call return types have reached their fixed point.</summary>
    private sealed class ValueConsumptionPass : IPowerShellSemanticPass
    {
        public string Id => "37-value-consumption";

        public PowerShellBoundProgram Run(PowerShellBoundProgram program)
        {
            var lookup = program.Functions.ToDictionary(static function => function.Symbol.StableKey, StringComparer.Ordinal);
            var diagnostics = new List<PowerShellSemanticDiagnostic>(program.Diagnostics);
            var functions = program.Functions.Select(function =>
            {
                var invalid = FindVoidValueUse(function, lookup);
                if (invalid is null) return function;
                const string code = "PST2301";
                const string message = "An output-free expression is consumed as a value. PowerShell empty-output capture and conversion must remain hosted until that value contract is qualified.";
                diagnostics.Add(new PowerShellSemanticDiagnostic(code, message, invalid.Span));
                // Region opportunity analysis also runs this pass without the final
                // fallback pass. Invalid value uses must never become typed regions.
                return function.WithAnalysis(disposition: new PowerShellExecutionDisposition(
                    PowerShellExecutionDispositionKind.Fallback, code, message));
            }).ToArray();
            return program.WithFunctions(functions).WithDiagnostics(OrderDiagnostics(diagnostics));
        }

        private static PowerShellBoundExpression? FindVoidValueUse(
            PowerShellBoundFunction function,
            IReadOnlyDictionary<string, PowerShellBoundFunction> functions)
        {
            foreach (var statement in EnumerateStatements(function.Body))
            foreach (var root in EnumerateDirectExpressions(statement))
            {
                if (ResolveType(root, functions).ClrType == typeof(void) && !AllowsOutputFreeRoot(statement, root))
                    return root;
                // Every nested expression supplies an operand, receiver, argument,
                // element, or other value. A void operation has no such CLR value.
                var invalid = EnumerateExpressions(root).Skip(1)
                    .FirstOrDefault(expression => ResolveType(expression, functions).ClrType == typeof(void));
                if (invalid is not null) return invalid;
            }
            return null;
        }

        private static bool AllowsOutputFreeRoot(PowerShellBoundStatement statement, PowerShellBoundExpression expression)
            => statement is PowerShellBoundExpressionStatement or PowerShellBoundReturnStatement or
                   PowerShellBoundStreamWriteStatement { Provider: null } ||
               statement is PowerShellBoundForStatement loop &&
                   (ReferenceEquals(expression, loop.Initializer) || ReferenceEquals(expression, loop.Iterator));
    }
}
