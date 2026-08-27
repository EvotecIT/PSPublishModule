using System.Management.Automation.Language;

namespace PowerForge;

internal sealed class PowerShellSemanticSymbolBinding
{
    internal PowerShellSemanticSymbolBinding(PowerShellSymbolId symbol, PowerShellTypeFact type)
    {
        Symbol = symbol;
        Type = type;
        ValueState = PowerShellValueState.Unknown;
    }

    internal PowerShellSymbolId Symbol { get; }
    internal PowerShellTypeFact Type { get; private set; }
    internal PowerShellValueState ValueState { get; private set; }

    internal void Refine(PowerShellTypeFact type, PowerShellValueState valueState)
    {
        if (Type.Provenance == PowerShellTypeFactProvenance.Unknown) Type = type;
        ValueState = valueState;
    }
}

/// <summary>Owns local and parameter mutation semantics.</summary>
internal static class PowerShellMutationSemanticBinder
{
    internal static PowerShellBoundMutationExpression? BindAssignment(
        ParsedSourceDocument document,
        AssignmentStatementAst syntax,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        Func<Ast, Type?, PowerShellBoundExpression?> bindExpression,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var variable = PowerShellAssignmentTargetPolicy.FindDirectVariable(syntax.Left);
        if (variable is null || !symbols.TryGetValue(variable.VariablePath.UserPath, out var target)) return null;
        var operation = syntax.Operator.ToString() switch
        {
            "Equals" => PowerShellBoundMutationOperator.Assign,
            "PlusEquals" => PowerShellBoundMutationOperator.Add,
            "MinusEquals" => PowerShellBoundMutationOperator.Subtract,
            "MultiplyEquals" => PowerShellBoundMutationOperator.Multiply,
            "DivideEquals" => PowerShellBoundMutationOperator.Divide,
            "RemEquals" => PowerShellBoundMutationOperator.Remainder,
            _ => (PowerShellBoundMutationOperator?)null
        };
        if (operation is null) return null;
        var targetType = target.Type.ClrType;
        var value = bindExpression(syntax.Right, target.Type.Provenance == PowerShellTypeFactProvenance.Unknown ? null : targetType);
        if (value is null) return null;
        if (value.Type.ClrType == typeof(void))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2406", "A void CLR invocation or output-free mutation cannot be assigned to a PowerShell value.", PowerShellSourceParser.GetSpan(document, syntax.Right.Extent)));
            return null;
        }
        if (operation == PowerShellBoundMutationOperator.Assign)
        {
            target.Refine(
                new PowerShellTypeFact(value.Type.ClrType, PowerShellTypeFactProvenance.Inferred, $"The first bound assignment to '${target.Symbol.Name}' provides a stable CLR representation."),
                value.ValueState);
            targetType = target.Type.ClrType;
        }
        if (operation == PowerShellBoundMutationOperator.Assign && !PowerShellClrTypeSemantics.CanAssign(targetType, value.Type.ClrType))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2401", $"Assignment requires PowerShell conversion from '{value.Type.ClrType.FullName}' to '{targetType.FullName}', which is not an implicit CLR conversion.", PowerShellSourceParser.GetSpan(document, syntax.Extent)));
            return null;
        }
        if (operation != PowerShellBoundMutationOperator.Assign &&
            !PowerShellCSharpOperatorPolicy.SupportsCompoundAssignment(syntax.Operator.ToString(), targetType, value.Type.ClrType))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2402", $"Compound assignment '{syntax.Operator}' is not defined for CLR types '{targetType.FullName}' and '{value.Type.ClrType.FullName}' on the conservative compilation path.", PowerShellSourceParser.GetSpan(document, syntax.Extent)));
            return null;
        }
        var explicitType = target.Type.Provenance == PowerShellTypeFactProvenance.Explicit;
        if (operation != PowerShellBoundMutationOperator.Assign && PowerShellClrTypeSemantics.IsIntegral(targetType) && !explicitType)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2403", $"Integral compound assignment to untyped local '${target.Symbol.Name}' can promote dynamically in PowerShell and is not eligible for typed compilation.", PowerShellSourceParser.GetSpan(document, syntax.Extent)));
            return null;
        }
        return new PowerShellBoundMutationExpression(
            PowerShellSourceParser.GetSpan(document, syntax.Extent),
            target.Symbol,
            targetType,
            operation.Value,
            value,
            target.Type,
            operation == PowerShellBoundMutationOperator.Assign && explicitType && targetType == typeof(string),
            operation != PowerShellBoundMutationOperator.Assign && PowerShellClrTypeSemantics.IsIntegral(targetType));
    }

    internal static bool TryBindIncrement(
        ParsedSourceDocument document,
        UnaryExpressionAst syntax,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        out PowerShellBoundMutationExpression? mutation,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        mutation = null;
        var operation = syntax.TokenKind.ToString() switch
        {
            "PlusPlus" => PowerShellBoundMutationOperator.Increment,
            "MinusMinus" => PowerShellBoundMutationOperator.Decrement,
            "PostfixPlusPlus" => PowerShellBoundMutationOperator.PostIncrement,
            "PostfixMinusMinus" => PowerShellBoundMutationOperator.PostDecrement,
            _ => (PowerShellBoundMutationOperator?)null
        };
        if (operation is null) return false;
        if (!IsStandaloneStatement(syntax))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2407", "Value-producing increment and decrement contexts require PowerShell expression-result semantics.", PowerShellSourceParser.GetSpan(document, syntax.Extent)));
            return true;
        }
        var operand = UnwrapExpression(syntax.Child) as VariableExpressionAst;
        if (operand is null || !symbols.TryGetValue(operand.VariablePath.UserPath, out var target)) return false;
        if (!PowerShellCSharpOperatorPolicy.SupportsIncrement(target.Type.ClrType) || target.Type.Provenance != PowerShellTypeFactProvenance.Explicit)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2404", $"Increment or decrement of '${target.Symbol.Name}' requires one explicitly typed supported CLR representation.", PowerShellSourceParser.GetSpan(document, syntax.Extent)));
            return true;
        }
        mutation = new PowerShellBoundMutationExpression(
            PowerShellSourceParser.GetSpan(document, syntax.Extent),
            target.Symbol,
            target.Type.ClrType,
            operation.Value,
            null,
            new PowerShellTypeFact(typeof(void), PowerShellTypeFactProvenance.Inferred, "Increment and decrement are statement-valued on the conservative path."),
            false,
            PowerShellClrTypeSemantics.IsIntegral(target.Type.ClrType));
        return true;
    }

    private static Ast UnwrapExpression(Ast syntax)
    {
        while (syntax is CommandExpressionAst command) syntax = command.Expression;
        while (syntax is ParenExpressionAst parenthesized) syntax = parenthesized.Pipeline;
        return syntax;
    }

    private static bool IsStandaloneStatement(Ast syntax)
    {
        Ast current = syntax;
        while (current.Parent is CommandExpressionAst or PipelineAst) current = current.Parent;
        return current.Parent is NamedBlockAst or StatementBlockAst ||
               current.Parent is ForStatementAst loop &&
               (ReferenceEquals(loop.Initializer, current) || ReferenceEquals(loop.Iterator, current));
    }
}
