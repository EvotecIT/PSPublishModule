using System.Management.Automation.Language;

namespace PowerForge;

internal static partial class PowerShellBoundRegionCandidateSelector
{
    /// <summary>
    /// Selects a prefix that initializes and computes supported transfer locals. Every introduced local is
    /// transferred back in declaration order, including locals observed only by unbound later code.
    /// </summary>
    internal static bool TryCreateContinuation(
        ParsedSourceDocument document,
        FunctionDefinitionAst syntax,
        PowerShellSymbolId sourceFunction,
        IReadOnlyList<PowerShellBoundParameter> parameters,
        IReadOnlyList<PowerShellBoundLocal> functionLocals,
        IReadOnlyList<StatementAst> authoredStatements,
        IReadOnlyList<PowerShellBoundStatementBinding> bindings,
        out PowerShellBoundRegionCandidate candidate)
    {
        candidate = null!;
        // A detached initializer has no trap continuation or hosted type-identity contract.
        // Keep those function owners native until their boundary is separately qualified.
        if (!PowerShellSourceSemanticValidator.SupportsDetachedFunctionMetadata(document) ||
            syntax.IsFilter || syntax.IsWorkflow || HasNamedLifecycle(syntax.Body) || syntax.Body.EndBlock?.Traps is { Count: > 0 } ||
            bindings.Count == 0 || bindings[0].AuthoredStatementIndex != 0)
            return false;
        return TryCreateContinuationRun(
            document, syntax, sourceFunction, parameters, functionLocals,
            authoredStatements, bindings, out candidate);
    }

    /// <summary>
    /// Selects a later statement-aligned initialization run when its first local has no earlier
    /// authored reference. Runtime storage is still guarded at invocation entry, so inherited
    /// AllScope, constrained, or otherwise decorated storage retains the authored PowerShell path.
    /// </summary>
    internal static bool TryCreateLaterContinuation(
        ParsedSourceDocument document,
        FunctionDefinitionAst syntax,
        PowerShellSymbolId sourceFunction,
        IReadOnlyList<PowerShellBoundParameter> parameters,
        IReadOnlyList<PowerShellBoundLocal> functionLocals,
        IReadOnlyList<StatementAst> authoredStatements,
        IReadOnlyList<PowerShellBoundStatementBinding> bindings,
        out PowerShellBoundRegionCandidate candidate)
    {
        candidate = null!;
        if (!PowerShellSourceSemanticValidator.SupportsDetachedFunctionMetadata(document) ||
            syntax.IsFilter || syntax.IsWorkflow || HasNamedLifecycle(syntax.Body) ||
            syntax.Body.EndBlock?.Traps is { Count: > 0 })
            return false;
        for (var index = 1; index < bindings.Count; index++)
        {
            var binding = bindings[index];
            if (binding.AuthoredStatementIndex <= 0 ||
                !IsFreshLocalInitialization(authoredStatements, binding.AuthoredStatementIndex))
                continue;
            if (TryCreateContinuationRun(
                    document, syntax, sourceFunction, parameters, functionLocals,
                    authoredStatements, bindings.Skip(index).ToArray(), out candidate))
                return true;
        }
        return false;
    }

    private static bool TryCreateContinuationRun(
        ParsedSourceDocument document,
        FunctionDefinitionAst syntax,
        PowerShellSymbolId sourceFunction,
        IReadOnlyList<PowerShellBoundParameter> parameters,
        IReadOnlyList<PowerShellBoundLocal> functionLocals,
        IReadOnlyList<StatementAst> authoredStatements,
        IReadOnlyList<PowerShellBoundStatementBinding> bindings,
        out PowerShellBoundRegionCandidate candidate)
    {
        candidate = null!;
        if (bindings.Count == 0) return false;
        var selected = new List<PowerShellBoundStatement>();
        var locals = new List<PowerShellBoundLocal>();
        var transfers = new List<PowerShellCompiledRegionLocal>();
        var regionParameters = parameters.Select(ProjectParameterContract).ToArray();
        var nextIndex = bindings[0].AuthoredStatementIndex;
        foreach (var binding in bindings)
        {
            var statement = PowerShellBoundRegionLocalProjection.Project(binding.Statement, regionParameters, locals, functionLocals);
            if (statement is null) break;
            // The helper ABI already carries native stopping. Its checkpoint is the only
            // host effect allowed here; the ordinary promotion policy still checks errors.
            var stopping = statement.Capabilities.HasFlag(PowerShellRequiredCapability.PowerShellStopping);
            if (binding.AuthoredStatementIndex != nextIndex ||
                binding.AuthoredStatementEndIndex >= authoredStatements.Count - 1 ||
                (statement.Effects & ~(PowerShellSemanticEffect.Mutation | PowerShellLoopInterruptContract.Effects(stopping))) != 0 ||
                (statement.Capabilities & ~PowerShellLoopInterruptContract.Capabilities(stopping)) != PowerShellRequiredCapability.None)
                break;
            var candidateLocals = locals.ToList();
            PowerShellCompiledRegionLocal? newTransfer = null;
            if (statement is PowerShellBoundAssignmentStatement assignment &&
                !locals.Any(local => local.Symbol.StableKey == assignment.Target.StableKey))
            {
                if (functionLocals.Any(local => local.Symbol.StableKey == assignment.Target.StableKey &&
                        local.Type.Provenance == PowerShellTypeFactProvenance.Int32OrDouble))
                    break;
                if (!TryCreateContinuationLocal(assignment, authoredStatements[binding.AuthoredStatementIndex], out newTransfer))
                    break;
                candidateLocals.Add(new PowerShellBoundLocal(assignment.Target, assignment.Value.Type));
            }
            var nested = PowerShellSemanticAnalyzer.EnumerateStatements(
                new PowerShellBoundBlock(statement.Span, new[] { statement })).ToArray();
            if (nested.OfType<PowerShellBoundAssignmentStatement>().Any(update =>
                    !candidateLocals.Any(local => local.Symbol.StableKey == update.Target.StableKey &&
                                                  local.Type.ClrType == update.Value.Type.ClrType)) ||
                authoredStatements[binding.AuthoredStatementIndex].FindAll(
                    static node => node is AssignmentStatementAst { Left: AttributedExpressionAst },
                    searchNestedScriptBlocks: false).Any(node =>
                    newTransfer is null || !ReferenceEquals(node, authoredStatements[binding.AuthoredStatementIndex])))
                break;
            if (nested.Any(static statement => statement is PowerShellBoundReturnStatement or PowerShellBoundThrowStatement) ||
                PowerShellBoundRegionOpportunitySelector.EnumerateWrittenSymbols(new[] { statement })
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
            selected.Add(statement);
            locals = candidateLocals;
            if (newTransfer is not null) transfers.Add(newTransfer);
            nextIndex = binding.AuthoredStatementEndIndex + 1;
        }
        if (selected.Count == 0 || locals.Count == 0) return false;
        var lastSpan = selected[selected.Count - 1].Span;
        // The synthetic transfer occurs after the last authored assignment. Giving it that
        // assignment's start offset would falsely classify the newly initialized local as live-in.
        var transferSpan = new SourceSpan(lastSpan.DocumentId, lastSpan.EndOffset, lastSpan.EndOffset,
            lastSpan.EndLine, lastSpan.EndColumn, lastSpan.EndLine, lastSpan.EndColumn);
        var values = locals.Select(local => new PowerShellBoundVariableExpression(
            transferSpan, local.Symbol, local.Type)).ToArray();
        selected.Add(new PowerShellBoundRegionTransferStatement(transferSpan, values));
        return TryCreateBound(document, syntax, sourceFunction, regionParameters, locals, selected.ToArray(),
            out candidate, transfers.ToArray());
    }

    private static bool IsFreshLocalInitialization(
        IReadOnlyList<StatementAst> authoredStatements,
        int statementIndex)
    {
        if (statementIndex < 0 || statementIndex >= authoredStatements.Count ||
            authoredStatements[statementIndex] is not AssignmentStatementAst assignment)
            return false;
        Ast target = assignment.Left;
        if (target is AttributedExpressionAst attributed) target = attributed.Child;
        if (target is not VariableExpressionAst variable || !variable.VariablePath.IsUnqualified)
            return false;
        var name = variable.VariablePath.UserPath;
        return !authoredStatements.Take(statementIndex).Any(statement => statement.FindAll(node =>
                node is VariableExpressionAst prior && prior.VariablePath.IsUnqualified &&
                prior.VariablePath.UserPath.Equals(name, StringComparison.OrdinalIgnoreCase),
                searchNestedScriptBlocks: false).Any());
    }

    private static PowerShellBoundParameter ProjectParameterContract(PowerShellBoundParameter parameter)
    {
        if (parameter.Type.Provenance != PowerShellTypeFactProvenance.Unknown ||
            !parameter.Contract.TypeCapabilities.HasFlag(PowerShellCompilationParameterTypeCapability.ClrMethod) ||
            Type.GetType(parameter.Contract.TypeName, throwOnError: false) is not { } contractType ||
            !PowerShellStableScalarTypePolicy.IsSupported(contractType))
            return parameter;
        return new PowerShellBoundParameter(
            parameter.Symbol,
            new PowerShellTypeFact(
                contractType,
                PowerShellTypeFactProvenance.Explicit,
                "The detached region receives this value after the retained PowerShell function applies its authored parameter contract."),
            parameter.Contract);
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
            !PowerShellRegionTransferTypePolicy.IsSupported(assignment.Value.Type.ClrType) ||
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
            assignment.Value.Type.ClrType.FullName ?? assignment.Value.Type.ClrType.Name, constrained, constraintSyntax,
            PowerShellRegionTransferTypePolicy.Describe(
                assignment.Value.Type.ClrType,
                PowerShellRegionTransferDirection.LiveOut,
                PowerShellRegionTransferOwnership.GuardedFresh,
                PowerShellRegionTransferMutation.RetainedOnly));
        return true;
    }
}
