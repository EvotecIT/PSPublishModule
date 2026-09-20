using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Recognizes a zero-argument local helper that creates one fresh ArrayList and returns that exact
/// reference as one explicitly non-enumerated success record. No helper name participates in the proof.
/// </summary>
internal static class PowerShellClosedLocalCollectionFactoryPolicy
{
    internal static bool TryCreate(
        FunctionDefinitionAst function,
        PowerShellSymbolId symbol,
        IReadOnlyList<PowerShellLocalCallParameter> parameters,
        out PowerShellCompiledRegionLocalCall? contract)
    {
        contract = null;
        if (parameters.Count != 0 || function.IsFilter || function.IsWorkflow ||
            function.Body.DynamicParamBlock is not null || function.Body.BeginBlock is not null ||
            function.Body.ProcessBlock is not null || function.Body.EndBlock is not { Statements.Count: 2 } endBlock ||
            function.Body.GetType().GetProperty("CleanBlock")?.GetValue(function.Body) is not null ||
            endBlock.Traps is { Count: > 0 } ||
            endBlock.Statements[0] is not AssignmentStatementAst assignment ||
            assignment.Operator.ToString() != "Equals" ||
            assignment.Left is not VariableExpressionAst { VariablePath.IsUnqualified: true } target ||
            PowerShellAssignmentTargetPolicy.IsAutomaticVariable(target.VariablePath.UserPath) ||
            Unwrap(assignment.Right) is not InvokeMemberExpressionAst
            {
                Static: true,
                Expression: TypeExpressionAst typeExpression,
                Member: StringConstantExpressionAst member,
                Arguments: null or { Count: 0 }
            } ||
            typeExpression.TypeName.GetReflectionType() != typeof(System.Collections.ArrayList) ||
            !member.Value.Equals("new", StringComparison.OrdinalIgnoreCase) ||
            endBlock.Statements[1] is not ReturnStatementAst { Pipeline: not null } returned ||
            Unwrap(returned.Pipeline) is not ArrayLiteralAst { Elements.Count: 1 } array ||
            array.Elements[0] is not VariableExpressionAst returnedVariable ||
            !returnedVariable.VariablePath.IsUnqualified ||
            !returnedVariable.VariablePath.UserPath.Equals(target.VariablePath.UserPath, StringComparison.OrdinalIgnoreCase))
            return false;

        contract = new PowerShellCompiledRegionLocalCall(
            symbol.Name,
            Array.Empty<string>(),
            typeof(System.Collections.ArrayList).FullName!,
            typeof(System.Collections.ArrayList).FullName!,
            new PowerShellRegionTransferContract(
                PowerShellRegionTransferShape.ListSequence,
                PowerShellRegionTransferElementContract.OpaqueReference,
                PowerShellRegionTransferDirection.LiveOut,
                PowerShellRegionTransferOwnership.CompiledCalleeFresh,
                PowerShellRegionTransferOutputBehavior.NoEnumerate,
                PowerShellRegionTransferMutation.RetainedOnly,
                PowerShellRegionMutationLifetime.None,
                supported: true));
        return true;
    }

    internal static void NormalizeBoundReturn(
        IList<PowerShellBoundStatement> statements,
        IList<PowerShellBoundStatementBinding> bindings)
    {
        if (statements.Count != 2 ||
            statements[1] is not PowerShellBoundReturnStatement
            {
                Expression: PowerShellBoundArrayExpression { Elements.Count: 1 } array
            } returned ||
            array.Elements[0] is not PowerShellBoundVariableExpression variable ||
            variable.Type.ClrType != typeof(System.Collections.ArrayList))
            throw new InvalidOperationException("A closed local collection factory did not bind to its proved one-record return shape.");
        var normalized = new PowerShellBoundReturnStatement(
            returned.Span,
            new PowerShellBoundClosedCollectionFactoryResultExpression(variable));
        statements[1] = normalized;
        var bindingIndex = -1;
        for (var index = 0; index < bindings.Count; index++)
            if (ReferenceEquals(bindings[index].Statement, returned))
            {
                bindingIndex = index;
                break;
            }
        if (bindingIndex < 0)
            throw new InvalidOperationException("A closed local collection factory return is missing its authored statement binding.");
        var binding = bindings[bindingIndex];
        bindings[bindingIndex] = new PowerShellBoundStatementBinding(
            binding.AuthoredStatementIndex,
            binding.AuthoredStatementEndIndex,
            normalized);
    }

    private static Ast Unwrap(Ast syntax)
    {
        while (syntax is PipelineAst { PipelineElements.Count: 1 } pipeline)
            syntax = pipeline.PipelineElements[0];
        while (syntax is CommandExpressionAst command)
            syntax = command.Expression;
        while (syntax is ParenExpressionAst parenthesized)
            syntax = parenthesized.Pipeline;
        return syntax;
    }
}
