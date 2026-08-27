namespace PowerForge;

internal interface IPowerShellSemanticPass
{
    string Id { get; }
    PowerShellBoundProgram Run(PowerShellBoundProgram program);
}

/// <summary>
/// Runs semantic passes in canonical identity order and rejects duplicate owners.
/// </summary>
internal sealed class PowerShellSemanticAnalyzer
{
    private readonly IPowerShellSemanticPass[] _passes;

    internal PowerShellSemanticAnalyzer(IEnumerable<IPowerShellSemanticPass>? passes = null)
    {
        _passes = (passes ?? CreateDefaultPasses())
            .OrderBy(static pass => pass.Id, StringComparer.Ordinal)
            .ToArray();
        var duplicate = _passes.GroupBy(static pass => pass.Id, StringComparer.Ordinal).FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Semantic pass '{duplicate.Key}' is registered more than once.");
    }

    internal PowerShellBoundProgram Analyze(PowerShellBoundProgram program)
    {
        var current = program ?? throw new ArgumentNullException(nameof(program));
        foreach (var pass in _passes) current = pass.Run(current);
        return current;
    }

    private static IEnumerable<IPowerShellSemanticPass> CreateDefaultPasses()
    {
        yield return new DefiniteAssignmentPass();
        yield return new ReturnTypePass();
        yield return new EffectPass();
        yield return new CapabilityPass();
        yield return new FallbackPass();
    }

    private sealed class DefiniteAssignmentPass : IPowerShellSemanticPass
    {
        public string Id => "10-definite-assignment";
        public PowerShellBoundProgram Run(PowerShellBoundProgram program) => program;
    }

    private sealed class ReturnTypePass : IPowerShellSemanticPass
    {
        public string Id => "30-return-type";

        public PowerShellBoundProgram Run(PowerShellBoundProgram program)
            => program.WithFunctions(program.Functions.Select(AnalyzeFunction).ToArray());

        private static PowerShellBoundFunction AnalyzeFunction(PowerShellBoundFunction function)
        {
            var values = function.Body.Statements.Select(statement => statement switch
            {
                PowerShellBoundReturnStatement { Expression: not null } returned => returned.Expression.Type,
                PowerShellBoundExpressionStatement expression => expression.Expression.Type,
                _ => null
            }).Where(static type => type is not null).Cast<PowerShellTypeFact>().ToArray();
            if (values.Length == 0)
                return function.WithAnalysis(returnType: new PowerShellTypeFact(typeof(void), PowerShellTypeFactProvenance.Inferred, "The function has no success output."));
            var first = values[0];
            if (values.All(type => type.ClrType == first.ClrType))
                return function.WithAnalysis(returnType: new PowerShellTypeFact(first.ClrType, PowerShellTypeFactProvenance.Inferred, "All reachable success outputs have the same CLR type."));
            return function.WithAnalysis(
                returnType: PowerShellTypeFact.Unknown,
                disposition: new PowerShellExecutionDisposition(PowerShellExecutionDispositionKind.Fallback, "type.return.heterogeneous", "Reachable success outputs do not share one CLR type."));
        }
    }

    private sealed class EffectPass : IPowerShellSemanticPass
    {
        public string Id => "40-effects";
        public PowerShellBoundProgram Run(PowerShellBoundProgram program)
            => program.WithFunctions(program.Functions.Select(function => function.WithAnalysis(
                effects: function.Body.Statements.Aggregate(PowerShellSemanticEffect.None, static (current, statement) => current | statement.Effects))).ToArray());
    }

    private sealed class CapabilityPass : IPowerShellSemanticPass
    {
        public string Id => "50-capabilities";
        public PowerShellBoundProgram Run(PowerShellBoundProgram program)
            => program.WithFunctions(program.Functions.Select(function => function.WithAnalysis(
                capabilities: function.Body.Statements.Aggregate(PowerShellRequiredCapability.None, static (current, statement) => current | statement.Capabilities))).ToArray());
    }

    private sealed class FallbackPass : IPowerShellSemanticPass
    {
        public string Id => "60-fallback";
        public PowerShellBoundProgram Run(PowerShellBoundProgram program)
            => program.WithFunctions(program.Functions.Select(function =>
                function.ReturnType.Provenance == PowerShellTypeFactProvenance.Unknown
                    ? function.WithAnalysis(disposition: new PowerShellExecutionDisposition(PowerShellExecutionDispositionKind.Fallback, "type.return.unknown", "The function return type is not statically known."))
                    : function).ToArray());
    }
}
