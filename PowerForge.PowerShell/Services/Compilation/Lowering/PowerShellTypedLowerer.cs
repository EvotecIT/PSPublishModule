namespace PowerForge;

/// <summary>
/// Selects typed CLR operations from analyzed bound nodes. It does not render target-language source.
/// </summary>
internal sealed class PowerShellTypedLowerer
{
    internal PowerShellLoweredProgram Lower(PowerShellBoundProgram program)
    {
        if (program is null) throw new ArgumentNullException(nameof(program));
        var diagnostics = new List<PowerShellSemanticDiagnostic>(program.Diagnostics);
        var functions = new List<PowerShellLoweredFunction>();
        var bySymbol = program.Functions.ToDictionary(static function => function.Symbol.StableKey, StringComparer.Ordinal);
        foreach (var function in program.Functions)
        {
            if (function.Disposition.Kind != PowerShellExecutionDispositionKind.Typed)
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic(
                    string.IsNullOrWhiteSpace(function.Disposition.ReasonCode) ? "PSL1001" : function.Disposition.ReasonCode,
                    function.Disposition.Explanation,
                    function.Symbol.Declaration));
                continue;
            }

            var statements = new List<PowerShellLoweredStatement>();
            var declared = new HashSet<string>(StringComparer.Ordinal);
            var localTypes = function.Locals.ToDictionary(static local => local.Symbol.StableKey, static local => local.Type.ClrType, StringComparer.Ordinal);
            var symbolTypes = function.Parameters.ToDictionary(static parameter => parameter.Symbol.StableKey, static parameter => parameter.Type.ClrType, StringComparer.Ordinal);
            foreach (var local in function.Locals) symbolTypes[local.Symbol.StableKey] = local.Type.ClrType;
            foreach (var statement in function.Body.Statements)
            {
                switch (statement)
                {
                    case PowerShellBoundAssignmentStatement assignment:
                        statements.Add(new PowerShellLoweredAssignmentStatement(
                            assignment.Span,
                            assignment.Target,
                            symbolTypes[assignment.Target.StableKey],
                            LowerExpression(assignment.Value, bySymbol),
                            localTypes.ContainsKey(assignment.Target.StableKey) && declared.Add(assignment.Target.StableKey)));
                        break;
                    case PowerShellBoundReturnStatement returned:
                        statements.Add(new PowerShellLoweredReturnStatement(returned.Span, returned.Expression is null ? null : LowerExpression(returned.Expression, bySymbol)));
                        break;
                    case PowerShellBoundExpressionStatement expression:
                        statements.Add(new PowerShellLoweredReturnStatement(expression.Span, LowerExpression(expression.Expression, bySymbol)));
                        break;
                    default:
                        throw new InvalidOperationException($"Bound statement '{statement.GetType().Name}' reached typed lowering without an owner.");
                }
            }

            functions.Add(new PowerShellLoweredFunction(
                function.Symbol,
                PowerShellCSharpMethodEmitter.SanitizeIdentifier(function.Symbol.Name),
                function.ReturnType.ClrType,
                function.Parameters.Select(static parameter => new PowerShellLoweredParameter(parameter.Symbol, parameter.Type.ClrType)).ToArray(),
                function.Locals.Select(static local => new PowerShellLoweredLocal(local.Symbol, local.Type.ClrType)).ToArray(),
                statements.ToArray(),
                function.Body.Span));
        }

        return new PowerShellLoweredProgram(
            functions.OrderBy(static function => function.Symbol.StableKey, StringComparer.Ordinal).ToArray(),
            diagnostics.OrderBy(static diagnostic => diagnostic.Span.DocumentId, StringComparer.Ordinal)
                .ThenBy(static diagnostic => diagnostic.Span.StartOffset)
                .ThenBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .ToArray());
    }

    private static PowerShellLoweredExpression LowerExpression(
        PowerShellBoundExpression expression,
        IReadOnlyDictionary<string, PowerShellBoundFunction> functions)
        => expression switch
        {
            PowerShellBoundLiteralExpression literal => new PowerShellLoweredLiteralExpression(literal.Span, literal.Type.ClrType, literal.Value),
            PowerShellBoundVariableExpression variable => new PowerShellLoweredVariableExpression(variable.Span, variable.Type.ClrType, variable.Symbol),
            PowerShellBoundConversionExpression conversion => new PowerShellLoweredConversionExpression(
                conversion.Span,
                conversion.Type.ClrType,
                LowerExpression(conversion.Operand, functions)),
            PowerShellBoundInvocationExpression invocation when functions.TryGetValue(invocation.Target.StableKey, out var target) =>
                new PowerShellLoweredInvocationExpression(
                    invocation.Span,
                    target.ReturnType.ClrType,
                    invocation.Target,
                    invocation.Arguments.Select(argument => LowerExpression(argument, functions)).ToArray()),
            _ => throw new InvalidOperationException($"Bound expression '{expression.GetType().Name}' reached typed lowering without an owner.")
        };
}
