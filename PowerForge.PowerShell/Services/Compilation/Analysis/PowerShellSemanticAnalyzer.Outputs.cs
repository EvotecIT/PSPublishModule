namespace PowerForge;

internal sealed partial class PowerShellSemanticAnalyzer
{
    private sealed class ReturnTypePass : IPowerShellSemanticPass
    {
        public string Id => "30-return-type-fixed-point";

        public PowerShellBoundProgram Run(PowerShellBoundProgram program)
        {
            var functions = program.Functions.ToDictionary(static function => function.Symbol.StableKey, StringComparer.Ordinal);
            var maximumIterations = Math.Max(1, functions.Count + 1);
            for (var iteration = 0; iteration < maximumIterations; iteration++)
            {
                var changed = false;
                foreach (var key in functions.Keys.OrderBy(static key => key, StringComparer.Ordinal).ToArray())
                {
                    var current = functions[key];
                    var next = AnalyzeReturnType(current, functions);
                    if (next.ReturnType.ClrType != current.ReturnType.ClrType ||
                        next.ReturnType.Provenance != current.ReturnType.Provenance ||
                        next.Disposition.Kind != current.Disposition.Kind ||
                        !next.Disposition.ReasonCode.Equals(current.Disposition.ReasonCode, StringComparison.Ordinal))
                    {
                        functions[key] = next;
                        changed = true;
                    }
                }
                if (!changed) break;
            }
            return program.WithFunctions(functions.Values.OrderBy(static function => function.Symbol.StableKey, StringComparer.Ordinal).ToArray());
        }

        private static PowerShellBoundFunction AnalyzeReturnType(
            PowerShellBoundFunction function,
            IReadOnlyDictionary<string, PowerShellBoundFunction> functions)
        {
            var expressions = EnumerateStatements(function.Body).Select(GetCallableReturnExpression).Where(static expression => expression is not null).Cast<PowerShellBoundExpression>().ToArray();
            if (expressions.Length == 0)
                return function.WithAnalysis(returnType: new PowerShellTypeFact(typeof(void), PowerShellTypeFactProvenance.Inferred, "The function has no success output."));
            var facts = expressions.Select(expression => ResolveType(expression, functions)).ToArray();
            if (facts.All(static fact => fact.Provenance != PowerShellTypeFactProvenance.Unknown && fact.ClrType == typeof(void)))
                return function.WithAnalysis(returnType: new PowerShellTypeFact(typeof(void), PowerShellTypeFactProvenance.Inferred, "All reachable success-output expressions are output-free."));
            var known = facts.Where(static fact =>
                fact.Provenance != PowerShellTypeFactProvenance.Unknown &&
                fact.ClrType != typeof(void)).ToArray();
            if (known.Length == 0) return function;
            var first = known[0];
            if (known.All(fact => fact.ClrType == first.ClrType))
                return function.WithAnalysis(returnType: new PowerShellTypeFact(first.ClrType, PowerShellTypeFactProvenance.Inferred, "All reachable success outputs have the same CLR type after call-graph propagation."));
            return function.WithAnalysis(
                returnType: PowerShellTypeFact.Unknown,
                disposition: new PowerShellExecutionDisposition(
                    PowerShellExecutionDispositionKind.Fallback,
                    "type.return.heterogeneous",
                    "Reachable success outputs have branch-specific runtime types and do not share one CLR representation."));
        }
    }

    private sealed class CardinalityPass : IPowerShellSemanticPass
    {
        public string Id => "35-output-cardinality-fixed-point";

        public PowerShellBoundProgram Run(PowerShellBoundProgram program)
        {
            var functions = program.Functions.ToDictionary(static function => function.Symbol.StableKey, StringComparer.Ordinal);
            RunFixedPoint(
                functions,
                (function, lookup) => function.WithAnalysis(outputCardinality: Analyze(function, lookup)),
                static (left, right) => left.OutputCardinality == right.OutputCardinality);
            return program.WithFunctions(functions.Values.OrderBy(static function => function.Symbol.StableKey, StringComparer.Ordinal).ToArray());
        }

        private static PowerShellOutputCardinality Analyze(
            PowerShellBoundFunction function,
            IReadOnlyDictionary<string, PowerShellBoundFunction> functions)
        {
            var statements = EnumerateStatements(function.Body).ToArray();
            var outputs = statements
                .Select(GetSuccessOutputExpression)
                .Where(static expression => expression is not null)
                .Select(expression => Resolve(expression!, functions))
                .ToArray();
            if (outputs.Length == 0) return PowerShellOutputCardinality.None;
            if (outputs.Any(static cardinality => cardinality == PowerShellOutputCardinality.Unknown))
                return PowerShellOutputCardinality.Unknown;
            if (outputs.Any(static cardinality => cardinality == PowerShellOutputCardinality.Collection))
                return PowerShellOutputCardinality.Collection;
            if (outputs.Length > 1 && statements.Any(static statement =>
                    statement is PowerShellBoundStreamWriteStatement { Kind: PowerShellStreamCommandKind.Success }))
                return PowerShellOutputCardinality.Collection;
            return PowerShellOutputCardinality.Scalar;
        }

        private static PowerShellOutputCardinality Resolve(
            PowerShellBoundExpression expression,
            IReadOnlyDictionary<string, PowerShellBoundFunction> functions)
        {
            if (expression is PowerShellBoundInvocationExpression invocation &&
                functions.TryGetValue(invocation.Target.StableKey, out var target))
                return target.OutputCardinality;
            return expression.Type.ClrType.IsArray ? PowerShellOutputCardinality.Collection : expression.Cardinality;
        }
    }
}
