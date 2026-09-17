namespace PowerForge;

/// <summary>
/// Projects closed native storage operations onto the guarded region's parameter values and
/// freshly initialized locals. Unsupported operations retain their native owner; syntax is never rebound.
/// </summary>
internal static class PowerShellBoundRegionLocalProjection
{
    internal static PowerShellBoundStatement? Project(PowerShellBoundStatement statement,
        IReadOnlyList<PowerShellBoundParameter> parameters, IReadOnlyList<PowerShellBoundLocal> initializedLocals,
        IReadOnlyList<PowerShellBoundLocal> functionLocals)
    {
        if (!statement.Capabilities.HasFlag(PowerShellRequiredCapability.NativeFunctionBinding)) return statement;
        var values = parameters.Select(static parameter => (parameter.Symbol, parameter.Type))
            .Concat(initializedLocals.Select(static local => (local.Symbol, local.Type)))
            .ToDictionary(static value => value.Symbol.Name, StringComparer.OrdinalIgnoreCase);
        var targets = functionLocals.ToDictionary(static local => local.Symbol.Name, static local => local.Symbol, StringComparer.OrdinalIgnoreCase);
        return Statement(statement);

        PowerShellBoundStatement? Statement(PowerShellBoundStatement current)
        {
            if (current is PowerShellBoundStatementErrorBoundary boundary)
                return boundary.Body.Statements.Length == 1 ? Statement(boundary.Body.Statements[0]) : null;
            if (current is PowerShellBoundNativeAssignmentStatement { Operation: PowerShellBoundMutationOperator.Assign } assigned)
                return Assignment(assigned.Span, assigned.Name, assigned.Value);
            if (current is PowerShellBoundExpressionStatement { EmitsOutput: false,
                    Expression: PowerShellBoundMutationExpression { Operation: PowerShellBoundMutationOperator.Assign, Value: not null,
                        NativeTargetRead: { DirectLocal: true } } mutation })
                return Assignment(mutation.Span, mutation.NativeTargetRead.Name, mutation.Value);
            if (current is PowerShellBoundExpressionStatement expressionStatement)
            {
                var expression = Expression(expressionStatement.Expression);
                return expression is null
                    ? null
                    : new PowerShellBoundExpressionStatement(
                        expressionStatement.Span,
                        expression,
                        expressionStatement.EmitsOutput,
                        expressionStatement.RequiresOutputContinuation);
            }
            if (current is PowerShellBoundIfStatement conditional)
            {
                var clauses = new List<PowerShellBoundConditionalClause>();
                foreach (var clause in conditional.Clauses)
                {
                    var condition = Expression(clause.Condition);
                    var body = Block(clause.Body);
                    if (condition?.Type.ClrType != typeof(bool) || body is null) return null;
                    clauses.Add(new PowerShellBoundConditionalClause(condition, body));
                }
                var otherwise = conditional.ElseBlock is null ? null : Block(conditional.ElseBlock);
                if (conditional.ElseBlock is not null && otherwise is null) return null;
                return new PowerShellBoundIfStatement(current.Span, clauses.ToArray(), otherwise);
            }
            return current.Capabilities.HasFlag(PowerShellRequiredCapability.NativeFunctionBinding) ? null : current;
        }

        PowerShellBoundBlock? Block(PowerShellBoundBlock block)
        {
            var statements = new List<PowerShellBoundStatement>();
            foreach (var item in block.Statements)
            {
                var projected = Statement(item);
                if (projected is null) return null;
                statements.Add(projected);
            }
            return new PowerShellBoundBlock(block.Span, statements.ToArray());
        }

        PowerShellBoundAssignmentStatement? Assignment(SourceSpan span, string name, PowerShellBoundExpression source)
        {
            if (name.IndexOf(':') >= 0 || !targets.TryGetValue(name, out var target)) return null;
            var value = Expression(source);
            if (value is null || !PowerShellRegionTransferTypePolicy.IsSupported(value.Type) ||
                values.TryGetValue(name, out var existing) && existing.Type.ClrType != value.Type.ClrType)
                return null;
            return new PowerShellBoundAssignmentStatement(span, target, value);
        }

        PowerShellBoundExpression? Expression(PowerShellBoundExpression expression)
        {
            if (expression is PowerShellBoundNativeVariableExpression { DirectLocal: true } variable)
            {
                if (variable.Name.IndexOf(':') >= 0 || !values.TryGetValue(variable.Name, out var value) ||
                    !PowerShellRegionTransferTypePolicy.IsSupported(value.Type)) return null;
                return new PowerShellBoundVariableExpression(variable.Span, value.Symbol, value.Type);
            }
            if (expression is PowerShellBoundConversionExpression conversion)
            {
                var operand = Expression(conversion.Operand);
                // Only identity conversion is closed here. Other conversion, truthiness,
                // or constraint-transition rules stay with their existing native operation.
                if (operand is null || operand.Type.ClrType != conversion.Type.ClrType) return null;
                return conversion.NormalizeNullString
                    ? new PowerShellBoundConversionExpression(conversion.Span, conversion.Type, operand, normalizeNullString: true)
                    : operand;
            }
            if (expression is PowerShellBoundInterpolatedStringExpression text)
            {
                var parts = new List<PowerShellBoundInterpolatedStringPart>();
                foreach (var part in text.Parts)
                {
                    if (part.Expression is null) { parts.Add(part); continue; }
                    var value = Expression(part.Expression);
                    if (value is null || !PowerShellStableScalarTypePolicy.IsSupported(value.Type)) return null;
                    parts.Add(new PowerShellBoundInterpolatedStringPart(part.Text, value,
                        value.Type.Provenance == PowerShellTypeFactProvenance.Int32OrDouble));
                }
                return new PowerShellBoundInterpolatedStringExpression(text.Span, parts.ToArray());
            }
            return expression.Capabilities.HasFlag(PowerShellRequiredCapability.NativeFunctionBinding) ? null : expression;
        }
    }
}
