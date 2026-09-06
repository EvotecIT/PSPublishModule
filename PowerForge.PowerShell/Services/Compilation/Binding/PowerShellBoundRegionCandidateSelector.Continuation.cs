using System.Management.Automation.Language;

namespace PowerForge;

internal static partial class PowerShellBoundRegionCandidateSelector
{
    /// <summary>
    /// Selects a prefix that initializes and computes scalar locals. Every introduced local is
    /// transferred back in declaration order, including locals observed only by unbound later code.
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
        if (HasNamedLifecycle(syntax.Body) || bindings.Count == 0 || bindings[0].AuthoredStatementIndex != 0)
            return false;
        var selected = new List<PowerShellBoundStatement>();
        var locals = new List<PowerShellBoundLocal>();
        var transfers = new List<PowerShellCompiledRegionLocal>();
        var nextIndex = 0;
        foreach (var binding in bindings)
        {
            if (binding.AuthoredStatementIndex != nextIndex ||
                binding.AuthoredStatementEndIndex >= authoredStatements.Count - 1 ||
                (binding.Statement.Effects & ~PowerShellSemanticEffect.Mutation) != 0 ||
                binding.Statement.Capabilities != PowerShellRequiredCapability.None)
                break;
            var candidateLocals = locals.ToList();
            PowerShellCompiledRegionLocal? newTransfer = null;
            if (binding.Statement is PowerShellBoundAssignmentStatement assignment &&
                !locals.Any(local => local.Symbol.StableKey == assignment.Target.StableKey))
            {
                if (!TryCreateContinuationLocal(assignment, authoredStatements[binding.AuthoredStatementIndex], out newTransfer))
                    break;
                candidateLocals.Add(new PowerShellBoundLocal(assignment.Target, assignment.Value.Type));
            }
            var nested = PowerShellSemanticAnalyzer.EnumerateStatements(
                new PowerShellBoundBlock(binding.Statement.Span, new[] { binding.Statement })).ToArray();
            if (nested.OfType<PowerShellBoundAssignmentStatement>().Any(update =>
                    !candidateLocals.Any(local => local.Symbol.StableKey == update.Target.StableKey &&
                                                  local.Type.ClrType == update.Value.Type.ClrType)) ||
                authoredStatements[binding.AuthoredStatementIndex].FindAll(
                    static node => node is AssignmentStatementAst { Left: AttributedExpressionAst },
                    searchNestedScriptBlocks: false).Any(node =>
                    newTransfer is null || !ReferenceEquals(node, authoredStatements[binding.AuthoredStatementIndex])))
                break;
            if (nested.Any(static statement => statement is PowerShellBoundReturnStatement or PowerShellBoundThrowStatement) ||
                PowerShellBoundRegionOpportunitySelector.EnumerateWrittenSymbols(new[] { binding.Statement })
                    .Any(symbol => !candidateLocals.Any(local => local.Symbol.StableKey == symbol.StableKey)))
                break;
            var expressions = nested.SelectMany(PowerShellSemanticAnalyzer.EnumerateDirectExpressions)
                .SelectMany(PowerShellSemanticAnalyzer.EnumerateExpressions).ToArray();
            if (expressions.Any(static expression => expression is PowerShellBoundInvocationExpression) ||
                expressions.OfType<PowerShellBoundVariableExpression>().Any(read =>
                    read.Symbol.Kind == PowerShellSymbolKind.Local &&
                    !candidateLocals.Any(local => local.Symbol.StableKey == read.Symbol.StableKey &&
                                                  local.Type.ClrType == read.Type.ClrType)))
                break;
            selected.Add(binding.Statement);
            locals = candidateLocals;
            if (newTransfer is not null) transfers.Add(newTransfer);
            nextIndex = binding.AuthoredStatementEndIndex + 1;
        }
        if (selected.Count < 2 || locals.Count == 0) return false;
        var lastSpan = selected[selected.Count - 1].Span;
        // The synthetic transfer occurs after the last authored assignment. Giving it that
        // assignment's start offset would falsely classify the newly initialized local as live-in.
        var transferSpan = new SourceSpan(lastSpan.DocumentId, lastSpan.EndOffset, lastSpan.EndOffset,
            lastSpan.EndLine, lastSpan.EndColumn, lastSpan.EndLine, lastSpan.EndColumn);
        var values = locals.Select(local => (PowerShellBoundExpression)new PowerShellBoundVariableExpression(
            transferSpan, local.Symbol, local.Type)).ToArray();
        var result = values.Length == 1 ? values[0] : new PowerShellBoundArrayExpression(
            transferSpan, typeof(object[]), PowerShellBoundArrayKind.Literal, values);
        selected.Add(new PowerShellBoundReturnStatement(transferSpan, result));
        return TryCreateBound(document, syntax, sourceFunction, parameters, locals, selected.ToArray(),
            out candidate, transfers.ToArray());
    }

    private static bool TryCreateContinuationLocal(
        PowerShellBoundAssignmentStatement assignment,
        StatementAst authoredStatement,
        out PowerShellCompiledRegionLocal? transfer)
    {
        transfer = null;
        if (assignment.Operation != PowerShellBoundMutationOperator.Assign ||
            assignment.Target.Kind != PowerShellSymbolKind.Local ||
            !IsSimpleVariableName(assignment.Target.Name) ||
            !PowerShellStableScalarTypePolicy.IsSupported(assignment.Value.Type.ClrType) ||
            authoredStatement is not AssignmentStatementAst authoredAssignment)
            return false;
        // Validation and transformation attributes can execute user behavior during binding.
        var constrained = false;
        var constraintSyntax = string.Empty;
        var target = authoredAssignment.Left;
        if (target is AttributedExpressionAst attributed)
        {
            if (attributed.Attribute is not TypeConstraintAst typeConstraint ||
                attributed.Child is not VariableExpressionAst ||
                typeConstraint.TypeName.GetReflectionType() != assignment.Value.Type.ClrType)
                return false;
            constrained = true;
            constraintSyntax = typeConstraint.Extent.Text;
            target = attributed.Child;
        }
        if (target is not VariableExpressionAst variable || !variable.VariablePath.IsUnqualified)
            return false;
        transfer = new PowerShellCompiledRegionLocal(assignment.Target.Name,
            assignment.Value.Type.ClrType.FullName ?? assignment.Value.Type.ClrType.Name, constrained, constraintSyntax);
        return true;
    }
}
