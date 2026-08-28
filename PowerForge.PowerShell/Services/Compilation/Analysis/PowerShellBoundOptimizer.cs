namespace PowerForge;

/// <summary>Runs deterministic, semantics-preserving rewrites over immutable bound IR.</summary>
internal sealed class PowerShellBoundOptimizer
{
    private int _constantExpressionsFolded;
    private int _deadBranchesRemoved;
    private int _identityConversionsRemoved;
    private int _pipelineStagesFused;
    private int _commandRegionStatementsCoalesced;
    private int _specializedCollectionLoops;
    private int _runtimeConversionSitesSpecialized;
    private int _sourceMappedStatements;

    internal PowerShellBoundOptimizationResult Optimize(PowerShellBoundProgram program)
    {
        if (program is null) throw new ArgumentNullException(nameof(program));
        _constantExpressionsFolded = 0;
        _deadBranchesRemoved = 0;
        _identityConversionsRemoved = 0;
        _pipelineStagesFused = 0;
        _commandRegionStatementsCoalesced = 0;
        _specializedCollectionLoops = 0;
        _runtimeConversionSitesSpecialized = 0;
        _sourceMappedStatements = 0;
        var functions = program.Functions.Select(function => function.WithBody(OptimizeBlock(function.Body))).ToArray();
        return new PowerShellBoundOptimizationResult(
            program.WithFunctions(functions),
            new PowerShellBoundOptimizationEvidence(
                _constantExpressionsFolded,
                _deadBranchesRemoved,
                _identityConversionsRemoved,
                _pipelineStagesFused,
                _commandRegionStatementsCoalesced,
                _specializedCollectionLoops,
                _runtimeConversionSitesSpecialized,
                _sourceMappedStatements));
    }

    private PowerShellBoundBlock OptimizeBlock(PowerShellBoundBlock block)
    {
        var statements = new List<PowerShellBoundStatement>();
        foreach (var statement in block.Statements)
        {
            _sourceMappedStatements++;
            if (statement is PowerShellBoundCommandRegionStatement region)
            {
                _pipelineStagesFused += Math.Max(0, region.Stages.Length - 1);
                _commandRegionStatementsCoalesced += Math.Max(0, region.StatementCount - 1);
            }
            if (statement is PowerShellBoundCommandCaptureStatement capture)
                _pipelineStagesFused += Math.Max(0, capture.Stages.Length - 1);
            if (statement is PowerShellBoundForEachStatement { Collection.Type.ClrType.IsArray: true })
                _specializedCollectionLoops++;
            if (statement is PowerShellBoundIfStatement { Clauses.Length: 1 } conditional)
            {
                var clause = conditional.Clauses[0];
                var condition = OptimizeExpression(clause.Condition);
                var body = OptimizeBlock(clause.Body);
                var alternative = conditional.ElseBlock is null ? null : OptimizeBlock(conditional.ElseBlock);
                if (TryGetBoolean(condition, out var selected))
                {
                    _deadBranchesRemoved++;
                    statements.AddRange((selected ? body : alternative)?.Statements.ToArray() ?? Array.Empty<PowerShellBoundStatement>());
                    continue;
                }
                statements.Add(new PowerShellBoundIfStatement(
                    conditional.Span,
                    new[] { new PowerShellBoundConditionalClause(condition, body) },
                    alternative));
                continue;
            }
            if (statement is PowerShellBoundWhileStatement loop)
            {
                var condition = OptimizeExpression(loop.Condition);
                var body = OptimizeBlock(loop.Body);
                if (TryGetBoolean(condition, out var execute) && !execute)
                {
                    _deadBranchesRemoved++;
                    continue;
                }
                statements.Add(new PowerShellBoundWhileStatement(loop.Span, condition, body));
                continue;
            }
            statements.Add(OptimizeStatement(statement));
        }
        return new PowerShellBoundBlock(block.Span, statements.ToArray());
    }

    private PowerShellBoundStatement OptimizeStatement(PowerShellBoundStatement statement)
        => statement switch
        {
            PowerShellBoundAssignmentStatement assignment => new PowerShellBoundAssignmentStatement(
                assignment.Span, assignment.Target, OptimizeExpression(assignment.Value), assignment.Operation,
                assignment.NormalizeNullString, assignment.CheckedIntegral),
            PowerShellBoundReturnStatement returned => new PowerShellBoundReturnStatement(
                returned.Span, returned.Expression is null ? null : OptimizeExpression(returned.Expression), returned.EmitsValue),
            PowerShellBoundExpressionStatement expression => new PowerShellBoundExpressionStatement(
                expression.Span, OptimizeExpression(expression.Expression), expression.EmitsOutput),
            PowerShellBoundForStatement loop => new PowerShellBoundForStatement(
                loop.Span,
                loop.Initializer is null ? null : (PowerShellBoundMutationExpression)OptimizeExpression(loop.Initializer),
                loop.Condition is null ? null : OptimizeExpression(loop.Condition),
                loop.Iterator is null ? null : (PowerShellBoundMutationExpression)OptimizeExpression(loop.Iterator),
                OptimizeBlock(loop.Body)),
            PowerShellBoundForEachStatement loop => new PowerShellBoundForEachStatement(
                loop.Span, loop.Variable, loop.ElementType, OptimizeExpression(loop.Collection), loop.ScalarString, OptimizeBlock(loop.Body)),
            PowerShellBoundThrowStatement thrown => new PowerShellBoundThrowStatement(
                thrown.Span, thrown.Expression is null ? null : OptimizeExpression(thrown.Expression)),
            PowerShellBoundTryStatement attempted => new PowerShellBoundTryStatement(
                attempted.Span,
                OptimizeBlock(attempted.Body),
                attempted.Catches.Select(clause => new PowerShellBoundCatchClause(clause.ExceptionTypes.ToArray(), OptimizeBlock(clause.Body))).ToArray(),
                attempted.FinallyBlock is null ? null : OptimizeBlock(attempted.FinallyBlock)),
            PowerShellBoundStreamWriteStatement stream => new PowerShellBoundStreamWriteStatement(
                stream.Span, stream.Kind, stream.Provider, OptimizeExpression(stream.Message)),
            PowerShellBoundIndexAssignmentStatement index => new PowerShellBoundIndexAssignmentStatement(
                index.Span, OptimizeExpression(index.Target), OptimizeExpression(index.Index), OptimizeExpression(index.Value), index.Kind, index.UsePowerShellRuntimeErrors),
            PowerShellBoundClrMemberAssignmentStatement member => new PowerShellBoundClrMemberAssignmentStatement(
                member.Span, OptimizeExpression(member.Receiver), member.DeclaringType, member.MemberName, member.ReceiverBehavior, OptimizeExpression(member.Value)),
            _ => statement
        };

    private PowerShellBoundExpression OptimizeExpression(PowerShellBoundExpression expression)
    {
        if (expression is PowerShellBoundBinaryExpression binary)
        {
            var left = OptimizeExpression(binary.Left);
            var right = OptimizeExpression(binary.Right);
            if (left is PowerShellBoundLiteralExpression leftLiteral && right is PowerShellBoundLiteralExpression rightLiteral &&
                TryFold(binary.Operation, leftLiteral.Value, rightLiteral.Value, binary.Type.ClrType, out var value))
            {
                _constantExpressionsFolded++;
                return new PowerShellBoundLiteralExpression(binary.Span, value, binary.Type, PowerShellValueState.Known);
            }
            return new PowerShellBoundBinaryExpression(binary.Span, binary.Operation, left, right, binary.Type);
        }
        if (expression is PowerShellBoundUnaryExpression unary)
        {
            var operand = OptimizeExpression(unary.Operand);
            if (operand is PowerShellBoundLiteralExpression literal && TryFold(unary.Operation, literal.Value, unary.Type.ClrType, out var value))
            {
                _constantExpressionsFolded++;
                return new PowerShellBoundLiteralExpression(unary.Span, value, unary.Type, PowerShellValueState.Known);
            }
            return new PowerShellBoundUnaryExpression(unary.Span, unary.Operation, operand, unary.Type);
        }
        if (expression is PowerShellBoundConversionExpression conversion)
        {
            var operand = OptimizeExpression(conversion.Operand);
            if (!conversion.UsePowerShellLanguageRuntime && !conversion.UsePowerShellTruthiness &&
                operand.Type.ClrType == conversion.Type.ClrType)
            {
                _identityConversionsRemoved++;
                return operand;
            }
            if (conversion.UsePowerShellLanguageRuntime) _runtimeConversionSitesSpecialized++;
            return new PowerShellBoundConversionExpression(conversion.Span, conversion.Type, operand, conversion.UsePowerShellLanguageRuntime, conversion.UsePowerShellTruthiness);
        }
        if (expression is PowerShellBoundInvocationExpression invocation)
            return new PowerShellBoundInvocationExpression(invocation.Span, invocation.Target,
                invocation.Arguments.Select(OptimizeExpression).ToArray(), invocation.Type,
                invocation.AuthoredEvaluationOrder.ToArray(), invocation.BoundParameterNames.ToArray());
        if (expression is PowerShellBoundMutationExpression mutation)
            return new PowerShellBoundMutationExpression(mutation.Span, mutation.Target, mutation.TargetClrType, mutation.Operation,
                mutation.Value is null ? null : OptimizeExpression(mutation.Value), mutation.Type, mutation.NormalizeNullString, mutation.CheckedIntegral);
        if (expression is PowerShellBoundArrayExpression array)
            return new PowerShellBoundArrayExpression(array.Span, array.Type.ClrType, array.Kind, array.Elements.Select(OptimizeExpression).ToArray());
        return expression;
    }

    private static bool TryGetBoolean(PowerShellBoundExpression expression, out bool value)
    {
        if (expression is PowerShellBoundLiteralExpression { Value: bool literal })
        {
            value = literal;
            return true;
        }
        value = false;
        return false;
    }

    private static bool TryFold(PowerShellBoundUnaryOperator operation, object? operand, Type type, out object? value)
    {
        value = null;
        if (operation == PowerShellBoundUnaryOperator.Identity && operand is not null && type.IsInstanceOfType(operand)) { value = operand; return true; }
        if (operation == PowerShellBoundUnaryOperator.LogicalNot && operand is bool boolean) { value = !boolean; return true; }
        if (operand is int integer && type == typeof(int))
        {
            value = operation switch { PowerShellBoundUnaryOperator.BitwiseNot => ~integer, _ => null };
            return value is not null;
        }
        if (operand is long longInteger && type == typeof(long))
        {
            value = operation switch { PowerShellBoundUnaryOperator.BitwiseNot => ~longInteger, _ => null };
            return value is not null;
        }
        return false;
    }

    private static bool TryFold(PowerShellBoundBinaryOperator operation, object? left, object? right, Type type, out object? value)
    {
        value = null;
        try
        {
            if (left is bool leftBoolean && right is bool rightBoolean)
            {
                value = operation switch
                {
                    PowerShellBoundBinaryOperator.LogicalAnd => leftBoolean && rightBoolean,
                    PowerShellBoundBinaryOperator.LogicalOr => leftBoolean || rightBoolean,
                    PowerShellBoundBinaryOperator.Equal => leftBoolean == rightBoolean,
                    PowerShellBoundBinaryOperator.NotEqual => leftBoolean != rightBoolean,
                    _ => null
                };
                return value is not null;
            }
            if (left is int leftInt && right is int rightInt)
                return TryFoldInt32(operation, leftInt, rightInt, out value);
            if (left is long leftLong && right is long rightLong)
                return TryFoldInt64(operation, leftLong, rightLong, out value);
            if (left is double leftDouble && right is double rightDouble)
                return TryFoldDouble(operation, leftDouble, rightDouble, out value);
            if (left is string leftString && right is string rightString && type == typeof(bool))
            {
                value = operation switch
                {
                    PowerShellBoundBinaryOperator.Equal => leftString == rightString,
                    PowerShellBoundBinaryOperator.NotEqual => leftString != rightString,
                    PowerShellBoundBinaryOperator.EqualIgnoreCase => string.Equals(leftString, rightString, StringComparison.InvariantCultureIgnoreCase),
                    PowerShellBoundBinaryOperator.NotEqualIgnoreCase => !string.Equals(leftString, rightString, StringComparison.InvariantCultureIgnoreCase),
                    PowerShellBoundBinaryOperator.EqualCaseSensitive => string.Equals(leftString, rightString, StringComparison.InvariantCulture),
                    PowerShellBoundBinaryOperator.NotEqualCaseSensitive => !string.Equals(leftString, rightString, StringComparison.InvariantCulture),
                    _ => null
                };
                return value is not null;
            }
        }
        catch (ArithmeticException)
        {
            return false;
        }
        return false;
    }

    private static bool TryFoldInt32(PowerShellBoundBinaryOperator operation, int left, int right, out object? value)
    {
        value = operation switch
        {
            PowerShellBoundBinaryOperator.Equal => left == right,
            PowerShellBoundBinaryOperator.NotEqual => left != right,
            PowerShellBoundBinaryOperator.LessThan => left < right,
            PowerShellBoundBinaryOperator.LessThanOrEqual => left <= right,
            PowerShellBoundBinaryOperator.GreaterThan => left > right,
            PowerShellBoundBinaryOperator.GreaterThanOrEqual => left >= right,
            PowerShellBoundBinaryOperator.BitwiseAnd => left & right,
            PowerShellBoundBinaryOperator.BitwiseOr => left | right,
            PowerShellBoundBinaryOperator.BitwiseExclusiveOr => left ^ right,
            PowerShellBoundBinaryOperator.ShiftLeft => left << right,
            PowerShellBoundBinaryOperator.ShiftRight => left >> right,
            _ => null
        };
        return value is not null;
    }

    private static bool TryFoldInt64(PowerShellBoundBinaryOperator operation, long left, long right, out object? value)
    {
        value = operation switch
        {
            PowerShellBoundBinaryOperator.Equal => left == right,
            PowerShellBoundBinaryOperator.NotEqual => left != right,
            PowerShellBoundBinaryOperator.LessThan => left < right,
            PowerShellBoundBinaryOperator.LessThanOrEqual => left <= right,
            PowerShellBoundBinaryOperator.GreaterThan => left > right,
            PowerShellBoundBinaryOperator.GreaterThanOrEqual => left >= right,
            PowerShellBoundBinaryOperator.BitwiseAnd => left & right,
            PowerShellBoundBinaryOperator.BitwiseOr => left | right,
            PowerShellBoundBinaryOperator.BitwiseExclusiveOr => left ^ right,
            PowerShellBoundBinaryOperator.ShiftLeft => left << (int)right,
            PowerShellBoundBinaryOperator.ShiftRight => left >> (int)right,
            _ => null
        };
        return value is not null;
    }

    private static bool TryFoldDouble(PowerShellBoundBinaryOperator operation, double left, double right, out object? value)
    {
        value = operation switch
        {
            PowerShellBoundBinaryOperator.Add => left + right,
            PowerShellBoundBinaryOperator.Subtract => left - right,
            PowerShellBoundBinaryOperator.Multiply => left * right,
            PowerShellBoundBinaryOperator.Divide => left / right,
            PowerShellBoundBinaryOperator.Remainder => left % right,
            PowerShellBoundBinaryOperator.Equal => left == right,
            PowerShellBoundBinaryOperator.NotEqual => left != right,
            PowerShellBoundBinaryOperator.LessThan => left < right,
            PowerShellBoundBinaryOperator.LessThanOrEqual => left <= right,
            PowerShellBoundBinaryOperator.GreaterThan => left > right,
            PowerShellBoundBinaryOperator.GreaterThanOrEqual => left >= right,
            _ => null
        };
        return value is not null;
    }
}

internal sealed class PowerShellBoundOptimizationResult
{
    internal PowerShellBoundOptimizationResult(PowerShellBoundProgram program, PowerShellBoundOptimizationEvidence evidence)
    {
        Program = program;
        Evidence = evidence;
    }

    internal PowerShellBoundProgram Program { get; }
    internal PowerShellBoundOptimizationEvidence Evidence { get; }
}

internal sealed class PowerShellBoundOptimizationEvidence
{
    internal PowerShellBoundOptimizationEvidence(
        int constantExpressionsFolded,
        int deadBranchesRemoved,
        int identityConversionsRemoved,
        int pipelineStagesFused,
        int commandRegionStatementsCoalesced,
        int specializedCollectionLoops,
        int runtimeConversionSitesSpecialized,
        int sourceMappedStatements)
    {
        ConstantExpressionsFolded = constantExpressionsFolded;
        DeadBranchesRemoved = deadBranchesRemoved;
        IdentityConversionsRemoved = identityConversionsRemoved;
        PipelineStagesFused = pipelineStagesFused;
        CommandRegionStatementsCoalesced = commandRegionStatementsCoalesced;
        SpecializedCollectionLoops = specializedCollectionLoops;
        RuntimeConversionSitesSpecialized = runtimeConversionSitesSpecialized;
        SourceMappedStatements = sourceMappedStatements;
    }

    internal int ConstantExpressionsFolded { get; }
    internal int DeadBranchesRemoved { get; }
    internal int IdentityConversionsRemoved { get; }
    internal int PipelineStagesFused { get; }
    internal int CommandRegionStatementsCoalesced { get; }
    internal int SpecializedCollectionLoops { get; }
    internal int RuntimeConversionSitesSpecialized { get; }
    internal int SourceMappedStatements { get; }
    internal bool Changed => ConstantExpressionsFolded > 0 || DeadBranchesRemoved > 0 || IdentityConversionsRemoved > 0;

    internal PowerShellCompilationOptimizationEvidence ToPublicModel()
        => new()
        {
            ConstantExpressionsFolded = ConstantExpressionsFolded,
            DeadBranchesRemoved = DeadBranchesRemoved,
            IdentityConversionsRemoved = IdentityConversionsRemoved,
            PipelineStagesFused = PipelineStagesFused,
            CommandRegionStatementsCoalesced = CommandRegionStatementsCoalesced,
            SpecializedCollectionLoops = SpecializedCollectionLoops,
            RuntimeConversionSitesSpecialized = RuntimeConversionSitesSpecialized,
            SourceMappedStatements = SourceMappedStatements
        };
}
