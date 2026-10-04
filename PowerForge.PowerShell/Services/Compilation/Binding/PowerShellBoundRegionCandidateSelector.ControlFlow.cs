using System.Management.Automation.Language;

namespace PowerForge;

internal static partial class PowerShellBoundRegionCandidateSelector
{
    /// <summary>
    /// Selects a statement-aligned conditional return whose alternate path falls through to retained
    /// PowerShell. The helper returns an envelope; the retained return statement owns enumeration,
    /// partial output, error continuation, stopping, and enumerator cleanup.
    /// </summary>
    internal static bool TryCreateControlFlowEnvelope(
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

        var projectedParameters = parameters.Select(ProjectParameterContract).ToArray();
        var parameterKeys = projectedParameters.Select(static parameter => parameter.Symbol.StableKey)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var binding in bindings.OrderBy(static item => item.AuthoredStatementIndex))
        {
            if (binding.AuthoredStatementIndex < 0 ||
                binding.AuthoredStatementEndIndex != binding.AuthoredStatementIndex ||
                binding.AuthoredStatementEndIndex >= authoredStatements.Count - 1 ||
                authoredStatements[binding.AuthoredStatementIndex] is not IfStatementAst authoredIf)
                continue;

            var projected = PowerShellBoundRegionLocalProjection.Project(
                binding.Statement,
                projectedParameters,
                Array.Empty<PowerShellBoundLocal>(),
                functionLocals,
                includeControlFlow: true) as PowerShellBoundIfStatement;
            if (projected is null || projected.ElseBlock is not null ||
                projected.Clauses.Length != authoredIf.Clauses.Count)
                continue;

            var clauses = new List<PowerShellBoundConditionalClause>();
            PowerShellRegionTransferContract? returnContract = null;
            var valid = true;
            for (var index = 0; index < projected.Clauses.Length; index++)
            {
                var clause = projected.Clauses[index];
                var authoredBody = authoredIf.Clauses[index].Item2;
                if (clause.Condition.Effects != PowerShellSemanticEffect.None ||
                    clause.Condition.Capabilities != PowerShellRequiredCapability.None ||
                    clause.Body.Statements is not { Count: 1 } ||
                    clause.Body.Statements[0] is not PowerShellBoundReturnStatement returned ||
                    authoredBody.Statements is not { Count: 1 } ||
                    authoredBody.Statements[0] is not ReturnStatementAst authoredReturn ||
                    !TryCreateControlFlowReturn(returned, authoredReturn, parameterKeys, out var flowReturn, out var contract) ||
                    returnContract is not null && !SameReturnContract(returnContract, contract))
                {
                    valid = false;
                    break;
                }
                returnContract ??= contract;
                clauses.Add(new PowerShellBoundConditionalClause(
                    clause.Condition,
                    new PowerShellBoundBlock(clause.Body.Span, new PowerShellBoundStatement[] { flowReturn })));
            }
            if (!valid || returnContract is null) continue;

            var selected = new PowerShellBoundIfStatement(projected.Span, clauses.ToArray(), elseBlock: null);
            if (CollectUsedSymbolKeys(new PowerShellBoundStatement[] { selected })
                .Any(key => !parameterKeys.Contains(key)))
                continue;

            var tailSpan = new SourceSpan(
                projected.Span.DocumentId,
                projected.Span.EndOffset,
                projected.Span.EndOffset,
                projected.Span.EndLine,
                projected.Span.EndColumn,
                projected.Span.EndLine,
                projected.Span.EndColumn);
            var controlFlow = new PowerShellRegionControlFlowContract(
                PowerShellRegionControlFlowBehavior.ReturnOrFallThrough,
                returnContract);
            if (TryCreateBound(
                    document,
                    syntax,
                    sourceFunction,
                    projectedParameters,
                    functionLocals,
                    new PowerShellBoundStatement[]
                    {
                        selected,
                        new PowerShellBoundRegionControlFlowReturnStatement(
                            tailSpan,
                            PowerShellRegionControlFlowKind.FallThrough)
                    },
                    out candidate,
                    terminalTransferContract: returnContract,
                    controlFlowContract: controlFlow))
                return true;
        }
        return false;
    }

    private static bool TryCreateControlFlowReturn(
        PowerShellBoundReturnStatement returned,
        ReturnStatementAst authored,
        HashSet<string> parameterKeys,
        out PowerShellBoundRegionControlFlowReturnStatement flowReturn,
        out PowerShellRegionTransferContract contract)
    {
        flowReturn = null!;
        contract = null!;
        var value = returned.Expression;
        if (value is null)
        {
            contract = PowerShellRegionTransferTypePolicy.DescribeNoValue();
            flowReturn = new PowerShellBoundRegionControlFlowReturnStatement(
                returned.Span,
                PowerShellRegionControlFlowKind.Return);
            return true;
        }
        if (value.ValueState == PowerShellValueState.Null &&
            value.Effects == PowerShellSemanticEffect.None &&
            value.Capabilities == PowerShellRequiredCapability.None)
        {
            contract = PowerShellRegionTransferTypePolicy.DescribeNullValue();
            flowReturn = new PowerShellBoundRegionControlFlowReturnStatement(
                returned.Span,
                PowerShellRegionControlFlowKind.Return,
                value);
            return true;
        }
        if (value is PowerShellBoundVariableExpression variable &&
            parameterKeys.Contains(variable.Symbol.StableKey) &&
            PowerShellRegionTransferTypePolicy.IsSupported(value.Type.ClrType))
        {
            contract = PowerShellRegionTransferTypePolicy.Describe(
                value.Type.ClrType,
                PowerShellRegionTransferDirection.TerminalSuccess,
                PowerShellRegionTransferOwnership.ParameterBorrowed,
                PowerShellRegionTransferMutation.None);
            flowReturn = new PowerShellBoundRegionControlFlowReturnStatement(
                returned.Span,
                PowerShellRegionControlFlowKind.Return,
                value);
            return true;
        }
        if (!authored.Extent.Text.TrimStart().StartsWith("return ,", StringComparison.OrdinalIgnoreCase) ||
            value is not PowerShellBoundArrayExpression
            {
                Type.ClrType: var wrapperType,
                Elements.Count: 1
            } wrapper ||
            wrapperType != typeof(object[]) ||
            wrapper.Elements[0] is not PowerShellBoundVariableExpression innerVariable ||
            !parameterKeys.Contains(innerVariable.Symbol.StableKey) ||
            !PowerShellRegionTransferTypePolicy.IsSupported(wrapper.Elements[0].Type.ClrType))
            return false;
        var inner = PowerShellRegionTransferTypePolicy.Describe(wrapper.Elements[0].Type.ClrType);
        contract = new PowerShellRegionTransferContract(
            inner.Shape,
            inner.ElementContract,
            PowerShellRegionTransferDirection.TerminalSuccess,
            PowerShellRegionTransferOwnership.ParameterBorrowed,
            PowerShellRegionTransferOutputBehavior.NoEnumerate,
            PowerShellRegionTransferMutation.None,
            PowerShellRegionMutationLifetime.None,
            supported: true);
        flowReturn = new PowerShellBoundRegionControlFlowReturnStatement(
            returned.Span,
            PowerShellRegionControlFlowKind.Return,
            wrapper.Elements[0]);
        return true;
    }

    private static bool SameReturnContract(
        PowerShellRegionTransferContract left,
        PowerShellRegionTransferContract right)
        => left.Shape == right.Shape &&
           left.ElementContract == right.ElementContract &&
           left.OutputBehavior == right.OutputBehavior &&
           left.Mutation == right.Mutation &&
           left.MutationLifetime == right.MutationLifetime &&
           left.Supported == right.Supported;
}
