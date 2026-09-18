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
        var conditionOnlySwitches = authoredStatements
            .OfType<IfStatementAst>()
            .Select(statement => PowerShellClosedValueAlternativePolicy.TryMatch(statement, out var initializer)
                ? initializer.ConditionVariableName
                : string.Empty)
            .Where(name => parameters.Any(parameter => parameter.Contract.IsSwitch &&
                parameter.Symbol.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var regionParameters = parameters
            .Select(parameter => ProjectParameterContract(
                parameter,
                conditionOnlySwitches.Contains(parameter.Symbol.Name)))
            .ToArray();
        var nextIndex = bindings[0].AuthoredStatementIndex;
        foreach (var binding in bindings)
        {
            var statement = PowerShellBoundRegionLocalProjection.Project(
                binding.Statement,
                regionParameters,
                locals,
                functionLocals,
                conditionOnlyBooleanParameters: conditionOnlySwitches);
            if (statement is null) break;
            var retainedOnlyAlternativeKeys = locals
                .Where(static local => PowerShellRegionTransferTypePolicy.IsClosedValueAlternative(local.Type))
                .Select(static local => local.Symbol.StableKey)
                .ToHashSet(StringComparer.Ordinal);
            if (retainedOnlyAlternativeKeys.Count > 0 &&
                PowerShellBoundRegionOpportunitySelector.EnumerateWrittenSymbols(new[] { statement })
                    .Any(symbol => retainedOnlyAlternativeKeys.Contains(symbol.StableKey)))
                break;
            // The helper ABI already carries native stopping. Its checkpoint is the only
            // host effect allowed here; the ordinary promotion policy still checks errors.
            var stopping = statement.Capabilities.HasFlag(PowerShellRequiredCapability.PowerShellStopping);
            if (binding.AuthoredStatementIndex != nextIndex ||
                binding.AuthoredStatementEndIndex >= authoredStatements.Count - 1 ||
                (statement.Effects & ~(PowerShellSemanticEffect.Mutation | PowerShellLoopInterruptContract.Effects(stopping))) != 0 ||
                (statement.Capabilities & ~PowerShellLoopInterruptContract.Capabilities(stopping)) != PowerShellRequiredCapability.None)
                break;
            var candidateLocals = locals.ToList();
            var newTransfers = new List<PowerShellCompiledRegionLocal>();
            if (statement is PowerShellBoundAssignmentStatement assignment &&
                !locals.Any(local => local.Symbol.StableKey == assignment.Target.StableKey))
            {
                if (functionLocals.Any(local => local.Symbol.StableKey == assignment.Target.StableKey &&
                        local.Type.Provenance == PowerShellTypeFactProvenance.Int32OrDouble))
                    break;
                if (!TryCreateContinuationLocal(assignment, authoredStatements[binding.AuthoredStatementIndex], out var newTransfer))
                    break;
                candidateLocals.Add(new PowerShellBoundLocal(assignment.Target, assignment.Value.Type));
                newTransfers.Add(newTransfer!);
            }
            else if (statement is PowerShellBoundIfStatement conditional &&
                     TryCreateClosedAlternativeLocal(
                         conditional,
                         authoredStatements[binding.AuthoredStatementIndex],
                         functionLocals,
                         out var alternativeLocal,
                         out var alternativeTransfer))
            {
                candidateLocals.Add(alternativeLocal!);
                newTransfers.Add(alternativeTransfer!);
            }
            else if (statement is PowerShellBoundOutputCaptureStatement
                     {
                         CapturesStableScalarVector: true,
                         Target: { } captureTarget,
                         CapturedVectorType: { } captureType
                     } capture &&
                     !locals.Any(local => local.Symbol.StableKey == captureTarget.StableKey))
            {
                if (!TryCreateStableVectorCaptureLocal(capture, authoredStatements[binding.AuthoredStatementIndex], out var captureTransfer))
                    break;
                candidateLocals.Add(new PowerShellBoundLocal(captureTarget,
                    new PowerShellTypeFact(captureType, PowerShellTypeFactProvenance.Explicit,
                        "The authored typed array target owns this closed stable-scalar capture.")));
                newTransfers.Add(captureTransfer!);

                var captureLoops = PowerShellSemanticAnalyzer.EnumerateStatements(capture.Body)
                    .OfType<PowerShellBoundForEachStatement>()
                    .Where(loop => !candidateLocals.Any(local => local.Symbol.StableKey == loop.Variable.StableKey))
                    .GroupBy(static loop => loop.Variable.StableKey, StringComparer.Ordinal)
                    .Select(static group => group.First())
                    .ToArray();
                foreach (var loop in captureLoops)
                {
                    if (!TryCreateStableScalarLoopLocal(loop, out var loopLocal, out var loopTransfer))
                    {
                        newTransfers.Clear();
                        break;
                    }
                    candidateLocals.Add(loopLocal!);
                    newTransfers.Add(loopTransfer!);
                }
                if (newTransfers.Count == 0)
                    break;
            }
            var nested = PowerShellSemanticAnalyzer.EnumerateStatements(
                new PowerShellBoundBlock(statement.Span, new[] { statement })).ToArray();
            var hasClosedAlternativeTransfer = newTransfers.Any(static transfer =>
                transfer.Contract?.Shape == PowerShellRegionTransferShape.ClosedValueAlternative);
            if (nested.OfType<PowerShellBoundAssignmentStatement>().Any(update =>
                    !candidateLocals.Any(local => local.Symbol.StableKey == update.Target.StableKey &&
                                                  local.Type.ClrType == update.Value.Type.ClrType)) ||
                authoredStatements[binding.AuthoredStatementIndex].FindAll(
                    static node => node is AssignmentStatementAst { Left: AttributedExpressionAst },
                    searchNestedScriptBlocks: false).Any(node =>
                    !hasClosedAlternativeTransfer &&
                    (newTransfers.Count == 0 || !ReferenceEquals(node, authoredStatements[binding.AuthoredStatementIndex]))))
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
            transfers.AddRange(newTransfers);
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
        if (statementIndex < 0 || statementIndex >= authoredStatements.Count)
            return false;
        string name;
        if (authoredStatements[statementIndex] is AssignmentStatementAst assignment)
        {
            Ast target = assignment.Left;
            if (target is AttributedExpressionAst attributed) target = attributed.Child;
            if (target is not VariableExpressionAst variable || !variable.VariablePath.IsUnqualified)
                return false;
            name = variable.VariablePath.UserPath;
        }
        else if (authoredStatements[statementIndex] is IfStatementAst conditional &&
                 PowerShellClosedValueAlternativePolicy.TryMatch(conditional, out var initializer))
        {
            name = initializer.Name;
        }
        else
        {
            return false;
        }
        return !authoredStatements.Take(statementIndex).Any(statement => statement.FindAll(node =>
                node is VariableExpressionAst prior && prior.VariablePath.IsUnqualified &&
                prior.VariablePath.UserPath.Equals(name, StringComparison.OrdinalIgnoreCase),
                searchNestedScriptBlocks: false).Any());
    }

    private static PowerShellBoundParameter ProjectParameterContract(PowerShellBoundParameter parameter)
        => ProjectParameterContract(parameter, projectSwitchTruthiness: false);

    private static PowerShellBoundParameter ProjectParameterContract(
        PowerShellBoundParameter parameter,
        bool projectSwitchTruthiness)
    {
        if (parameter.Contract.IsSwitch && projectSwitchTruthiness)
            return new PowerShellBoundParameter(
                parameter.Symbol,
                new PowerShellTypeFact(
                    typeof(bool),
                    PowerShellTypeFactProvenance.Explicit,
                    "The retained PowerShell parameter binder resolves SwitchParameter.IsPresent before the detached region call."),
                new PowerShellCompilationParameter(parameter.Symbol.Name, typeof(bool).FullName!, hasDefaultValue: false));
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

    private static bool TryCreateClosedAlternativeLocal(
        PowerShellBoundIfStatement conditional,
        StatementAst authoredStatement,
        IReadOnlyList<PowerShellBoundLocal> functionLocals,
        out PowerShellBoundLocal? local,
        out PowerShellCompiledRegionLocal? transfer)
    {
        local = null;
        transfer = null;
        if (authoredStatement is not IfStatementAst authoredConditional ||
            !PowerShellClosedValueAlternativePolicy.TryMatch(authoredConditional, out var initializer))
            return false;
        var assignments = PowerShellSemanticAnalyzer.EnumerateStatements(
                new PowerShellBoundBlock(conditional.Span, new PowerShellBoundStatement[] { conditional }))
            .OfType<PowerShellBoundAssignmentStatement>()
            .ToArray();
        if (assignments.Length != initializer.Alternatives.Length ||
            assignments.Select(static assignment => assignment.Target.StableKey).Distinct(StringComparer.Ordinal).Count() != 1 ||
            assignments.Any(static assignment =>
                assignment.Value is not PowerShellBoundRegionValueAlternativeExpression))
            return false;
        var target = assignments[0].Target;
        var functionLocal = functionLocals.SingleOrDefault(candidate =>
            candidate.Symbol.StableKey == target.StableKey);
        if (functionLocal is null || !PowerShellRegionTransferTypePolicy.IsClosedValueAlternative(functionLocal.Type) ||
            !functionLocal.Type.ClosedAlternativeTypes.SequenceEqual(
                initializer.Alternatives.Select(static alternative => alternative.Type)))
            return false;

        var alternatives = initializer.Alternatives.Select(alternative =>
            new PowerShellCompiledRegionLocalAlternative(
                alternative.Type.FullName ?? alternative.Type.Name,
                alternative.ConstraintSyntax,
                PowerShellRegionTransferTypePolicy.Describe(
                    alternative.Type,
                    PowerShellRegionTransferDirection.LiveOut,
                    PowerShellRegionTransferOwnership.GuardedFresh,
                    PowerShellRegionTransferMutation.RetainedOnly))).ToArray();
        var contract = PowerShellRegionTransferTypePolicy.DescribeClosedValueAlternative(
            PowerShellRegionTransferDirection.LiveOut,
            PowerShellRegionTransferOwnership.GuardedFresh,
            PowerShellRegionTransferMutation.RetainedOnly);
        local = new PowerShellBoundLocal(target, functionLocal.Type);
        transfer = new PowerShellCompiledRegionLocal(
            target.Name,
            functionLocal.Type.ClrType.FullName ?? functionLocal.Type.ClrType.Name,
            hasTypeConstraint: false,
            typeConstraintSyntax: string.Empty,
            contract: contract,
            alternatives: alternatives);
        return true;
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

    private static bool TryCreateStableVectorCaptureLocal(
        PowerShellBoundOutputCaptureStatement capture,
        StatementAst authoredStatement,
        out PowerShellCompiledRegionLocal? transfer)
    {
        transfer = null;
        if (capture.Target is not { Kind: PowerShellSymbolKind.Local } target ||
            capture.CapturedVectorType is not { } vectorType ||
            !IsSimpleVariableName(target.Name) ||
            !PowerShellRegionTransferTypePolicy.IsSupported(vectorType) ||
            authoredStatement is not AssignmentStatementAst
            {
                Left: AttributedExpressionAst
                {
                    Attribute: TypeConstraintAst constraint,
                    Child: VariableExpressionAst variable
                }
            } ||
            !variable.VariablePath.IsUnqualified ||
            constraint.TypeName.GetReflectionType() != vectorType)
            return false;
        transfer = new PowerShellCompiledRegionLocal(target.Name,
            vectorType.FullName ?? vectorType.Name, true, constraint.Extent.Text,
            PowerShellRegionTransferTypePolicy.Describe(
                vectorType,
                PowerShellRegionTransferDirection.LiveOut,
                PowerShellRegionTransferOwnership.GuardedFresh,
                PowerShellRegionTransferMutation.RetainedOnly));
        return true;
    }

    private static bool TryCreateStableScalarLoopLocal(
        PowerShellBoundForEachStatement loop,
        out PowerShellBoundLocal? local,
        out PowerShellCompiledRegionLocal? transfer)
    {
        local = null;
        transfer = null;
        if (loop.EnumerationKind != PowerShellForEachEnumerationKind.StableScalar ||
            !loop.ElementType.IsValueType ||
            Nullable.GetUnderlyingType(loop.ElementType) is not null ||
            loop.Variable.Kind != PowerShellSymbolKind.Local ||
            !IsSimpleVariableName(loop.Variable.Name) ||
            !PowerShellRegionTransferTypePolicy.IsSupported(loop.ElementType))
            return false;
        local = new PowerShellBoundLocal(
            loop.Variable,
            new PowerShellTypeFact(loop.ElementType, PowerShellTypeFactProvenance.Explicit,
                "The closed stable-scalar foreach assigns this exact element type before transfer."));
        transfer = new PowerShellCompiledRegionLocal(loop.Variable.Name,
            loop.ElementType.FullName ?? loop.ElementType.Name, false, string.Empty,
            PowerShellRegionTransferTypePolicy.Describe(
                loop.ElementType,
                PowerShellRegionTransferDirection.LiveOut,
                PowerShellRegionTransferOwnership.GuardedFresh,
                PowerShellRegionTransferMutation.RetainedOnly));
        return true;
    }
}
