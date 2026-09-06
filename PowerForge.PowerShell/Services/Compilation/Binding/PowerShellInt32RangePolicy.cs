using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Proves exact Int32 arithmetic from immutable bound operands and closed counter loops.</summary>
internal static class PowerShellInt32RangePolicy
{
    internal static PowerShellInt32Range? GetRange(PowerShellBoundExpression expression)
    {
        if (expression.Type.ClrType != typeof(int)) return null;
        if (expression.Type.Int32Range is { } range) return range;
        if (expression is PowerShellBoundLiteralExpression { Value: int value }) return new(value, value);
        if (expression is PowerShellBoundClrMemberExpression
            { IsStatic: false, MemberName: "Length", Receiver: { } receiver } member &&
            (receiver.Type.ClrType.IsArray || member.DeclaringType == typeof(string)))
            return new(0, int.MaxValue);
        return null;
    }

    internal static bool TryBindArithmetic(SourceSpan span, string operation,
        PowerShellBoundExpression left, PowerShellBoundExpression right, out PowerShellBoundExpression? result)
    {
        result = null;
        if (operation is not ("Plus" or "Minus") || GetRange(left) is not { } a || GetRange(right) is not { } b)
            return false;
        var minimum = operation == "Plus" ? (long)a.Minimum + b.Minimum : (long)a.Minimum - b.Maximum;
        var maximum = operation == "Plus" ? (long)a.Maximum + b.Maximum : (long)a.Maximum - b.Minimum;
        if (minimum < int.MinValue || maximum > int.MaxValue) return false;
        result = new PowerShellBoundBinaryExpression(span,
            operation == "Plus" ? PowerShellBoundBinaryOperator.Add : PowerShellBoundBinaryOperator.Subtract,
            left, right, new PowerShellTypeFact(typeof(int), PowerShellTypeFactProvenance.Inferred,
                "Both arithmetic endpoints remain Int32; PowerShell cannot promote this operation.",
                int32Range: new((int)minimum, (int)maximum)));
        return true;
    }

    internal static void RefineDescendingCounter(ForStatementAst syntax, PowerShellBoundMutationExpression? initializer,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols)
    {
        if (initializer is not { Operation: PowerShellBoundMutationOperator.Assign, Value: { } value } ||
            GetRange(value) is not { } initial || initial.Maximum < 1 ||
            !symbols.TryGetValue(initializer.Target.Name, out var counter) || counter.Type.ClrType != typeof(int) ||
            Unwrap(syntax.Condition) is not BinaryExpressionAst { Operator: TokenKind.Igt, Right: ConstantExpressionAst { Value: 0 } } condition ||
            !IsCounter(condition.Left, counter.Symbol.Name) ||
            Unwrap(syntax.Iterator) is not UnaryExpressionAst { TokenKind: TokenKind.MinusMinus or TokenKind.PostfixMinusMinus } iterator ||
            !IsCounter(iterator.Child, counter.Symbol.Name)) return;
        // Any body write, alias, reference, nested scope, or hosted command invalidates the induction.
        if (syntax.Body.FindAll(static node => node is CommandAst or FunctionDefinitionAst or ScriptBlockExpressionAst,
                searchNestedScriptBlocks: false).Any()) return;
        foreach (var variable in syntax.Body.FindAll(static node => node is VariableExpressionAst, false).Cast<VariableExpressionAst>())
        {
            if (!variable.VariablePath.UserPath.Split(':').Last().Equals(counter.Symbol.Name, StringComparison.OrdinalIgnoreCase)) continue;
            if (!variable.VariablePath.IsUnqualified || variable.Parent is UnaryExpressionAst or ForEachStatementAst ||
                variable.Parent is ConvertExpressionAst conversion && conversion.StaticType == typeof(System.Management.Automation.PSReference)) return;
        }
        if (syntax.Body.FindAll(static node => node is AssignmentStatementAst, false).Cast<AssignmentStatementAst>()
            .Any(assignment => PowerShellAssignmentTargetPolicy.FindDirectVariable(assignment.Left)?.VariablePath.UserPath
                .Equals(counter.Symbol.Name, StringComparison.OrdinalIgnoreCase) == true)) return;
        counter.SetInt32Range(new(1, initial.Maximum));
    }

    internal static bool CanDecrement(PowerShellSemanticSymbolBinding target, PowerShellBoundMutationOperator operation)
        => operation is PowerShellBoundMutationOperator.Decrement or PowerShellBoundMutationOperator.PostDecrement &&
           target.Type.Int32Range is { Minimum: > int.MinValue };

    private static bool IsCounter(Ast? syntax, string name)
        => syntax is VariableExpressionAst { VariablePath.IsUnqualified: true } variable &&
           variable.VariablePath.UserPath.Equals(name, StringComparison.OrdinalIgnoreCase);

    private static Ast? Unwrap(Ast? syntax)
    {
        while (true)
        {
            switch (syntax)
            {
                case PipelineAst { PipelineElements.Count: 1 } pipeline: syntax = pipeline.PipelineElements[0]; continue;
                case CommandExpressionAst command: syntax = command.Expression; continue;
                case ParenExpressionAst parentheses: syntax = parentheses.Pipeline; continue;
                default: return syntax;
            }
        }
    }
}
