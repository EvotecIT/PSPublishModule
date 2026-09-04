using System.Collections;
using System.Management.Automation;
using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Owns the conservative provenance boundary for values read from a live Hybrid module scope.
/// </summary>
internal static class PowerShellModuleStateOriginPolicy
{
    internal static bool IsDerived(PowerShellBoundExpression expression)
        => PowerShellSemanticAnalyzer.EnumerateExpressions(expression).Any(IsOrigin);

    internal static bool IsExactAuthoredTypedRead(
        ExpressionAst syntax,
        PowerShellBoundExpression expression)
        => UnwrapReceiverGrouping(syntax) is ConvertExpressionAst { Child: VariableExpressionAst } &&
           expression is PowerShellBoundConversionExpression
        {
            Type.Provenance: PowerShellTypeFactProvenance.Explicit,
            Operand: PowerShellBoundRuntimeStateExpression
            {
                Kind: PowerShellRuntimeStateIntrinsicKind.ModuleVariable
            }
        } conversion && IsConcreteTypedReceiver(conversion.Type.ClrType);

    internal static bool ReferencesDerivedModuleState<TAst>(
        IEnumerable<TAst> syntax,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        PowerShellCompilationCapability capabilities)
        where TAst : Ast
    {
        if (!capabilities.HasFlag(PowerShellCompilationCapability.PowerShellModuleState)) return false;
        return syntax.SelectMany(static item => item.FindAll(static node => node is VariableExpressionAst, searchNestedScriptBlocks: true))
            .Cast<VariableExpressionAst>()
            .Any(variable => IsModuleVariable(variable) || IsDerivedSymbolRead(variable, symbols));
    }

    internal static bool UsesDynamicMemberSemantics(Type type)
        => type == typeof(PSObject) ||
           type == typeof(PSCustomObject) ||
           typeof(IDictionary).IsAssignableFrom(type);

    internal static bool ReturnsDerivedModuleState(PowerShellBoundFunction function)
        => PowerShellSemanticAnalyzer.EnumerateStatements(function.Body)
            .Select(PowerShellSemanticAnalyzer.GetSuccessOutputExpression)
            .Where(static expression => expression is not null)
            .Any(expression => IsDerived(expression!));

    private static bool IsOrigin(PowerShellBoundExpression expression)
        => expression is PowerShellBoundRuntimeStateExpression
            {
                Kind: PowerShellRuntimeStateIntrinsicKind.ModuleVariable
            } ||
           expression is PowerShellBoundVariableExpression { IsModuleStateDerived: true } ||
           expression is PowerShellBoundInvocationExpression { ReturnsModuleStateDerived: true };

    private static bool IsConcreteTypedReceiver(Type type)
        => type != typeof(object) &&
           type != typeof(PSObject) &&
           type != typeof(PSCustomObject);

    private static ExpressionAst UnwrapReceiverGrouping(ExpressionAst expression)
    {
        while (expression is ParenExpressionAst
               {
                   Pipeline: PipelineAst { PipelineElements.Count: 1 } pipeline
               } &&
               pipeline.PipelineElements[0] is CommandExpressionAst
               {
                   Redirections.Count: 0,
                   Expression: var nested
               })
            expression = nested;
        return expression;
    }

    private static bool IsModuleVariable(VariableExpressionAst variable)
        => variable.VariablePath.UserPath.StartsWith("script:", StringComparison.OrdinalIgnoreCase);

    private static bool IsDerivedSymbolRead(
        VariableExpressionAst variable,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols)
    {
        if (IsPureWriteTarget(variable)) return false;
        return symbols.TryGetValue(variable.VariablePath.UserPath, out var binding) && binding.IsModuleStateDerived;
    }

    private static bool IsPureWriteTarget(VariableExpressionAst variable)
    {
        Ast target = variable;
        if (target.Parent is ConvertExpressionAst conversion && ReferenceEquals(conversion.Child, target)) target = conversion;
        return target.Parent is AssignmentStatementAst { Operator: TokenKind.Equals } assignment &&
               ReferenceEquals(assignment.Left, target);
    }
}
