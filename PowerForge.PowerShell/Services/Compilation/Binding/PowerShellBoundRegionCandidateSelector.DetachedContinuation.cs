using System.Management.Automation.Language;

namespace PowerForge;

internal static partial class PowerShellBoundRegionCandidateSelector
{
    /// <summary>
    /// Selects the first statement-aligned typed run after a guarded prefix. Every local input must
    /// have an authored stable constraint or exact guarded-prefix provenance. Mutated outputs remain
    /// stable scalars and return to a later retained continuation; a read-only run may be terminal.
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

        foreach (var run in runs.Where(run => run.Count >= 1 &&
                     run[0].AuthoredStatementIndex > 0))
        {
            if (TryCreateDetachedRun(document, syntax, sourceFunction, parameters, functionLocals,
                    authoredStatements, run, guardedPrefix, out candidate))
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
        PowerShellBoundRegionCandidate guardedPrefix,
        out PowerShellBoundRegionCandidate candidate)
    {
        candidate = null!;
        var rawStatements = run.Select(static binding => binding.Statement).ToArray();
        var nativeLocalNames = PowerShellSemanticAnalyzer.EnumerateStatements(
                new PowerShellBoundBlock(rawStatements[0].Span, rawStatements))
            .SelectMany(PowerShellSemanticAnalyzer.EnumerateDirectExpressions)
            .SelectMany(PowerShellSemanticAnalyzer.EnumerateExpressions)
            .OfType<PowerShellBoundNativeVariableExpression>()
            .Where(static variable => variable.DirectLocal && variable.Name.IndexOf(':') < 0)
            .Select(static variable => variable.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var localSymbols = PowerShellBoundRegionOpportunitySelector.EnumerateReadSymbols(rawStatements)
            .Concat(PowerShellBoundRegionOpportunitySelector.EnumerateWrittenSymbols(rawStatements))
            .Concat(functionLocals.Where(local => nativeLocalNames.Contains(local.Symbol.Name))
                .Select(static local => local.Symbol))
            .Where(static symbol => symbol.Kind == PowerShellSymbolKind.Local)
            .GroupBy(static symbol => symbol.StableKey, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToArray();
        if (localSymbols.Length == 0) return false;

        var localParameters = new List<PowerShellBoundParameter>();
        var inputLocals = new List<PowerShellCompiledRegionLocal>();
        var hasPrefixOwnedInput = false;
        foreach (var local in functionLocals.Where(local => localSymbols.Any(symbol =>
                     symbol.StableKey == local.Symbol.StableKey)))
        {
            if (local.Type.Provenance is PowerShellTypeFactProvenance.Unknown or PowerShellTypeFactProvenance.Int32OrDouble ||
                !PowerShellRegionTransferTypePolicy.IsSupported(local.Type.ClrType))
                return false;
            var hasConstraint = TryFindEstablishedConstraint(
                authoredStatements,
                run[0].AuthoredStatementIndex,
                local,
                out var constraintSyntax);
            var prefixOwned = !hasConstraint && guardedPrefix.ContinuationLocals.Any(output =>
                output.Name.Equals(local.Symbol.Name, StringComparison.OrdinalIgnoreCase) &&
                output.TypeName.Equals(local.Type.ClrType.FullName ?? local.Type.ClrType.Name, StringComparison.Ordinal));
            if (!hasConstraint && !prefixOwned) return false;
            hasPrefixOwnedInput |= prefixOwned;
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
                hasTypeConstraint: hasConstraint,
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
            var terminalOutput = binding.AuthoredStatementEndIndex == authoredStatements.Count - 1 &&
                ReferenceEquals(binding, run[run.Count - 1]) &&
                statement is PowerShellBoundExpressionStatement
                {
                    EmitsOutput: true,
                    RequiresOutputContinuation: false,
                    Expression.Type.ClrType: var outputType
                } &&
                PowerShellRegionTransferTypePolicy.IsSupported(outputType);
            var allowedEffects = PowerShellSemanticEffect.Mutation |
                                 PowerShellLoopInterruptContract.Effects(stopping) |
                                 (terminalOutput ? PowerShellSemanticEffect.SuccessOutput : PowerShellSemanticEffect.None);
            if ((statement.Effects & ~allowedEffects) != 0 ||
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
        if (outputs.Any(output => !PowerShellStableScalarTypePolicy.IsSupported(
                localParameters.Single(parameter => parameter.Symbol.Name.Equals(
                    output.Name, StringComparison.OrdinalIgnoreCase)).Type.ClrType)))
            return false;

        if (outputs.Length > 0)
        {
            if (run[run.Count - 1].AuthoredStatementEndIndex >= authoredStatements.Count - 1)
                return false;
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
        }
        else
        {
            if (run[run.Count - 1].AuthoredStatementEndIndex != authoredStatements.Count - 1)
                return false;
            if (projected[projected.Count - 1] is PowerShellBoundExpressionStatement
                { EmitsOutput: true, RequiresOutputContinuation: false } terminal &&
                PowerShellRegionTransferTypePolicy.IsSupported(terminal.Expression.Type.ClrType))
                projected[projected.Count - 1] = new PowerShellBoundReturnStatement(terminal.Span, terminal.Expression);
            if (!AlwaysReturns(projected[projected.Count - 1])) return false;
        }

        return TryCreateBound(
            document,
            syntax,
            sourceFunction,
            projectedParameters,
            functionLocals,
            projected.ToArray(),
            out candidate,
            outputs.Length == 0 ? null : outputs,
            inputLocals.ToArray(),
            hasPrefixOwnedInput);
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
