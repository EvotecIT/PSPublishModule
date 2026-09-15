using System.Management.Automation.Language;

namespace PowerForge;

internal static partial class PowerShellBoundRegionCandidateSelector
{
    /// <summary>
    /// Selects the first statement-aligned typed run after a guarded prefix and before a later
    /// retained statement. Every local crossing either edge must already have an authored stable
    /// scalar constraint, and every mutated local is returned to the retained continuation.
    /// </summary>
    internal static bool TryCreateDetachedContinuation(
        ParsedSourceDocument document,
        FunctionDefinitionAst syntax,
        PowerShellSymbolId sourceFunction,
        IReadOnlyList<PowerShellBoundParameter> parameters,
        IReadOnlyList<PowerShellBoundLocal> functionLocals,
        IReadOnlyList<StatementAst> authoredStatements,
        IReadOnlyList<PowerShellBoundStatementBinding> bindings,
        PowerShellBoundRegionCandidate guardedPrefix,
        out PowerShellBoundRegionCandidate candidate)
    {
        candidate = null!;
        if (!guardedPrefix.RequiresLocalOwnershipGuard ||
            !PowerShellSourceSemanticValidator.SupportsDetachedFunctionMetadata(document) ||
            syntax.IsFilter || syntax.IsWorkflow || HasNamedLifecycle(syntax.Body) ||
            syntax.Body.EndBlock?.Traps is { Count: > 0 })
            return false;

        var ordered = bindings.OrderBy(static binding => binding.AuthoredStatementIndex).ToArray();
        var runs = new List<List<PowerShellBoundStatementBinding>>();
        foreach (var binding in ordered)
        {
            if (binding.Statement.Span.StartOffset <= guardedPrefix.RegionFunction.Body.Span.EndOffset)
                continue;
            if (runs.Count == 0 || binding.AuthoredStatementIndex >
                runs[runs.Count - 1][runs[runs.Count - 1].Count - 1].AuthoredStatementEndIndex + 1)
                runs.Add(new List<PowerShellBoundStatementBinding>());
            runs[runs.Count - 1].Add(binding);
        }

        foreach (var run in runs.Where(run => run.Count >= 2 &&
                     run[0].AuthoredStatementIndex > 0 &&
                     run[run.Count - 1].AuthoredStatementEndIndex < authoredStatements.Count - 1))
        {
            if (TryCreateDetachedRun(document, syntax, sourceFunction, parameters, functionLocals,
                    authoredStatements, run, out candidate))
                return true;
        }
        return false;
    }

    private static bool TryCreateDetachedRun(
        ParsedSourceDocument document,
        FunctionDefinitionAst syntax,
        PowerShellSymbolId sourceFunction,
        IReadOnlyList<PowerShellBoundParameter> parameters,
        IReadOnlyList<PowerShellBoundLocal> functionLocals,
        IReadOnlyList<StatementAst> authoredStatements,
        IReadOnlyList<PowerShellBoundStatementBinding> run,
        out PowerShellBoundRegionCandidate candidate)
    {
        candidate = null!;
        var rawStatements = run.Select(static binding => binding.Statement).ToArray();
        var localSymbols = PowerShellBoundRegionOpportunitySelector.EnumerateReadSymbols(rawStatements)
            .Concat(PowerShellBoundRegionOpportunitySelector.EnumerateWrittenSymbols(rawStatements))
            .Where(static symbol => symbol.Kind == PowerShellSymbolKind.Local)
            .GroupBy(static symbol => symbol.StableKey, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToArray();
        if (localSymbols.Length == 0) return false;

        var localParameters = new List<PowerShellBoundParameter>();
        var inputLocals = new List<PowerShellCompiledRegionLocal>();
        foreach (var local in functionLocals.Where(local => localSymbols.Any(symbol =>
                     symbol.StableKey == local.Symbol.StableKey)))
        {
            if (local.Type.Provenance is PowerShellTypeFactProvenance.Unknown or PowerShellTypeFactProvenance.Int32OrDouble ||
                !PowerShellStableScalarTypePolicy.IsSupported(local.Type.ClrType) ||
                !TryFindEstablishedConstraint(authoredStatements, run[0].AuthoredStatementIndex, local, out var constraintSyntax))
                return false;
            localParameters.Add(new PowerShellBoundParameter(
                local.Symbol,
                local.Type,
                new PowerShellCompilationParameter(
                    local.Symbol.Name,
                    local.Type.ClrType.FullName ?? local.Type.ClrType.Name,
                    hasDefaultValue: false)));
            inputLocals.Add(new PowerShellCompiledRegionLocal(
                local.Symbol.Name,
                local.Type.ClrType.FullName ?? local.Type.ClrType.Name,
                hasTypeConstraint: true,
                constraintSyntax));
        }
        if (localParameters.Count != localSymbols.Length) return false;

        var projectedParameters = parameters.Select(ProjectParameterContract).Concat(localParameters).ToArray();
        var projected = new List<PowerShellBoundStatement>();
        foreach (var binding in run)
        {
            var statement = PowerShellBoundRegionLocalProjection.Project(
                binding.Statement,
                projectedParameters,
                Array.Empty<PowerShellBoundLocal>(),
                functionLocals);
            if (statement is null) return false;
            var stopping = statement.Capabilities.HasFlag(PowerShellRequiredCapability.PowerShellStopping);
            if ((statement.Effects & ~(PowerShellSemanticEffect.Mutation | PowerShellLoopInterruptContract.Effects(stopping))) != 0 ||
                (statement.Capabilities & ~PowerShellLoopInterruptContract.Capabilities(stopping)) != PowerShellRequiredCapability.None)
                return false;
            var nested = PowerShellSemanticAnalyzer.EnumerateStatements(
                new PowerShellBoundBlock(statement.Span, new[] { statement })).ToArray();
            if (nested.Any(static item => item is PowerShellBoundReturnStatement or PowerShellBoundThrowStatement) ||
                nested.SelectMany(PowerShellSemanticAnalyzer.EnumerateDirectExpressions)
                    .SelectMany(PowerShellSemanticAnalyzer.EnumerateExpressions)
                    .Any(static expression => expression is PowerShellBoundInvocationExpression))
                return false;
            projected.Add(statement);
        }

        var writtenKeys = PowerShellBoundRegionOpportunitySelector.EnumerateWrittenSymbols(projected)
            .Where(static symbol => symbol.Kind == PowerShellSymbolKind.Local)
            .Select(static symbol => symbol.StableKey)
            .ToHashSet(StringComparer.Ordinal);
        var outputs = inputLocals.Where(local => localParameters.Any(parameter =>
                parameter.Symbol.Name.Equals(local.Name, StringComparison.OrdinalIgnoreCase) &&
                writtenKeys.Contains(parameter.Symbol.StableKey)))
            .ToArray();
        if (outputs.Length == 0) return false;

        var lastSpan = projected[projected.Count - 1].Span;
        var transferSpan = new SourceSpan(lastSpan.DocumentId, lastSpan.EndOffset, lastSpan.EndOffset,
            lastSpan.EndLine, lastSpan.EndColumn, lastSpan.EndLine, lastSpan.EndColumn);
        projected.Add(new PowerShellBoundRegionTransferStatement(
            transferSpan,
            outputs.Select(output =>
            {
                var parameter = localParameters.Single(item =>
                    item.Symbol.Name.Equals(output.Name, StringComparison.OrdinalIgnoreCase));
                return new PowerShellBoundVariableExpression(transferSpan, parameter.Symbol, parameter.Type);
            }).ToArray()));

        return TryCreateBound(
            document,
            syntax,
            sourceFunction,
            projectedParameters,
            functionLocals,
            projected.ToArray(),
            out candidate,
            outputs,
            inputLocals.ToArray());
    }

    private static bool TryFindEstablishedConstraint(
        IReadOnlyList<StatementAst> authoredStatements,
        int beforeStatementIndex,
        PowerShellBoundLocal local,
        out string constraintSyntax)
    {
        constraintSyntax = string.Empty;
        foreach (var assignment in authoredStatements.Take(beforeStatementIndex).OfType<AssignmentStatementAst>())
        {
            if (assignment.Left is not AttributedExpressionAst
                {
                    Attribute: TypeConstraintAst constraint,
                    Child: VariableExpressionAst variable
                } ||
                !variable.VariablePath.IsUnqualified ||
                !variable.VariablePath.UserPath.Equals(local.Symbol.Name, StringComparison.OrdinalIgnoreCase) ||
                constraint.TypeName.GetReflectionType() != local.Type.ClrType)
                continue;
            constraintSyntax = constraint.Extent.Text;
            return true;
        }
        return false;
    }
}
