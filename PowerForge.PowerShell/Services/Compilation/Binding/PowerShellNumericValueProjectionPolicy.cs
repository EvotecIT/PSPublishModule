using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Represents an unobserved Int32 counter as Double when literal additive updates and
/// exclusively Double-valued consumers make its numeric value identical before and after promotion.
/// This is a local representation proof, not permission to expose a different PowerShell type.
/// </summary>
internal static class PowerShellNumericValueProjectionPolicy
{
    internal static bool CanProject(
        FunctionDefinitionAst function,
        string name,
        IReadOnlyList<AssignmentStatementAst> assignments)
    {
        if (function.Body.BeginBlock is not null || function.Body.ProcessBlock is not null ||
            function.Body.DynamicParamBlock is not null || function.Body.EndBlock is not { Unnamed: true } ||
            function.Body.GetType().GetProperty("CleanBlock")?.GetValue(function.Body) is not null)
            return false;
        // Dynamic commands, nested scopes, and error handlers can observe locals independently
        // of the ordinary expression graph. They are outside this representation contract.
        if (function.Body.FindAll(static node => node is CommandAst or ScriptBlockExpressionAst or
                FunctionDefinitionAst or TryStatementAst or TrapStatementAst, false).Any())
            return false;
        var writes = assignments.Where(assignment =>
            PowerShellAssignmentTargetPolicy.FindDirectVariable(assignment.Left)?.VariablePath.UserPath
                .Equals(name, StringComparison.OrdinalIgnoreCase) == true).ToArray();
        if (writes.Length < 2 || writes[0].Operator != TokenKind.Equals ||
            !writes.Any(static write => write.Operator is TokenKind.PlusEquals or TokenKind.MinusEquals))
            return false;
        if (writes.Any(write => write.Left is not VariableExpressionAst { VariablePath.IsUnqualified: true } ||
                write.Operator is not (TokenKind.Equals or TokenKind.PlusEquals or TokenKind.MinusEquals) ||
                write.Parent is not (NamedBlockAst or StatementBlockAst or ForStatementAst) ||
                !IsInt32Literal(write.Right)))
            return false;
        foreach (var variable in function.Body.FindAll(static node => node is VariableExpressionAst, false)
                     .Cast<VariableExpressionAst>())
        {
            var path = variable.VariablePath.UserPath;
            if (!path.Split(':').Last().Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            if (!variable.VariablePath.IsUnqualified || variable.Parent is UnaryExpressionAst or ForEachStatementAst)
                return false;
        }
        return true;
    }

    internal static bool Validate(
        PowerShellBoundBlock body,
        IReadOnlyList<PowerShellBoundLocal> locals,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var projected = locals.Where(static local =>
            local.Type.Provenance == PowerShellTypeFactProvenance.NumericValueProjection).ToArray();
        if (projected.Length == 0) return true;
        var expressions = PowerShellSemanticAnalyzer.EnumerateStatements(body)
            .SelectMany(PowerShellSemanticAnalyzer.EnumerateDirectExpressions)
            .SelectMany(PowerShellSemanticAnalyzer.EnumerateExpressions).ToArray();
        // Only Math's closed primitive operations are admitted while a projected local exists.
        // Arbitrary CLR calls or property getters could call back into the hosting runspace.
        var opaqueCall = expressions.Any(static expression => expression.Capabilities != PowerShellRequiredCapability.None ||
            expression is PowerShellBoundInvocationExpression or PowerShellBoundRuntimeStateExpression ||
            expression is PowerShellBoundClrInvocationExpression invocation &&
                (invocation.InvocationKind != PowerShellClrInvocationKind.StaticMethod || invocation.DeclaringType != typeof(Math)) ||
            expression is PowerShellBoundClrMemberExpression);
        var accepted = new HashSet<PowerShellBoundVariableExpression>();
        foreach (var binary in expressions.OfType<PowerShellBoundBinaryExpression>())
        {
            if (binary.Operation is not (PowerShellBoundBinaryOperator.Add or PowerShellBoundBinaryOperator.Subtract or
                PowerShellBoundBinaryOperator.Multiply or PowerShellBoundBinaryOperator.Divide or
                PowerShellBoundBinaryOperator.Remainder or PowerShellBoundBinaryOperator.Equal or
                PowerShellBoundBinaryOperator.NotEqual or PowerShellBoundBinaryOperator.LessThan or
                PowerShellBoundBinaryOperator.LessThanOrEqual or PowerShellBoundBinaryOperator.GreaterThan or
                PowerShellBoundBinaryOperator.GreaterThanOrEqual)) continue;
            if (binary.Left is PowerShellBoundVariableExpression left && IsProjected(left) && IsOriginalDouble(binary.Right))
                accepted.Add(left);
            if (binary.Right is PowerShellBoundVariableExpression right && IsProjected(right) && IsOriginalDouble(binary.Left))
                accepted.Add(right);
        }
        var exposed = expressions.OfType<PowerShellBoundVariableExpression>()
            .FirstOrDefault(variable => IsProjected(variable) && !accepted.Contains(variable));
        if (!opaqueCall && exposed is null) return true;
        diagnostics.Add(new PowerShellSemanticDiagnostic(
            "PSB2412",
            "An unconstrained numeric local can use a Double value representation only when every read consumes its value with an existing Double operand and no hosted or opaque CLR observer can inspect its Int32-or-Double identity.",
            exposed?.Span ?? projected[0].Symbol.Declaration));
        return false;
    }

    private static bool IsProjected(PowerShellBoundVariableExpression variable)
        => variable.Type.Provenance == PowerShellTypeFactProvenance.NumericValueProjection;

    private static bool IsOriginalDouble(PowerShellBoundExpression expression)
        => expression.Type.ClrType == typeof(double) &&
           expression.Type.Provenance != PowerShellTypeFactProvenance.NumericValueProjection &&
           (expression is not PowerShellBoundConversionExpression conversion || conversion.Operand.Type.ClrType == typeof(double));

    private static bool IsInt32Literal(Ast syntax)
    {
        while (true)
        {
            switch (syntax)
            {
                case StatementBlockAst { Statements.Count: 1 } block: syntax = block.Statements[0]; continue;
                case PipelineAst { PipelineElements.Count: 1 } pipeline: syntax = pipeline.PipelineElements[0]; continue;
                case CommandExpressionAst command: syntax = command.Expression; continue;
                case ParenExpressionAst parentheses: syntax = parentheses.Pipeline; continue;
                case ConstantExpressionAst { Value: int }: return true;
                case UnaryExpressionAst { TokenKind: TokenKind.Minus or TokenKind.Plus, Child: ConstantExpressionAst { Value: int value } }:
                    return value != int.MinValue;
                default: return false;
            }
        }
    }
}
