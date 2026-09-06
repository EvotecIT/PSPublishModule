using System.Management.Automation.Language;

namespace PowerForge;

internal static partial class PowerShellBoundRegionCandidateSelector
{
    /// <summary>
    /// Selects a prefix that initializes and computes one local scalar. The helper's scalar result
    /// transfers that local back to the retained function; it is not PowerShell success output.
    /// </summary>
    internal static bool TryCreateContinuation(
        ParsedSourceDocument document,
        FunctionDefinitionAst syntax,
        PowerShellSymbolId sourceFunction,
        IReadOnlyList<PowerShellBoundParameter> parameters,
        IReadOnlyList<StatementAst> authoredStatements,
        IReadOnlyList<PowerShellBoundStatementBinding> bindings,
        out PowerShellBoundRegionCandidate candidate)
    {
        candidate = null!;
        if (HasNamedLifecycle(syntax.Body) || bindings.Count == 0 ||
            bindings[0].AuthoredStatementIndex != 0 ||
            bindings[0].Statement is not PowerShellBoundAssignmentStatement initial ||
            initial.Operation != PowerShellBoundMutationOperator.Assign ||
            initial.Target.Kind != PowerShellSymbolKind.Local ||
            !IsSimpleVariableName(initial.Target.Name) ||
            !PowerShellStableScalarTypePolicy.IsSupported(initial.Value.Type.ClrType) ||
            authoredStatements[0] is not AssignmentStatementAst authoredAssignment)
            return false;

        // Preserve an authored type constraint on the receiving variable. Validation and
        // transformation attributes have observable binding behavior outside this contract.
        var constraint = string.Empty;
        var target = authoredAssignment.Left;
        if (target is AttributedExpressionAst attributed)
        {
            if (attributed.Attribute is not TypeConstraintAst typeConstraint ||
                attributed.Child is not VariableExpressionAst)
                return false;
            var constrainedType = typeConstraint.TypeName.GetReflectionType();
            if (constrainedType != initial.Value.Type.ClrType) return false;
            constraint = constrainedType.FullName ?? constrainedType.Name;
            target = attributed.Child;
        }
        if (target is not VariableExpressionAst variable || !variable.VariablePath.IsUnqualified)
            return false;

        var selected = new List<PowerShellBoundStatement>();
        var nextIndex = 0;
        foreach (var binding in bindings)
        {
            if (binding.AuthoredStatementIndex != nextIndex ||
                binding.AuthoredStatementEndIndex >= authoredStatements.Count - 1 ||
                (binding.Statement.Effects & ~PowerShellSemanticEffect.Mutation) != 0 ||
                binding.Statement.Capabilities != PowerShellRequiredCapability.None)
                break;
            var nested = PowerShellSemanticAnalyzer.EnumerateStatements(
                new PowerShellBoundBlock(binding.Statement.Span, new[] { binding.Statement })).ToArray();
            if (nested.Any(static statement => statement is PowerShellBoundReturnStatement or PowerShellBoundThrowStatement) ||
                PowerShellBoundRegionOpportunitySelector.EnumerateWrittenSymbols(new[] { binding.Statement })
                    .Any(symbol => symbol.StableKey != initial.Target.StableKey))
                break;
            var expressions = nested.SelectMany(PowerShellSemanticAnalyzer.EnumerateDirectExpressions)
                .SelectMany(PowerShellSemanticAnalyzer.EnumerateExpressions).ToArray();
            if (expressions.Any(static expression => expression is PowerShellBoundInvocationExpression) ||
                expressions.OfType<PowerShellBoundVariableExpression>().Any(read =>
                    read.Symbol.Kind == PowerShellSymbolKind.Local &&
                    (read.Symbol.StableKey != initial.Target.StableKey ||
                     read.Type.ClrType != initial.Value.Type.ClrType)))
                break;
            selected.Add(binding.Statement);
            nextIndex = binding.AuthoredStatementEndIndex + 1;
        }
        if (selected.Count < 2) return false;
        var lastSpan = selected[selected.Count - 1].Span;
        selected.Add(new PowerShellBoundReturnStatement(lastSpan,
            new PowerShellBoundVariableExpression(lastSpan, initial.Target, initial.Value.Type)));
        return TryCreateBound(document, syntax, sourceFunction, parameters,
            new[] { new PowerShellBoundLocal(initial.Target, initial.Value.Type) }, selected.ToArray(),
            out candidate, initial.Target.Name, constraint);
    }
}
