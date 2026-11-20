using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    // A direct assignment followed only by closed initializers cannot expose an array alias
    // or invalidate either a record range or an enclosing counter's initial value.
    private static PowerShellBoundExpression? FindStrictPipelineInitializer(
        IReadOnlyList<PowerShellBoundStatement> preceding, PowerShellSymbolId symbol)
    {
        for (var index = preceding.Count - 1; index >= 0; index--)
        {
            if (preceding[index] is not PowerShellBoundAssignmentStatement
                { Operation: PowerShellBoundMutationOperator.Assign } assignment)
                return null;
            if (assignment.Target.StableKey == symbol.StableKey)
                return assignment.Value;
            if (assignment.Value is PowerShellBoundArrayExpression array &&
                array.Elements.All(static element => IsTotalStrictStageExpression(element, null)))
                continue;
            if (!IsTotalStrictStageExpression(assignment.Value, null))
                return null;
        }
        return null;
    }

    private static PowerShellInt32Range? GetStrictElementRange(PowerShellBoundArrayExpression array)
    {
        if (array.Elements.Length == 0) return new(0, 0);
        var minimum = int.MaxValue;
        var maximum = int.MinValue;
        foreach (var item in array.Elements)
        {
            if (PowerShellInt32RangePolicy.GetRange(item) is not { } range) return null;
            minimum = Math.Min(minimum, range.Minimum);
            maximum = Math.Max(maximum, range.Maximum);
        }
        return new(minimum, maximum);
    }

    private static PowerShellBoundStatement? BindStrictPipelineCounter(
        StatementAst syntax,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyList<PowerShellBoundStatement> preceding,
        PowerShellBoundArrayExpression? initializer,
        ISet<string> counterWrites,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        SourceSpan span,
        Func<StatementAst, IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding>, PowerShellBoundStatement?> bind)
    {
        if (!TryGetStrictStageIncrement(syntax, out var variable) ||
            variable.VariablePath.UserPath.Equals("_", StringComparison.OrdinalIgnoreCase) ||
            variable.VariablePath.UserPath.Equals("PSItem", StringComparison.OrdinalIgnoreCase) ||
            !symbols.TryGetValue(variable.VariablePath.UserPath, out var target) ||
            target.Symbol.Kind != PowerShellSymbolKind.Local || target.Type.ClrType != typeof(int) ||
            initializer is null || FindStrictPipelineInitializer(preceding, target.Symbol) is not { } initialValue ||
            PowerShellInt32RangePolicy.GetRange(initialValue) is not { } range ||
            (long)range.Maximum + initializer.Elements.Length > int.MaxValue ||
            !counterWrites.Add(target.Symbol.StableKey))
        {
            RejectStrictPipeline(diagnostics, span,
                "requires each record counter to be a distinct initialized Int32 local with a proven non-overflowing bound");
            return null;
        }

        var updateSymbols = CloneSymbols(symbols);
        updateSymbols[variable.VariablePath.UserPath].ForgetValueState();
        return bind(syntax, updateSymbols);
    }

    // Cmdlet script blocks can report nonterminating errors per record. Limit fusion to
    // operations whose bound operands prove that no PowerShell error path is reachable.
    private static bool IsTotalStrictStageExpression(PowerShellBoundExpression expression, PowerShellSymbolId? current)
    {
        if (expression.Effects != PowerShellSemanticEffect.None || expression.Capabilities != PowerShellRequiredCapability.None)
            return false;
        return expression switch
        {
            PowerShellBoundLiteralExpression literal =>
                literal.Value is null || PowerShellStableScalarTypePolicy.IsSupported(literal.Type),
            PowerShellBoundVariableExpression variable => variable.Symbol.StableKey == current?.StableKey,
            PowerShellBoundConversionExpression conversion =>
                conversion.Type.ClrType == conversion.Operand.Type.ClrType &&
                conversion.Type.ClrType != typeof(string) &&
                IsTotalStrictStageExpression(conversion.Operand, current),
            PowerShellBoundUnaryExpression { Operation: PowerShellBoundUnaryOperator.LogicalNot } unary =>
                unary.Operand.Type.ClrType == typeof(bool) && IsTotalStrictStageExpression(unary.Operand, current),
            PowerShellBoundBinaryExpression binary =>
                IsTotalStrictStageBinary(binary) &&
                IsTotalStrictStageExpression(binary.Left, current) &&
                IsTotalStrictStageExpression(binary.Right, current),
            _ => false
        };
    }

    private static bool IsTotalStrictStageBinary(PowerShellBoundBinaryExpression binary)
        => binary.Operation switch
        {
            PowerShellBoundBinaryOperator.NullEqual or PowerShellBoundBinaryOperator.NullNotEqual => true,
            PowerShellBoundBinaryOperator.LogicalAnd or PowerShellBoundBinaryOperator.LogicalOr =>
                binary.Left.Type.ClrType == typeof(bool) && binary.Right.Type.ClrType == typeof(bool),
            PowerShellBoundBinaryOperator.Equal or PowerShellBoundBinaryOperator.NotEqual or
            PowerShellBoundBinaryOperator.LessThan or PowerShellBoundBinaryOperator.LessThanOrEqual or
            PowerShellBoundBinaryOperator.GreaterThan or PowerShellBoundBinaryOperator.GreaterThanOrEqual =>
                binary.Left.Type.ClrType == typeof(int) && binary.Right.Type.ClrType == typeof(int),
            PowerShellBoundBinaryOperator.Add or PowerShellBoundBinaryOperator.Subtract or
            PowerShellBoundBinaryOperator.Multiply => PowerShellInt32RangePolicy.GetRange(binary) is not null,
            _ => false
        };
}
