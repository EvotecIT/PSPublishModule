namespace PowerForge;

/// <summary>
/// Selects typed CLR operations from analyzed bound nodes. It does not render target-language source.
/// </summary>
internal sealed class PowerShellTypedLowerer
{
    internal PowerShellLoweredProgram Lower(
        PowerShellBoundProgram program,
        PowerShellCompilationCapability targetCapabilities = PowerShellCompilationCapability.None)
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
            var topLevelAssignments = function.Body.Statements.OfType<PowerShellBoundAssignmentStatement>()
                .Select(static assignment => assignment.Target.StableKey)
                .ToHashSet(StringComparer.Ordinal);
            var predeclared = EnumerateNestedAssignments(function.Body)
                .Where(localTypes.ContainsKey)
                .Where(key => !topLevelAssignments.Contains(key))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static key => key, StringComparer.Ordinal)
                .ToArray();
            foreach (var key in predeclared)
            {
                var local = function.Locals.Single(candidate => candidate.Symbol.StableKey == key);
                statements.Add(new PowerShellLoweredLocalDeclarationStatement(local.Symbol.Declaration, local.Symbol, localTypes[key]));
                declared.Add(key);
            }
            foreach (var statement in function.Body.Statements)
                statements.Add(LowerStatement(statement, bySymbol, symbolTypes, localTypes, declared));

            functions.Add(new PowerShellLoweredFunction(
                function.Symbol,
                PowerShellCSharpMethodEmitter.SanitizeIdentifier(function.Symbol.Name),
                function.ReturnType.ClrType,
                function.Parameters.Select(static parameter => new PowerShellLoweredParameter(parameter.Symbol, parameter.Type.ClrType, parameter.Contract)).ToArray(),
                function.Locals.Select(static local => new PowerShellLoweredLocal(local.Symbol, local.Type.ClrType)).ToArray(),
                function.Help,
                statements.ToArray(),
                function.Body.Span));
        }

        return new PowerShellLoweredProgram(
            functions.OrderBy(static function => function.Symbol.StableKey, StringComparer.Ordinal).ToArray(),
            diagnostics.OrderBy(static diagnostic => diagnostic.Span.DocumentId, StringComparer.Ordinal)
                .ThenBy(static diagnostic => diagnostic.Span.StartOffset)
                .ThenBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .ToArray(),
            targetCapabilities);
    }

    private static IEnumerable<string> EnumerateNestedAssignments(PowerShellBoundBlock block)
    {
        foreach (var statement in block.Statements)
        {
            if (statement is PowerShellBoundIfStatement conditional)
            {
                foreach (var clause in conditional.Clauses)
                {
                    foreach (var assignment in EnumerateAssignments(clause.Body)) yield return assignment;
                }
                if (conditional.ElseBlock is not null)
                {
                    foreach (var assignment in EnumerateAssignments(conditional.ElseBlock)) yield return assignment;
                }
            }
            else if (statement is PowerShellBoundWhileStatement loop)
            {
                foreach (var assignment in EnumerateAssignments(loop.Body)) yield return assignment;
            }
            else if (statement is PowerShellBoundForStatement forLoop)
            {
                foreach (var assignment in EnumerateAssignments(forLoop.Body)) yield return assignment;
            }
            else if (statement is PowerShellBoundForEachStatement forEachLoop)
            {
                foreach (var assignment in EnumerateAssignments(forEachLoop.Body).Where(key => key != forEachLoop.Variable.StableKey)) yield return assignment;
            }
            else if (statement is PowerShellBoundSwitchStatement switchStatement)
            {
                foreach (var clause in switchStatement.Clauses)
                foreach (var assignment in EnumerateAssignments(clause.Body))
                    yield return assignment;
                if (switchStatement.DefaultBlock is not null)
                foreach (var assignment in EnumerateAssignments(switchStatement.DefaultBlock))
                    yield return assignment;
            }
            else if (statement is PowerShellBoundTryStatement tryStatement)
            {
                foreach (var assignment in EnumerateAssignments(tryStatement.Body)) yield return assignment;
                foreach (var clause in tryStatement.Catches)
                foreach (var assignment in EnumerateAssignments(clause.Body))
                    yield return assignment;
                if (tryStatement.FinallyBlock is not null)
                foreach (var assignment in EnumerateAssignments(tryStatement.FinallyBlock))
                    yield return assignment;
            }
        }
    }

    private static IEnumerable<string> EnumerateAssignments(PowerShellBoundBlock block)
    {
        foreach (var statement in block.Statements)
        {
            if (statement is PowerShellBoundAssignmentStatement assignment) yield return assignment.Target.StableKey;
            if (statement is PowerShellBoundIfStatement conditional)
            {
                foreach (var clause in conditional.Clauses)
                foreach (var nested in EnumerateAssignments(clause.Body))
                    yield return nested;
                if (conditional.ElseBlock is not null)
                foreach (var nested in EnumerateAssignments(conditional.ElseBlock))
                    yield return nested;
            }
            else if (statement is PowerShellBoundWhileStatement loop)
            {
                foreach (var nested in EnumerateAssignments(loop.Body)) yield return nested;
            }
            else if (statement is PowerShellBoundForStatement forLoop)
            {
                foreach (var nested in EnumerateAssignments(forLoop.Body)) yield return nested;
            }
            else if (statement is PowerShellBoundForEachStatement forEachLoop)
            {
                foreach (var nested in EnumerateAssignments(forEachLoop.Body)) yield return nested;
            }
            else if (statement is PowerShellBoundSwitchStatement switchStatement)
            {
                foreach (var clause in switchStatement.Clauses)
                foreach (var nested in EnumerateAssignments(clause.Body))
                    yield return nested;
                if (switchStatement.DefaultBlock is not null)
                foreach (var nested in EnumerateAssignments(switchStatement.DefaultBlock))
                    yield return nested;
            }
            else if (statement is PowerShellBoundTryStatement tryStatement)
            {
                foreach (var nested in EnumerateAssignments(tryStatement.Body)) yield return nested;
                foreach (var clause in tryStatement.Catches)
                foreach (var nested in EnumerateAssignments(clause.Body))
                    yield return nested;
                if (tryStatement.FinallyBlock is not null)
                foreach (var nested in EnumerateAssignments(tryStatement.FinallyBlock))
                    yield return nested;
            }
        }
    }

    private static PowerShellLoweredStatement LowerStatement(
        PowerShellBoundStatement statement,
        IReadOnlyDictionary<string, PowerShellBoundFunction> functions,
        IReadOnlyDictionary<string, Type> symbolTypes,
        IReadOnlyDictionary<string, Type> localTypes,
        ISet<string> declared)
        => statement switch
        {
            PowerShellBoundAssignmentStatement assignment => new PowerShellLoweredAssignmentStatement(
                assignment.Span,
                assignment.Target,
                symbolTypes[assignment.Target.StableKey],
                LowerExpression(assignment.Value, functions),
                localTypes.ContainsKey(assignment.Target.StableKey) && declared.Add(assignment.Target.StableKey),
                assignment.Operation,
                assignment.NormalizeNullString,
                assignment.CheckedIntegral),
            PowerShellBoundReturnStatement returned => new PowerShellLoweredReturnStatement(
                returned.Span,
                returned.Expression is null ? null : LowerExpression(returned.Expression, functions)),
            PowerShellBoundExpressionStatement expression => new PowerShellLoweredReturnStatement(
                expression.Span,
                LowerExpression(expression.Expression, functions)),
            PowerShellBoundIfStatement conditional => new PowerShellLoweredIfStatement(
                conditional.Span,
                conditional.Clauses.Select(clause => new PowerShellLoweredConditionalClause(
                    LowerExpression(clause.Condition, functions),
                    LowerStatements(clause.Body, functions, symbolTypes, localTypes, declared))).ToArray(),
                conditional.ElseBlock is null ? null : LowerStatements(conditional.ElseBlock, functions, symbolTypes, localTypes, declared)),
            PowerShellBoundWhileStatement loop => new PowerShellLoweredWhileStatement(
                loop.Span,
                LowerExpression(loop.Condition, functions),
                LowerStatements(loop.Body, functions, symbolTypes, localTypes, declared)),
            PowerShellBoundForStatement loop => LowerFor(loop, functions, symbolTypes, localTypes, declared),
            PowerShellBoundForEachStatement loop => LowerForEach(loop, functions, symbolTypes, localTypes, declared),
            PowerShellBoundSwitchStatement switchStatement => new PowerShellLoweredSwitchStatement(
                switchStatement.Span,
                LowerExpression(switchStatement.Value, functions),
                switchStatement.Clauses.Select(clause => new PowerShellLoweredSwitchClause(
                    LowerExpression(clause.Value, functions),
                    LowerStatements(clause.Body, functions, symbolTypes, localTypes, declared))).ToArray(),
                switchStatement.DefaultBlock is null ? null : LowerStatements(switchStatement.DefaultBlock, functions, symbolTypes, localTypes, declared),
                switchStatement.CaseSensitive),
            PowerShellBoundThrowStatement thrown => new PowerShellLoweredThrowStatement(
                thrown.Span,
                thrown.Expression is null ? null : LowerExpression(thrown.Expression, functions)),
            PowerShellBoundTryStatement tryStatement => new PowerShellLoweredTryStatement(
                tryStatement.Span,
                LowerStatements(tryStatement.Body, functions, symbolTypes, localTypes, declared),
                tryStatement.Catches.Select(clause => new PowerShellLoweredCatchClause(
                    clause.ExceptionTypes,
                    LowerStatements(clause.Body, functions, symbolTypes, localTypes, declared))).ToArray(),
                tryStatement.FinallyBlock is null ? null : LowerStatements(tryStatement.FinallyBlock, functions, symbolTypes, localTypes, declared)),
            PowerShellBoundBreakStatement => new PowerShellLoweredBreakStatement(statement.Span),
            PowerShellBoundContinueStatement => new PowerShellLoweredContinueStatement(statement.Span),
            _ => throw new InvalidOperationException($"Bound statement '{statement.GetType().Name}' reached typed lowering without an owner.")
        };

    private static PowerShellLoweredForStatement LowerFor(
        PowerShellBoundForStatement loop,
        IReadOnlyDictionary<string, PowerShellBoundFunction> functions,
        IReadOnlyDictionary<string, Type> symbolTypes,
        IReadOnlyDictionary<string, Type> localTypes,
        ISet<string> declared)
    {
        var declareInitializer = loop.Initializer is not null &&
                                 localTypes.ContainsKey(loop.Initializer.Target.StableKey) &&
                                 declared.Add(loop.Initializer.Target.StableKey);
        return new PowerShellLoweredForStatement(
            loop.Span,
            loop.Initializer is null ? null : (PowerShellLoweredMutationExpression)LowerExpression(loop.Initializer, functions),
            loop.Condition is null ? null : LowerExpression(loop.Condition, functions),
            loop.Iterator is null ? null : (PowerShellLoweredMutationExpression)LowerExpression(loop.Iterator, functions),
            LowerStatements(loop.Body, functions, symbolTypes, localTypes, declared),
            declareInitializer);
    }

    private static PowerShellLoweredForEachStatement LowerForEach(
        PowerShellBoundForEachStatement loop,
        IReadOnlyDictionary<string, PowerShellBoundFunction> functions,
        IReadOnlyDictionary<string, Type> symbolTypes,
        IReadOnlyDictionary<string, Type> localTypes,
        ISet<string> declared)
    {
        declared.Add(loop.Variable.StableKey);
        return new PowerShellLoweredForEachStatement(
            loop.Span,
            loop.Variable,
            loop.ElementType,
            LowerExpression(loop.Collection, functions),
            loop.ScalarString,
            LowerStatements(loop.Body, functions, symbolTypes, localTypes, declared));
    }

    private static PowerShellLoweredStatement[] LowerStatements(
        PowerShellBoundBlock block,
        IReadOnlyDictionary<string, PowerShellBoundFunction> functions,
        IReadOnlyDictionary<string, Type> symbolTypes,
        IReadOnlyDictionary<string, Type> localTypes,
        ISet<string> declared)
        => block.Statements.Select(statement => LowerStatement(statement, functions, symbolTypes, localTypes, declared)).ToArray();

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
            PowerShellBoundBinaryExpression binary => new PowerShellLoweredBinaryExpression(
                binary.Span,
                binary.Type.ClrType,
                binary.Operation,
                LowerExpression(binary.Left, functions),
                LowerExpression(binary.Right, functions)),
            PowerShellBoundUnaryExpression unary => new PowerShellLoweredUnaryExpression(
                unary.Span,
                unary.Type.ClrType,
                unary.Operation,
                LowerExpression(unary.Operand, functions)),
            PowerShellBoundMutationExpression mutation => new PowerShellLoweredMutationExpression(
                mutation.Span,
                mutation.Type.ClrType,
                mutation.Target,
                mutation.TargetClrType,
                mutation.Operation,
                mutation.Value is null ? null : LowerExpression(mutation.Value, functions),
                mutation.NormalizeNullString,
                mutation.CheckedIntegral),
            PowerShellBoundArrayExpression array => new PowerShellLoweredArrayExpression(
                array.Span,
                array.Type.ClrType,
                array.Kind,
                array.Elements.Select(element => LowerExpression(element, functions)).ToArray()),
            PowerShellBoundClrMemberExpression member => new PowerShellLoweredClrMemberExpression(
                member.Span,
                member.Type.ClrType,
                member.DeclaringType,
                member.MemberName,
                member.IsStatic,
                member.Receiver is null ? null : LowerExpression(member.Receiver, functions),
                member.ReceiverBehavior),
            PowerShellBoundClrInvocationExpression invocation => new PowerShellLoweredClrInvocationExpression(
                invocation.Span,
                invocation.Type.ClrType,
                invocation.DeclaringType,
                invocation.MemberName,
                invocation.InvocationKind,
                invocation.Receiver is null ? null : LowerExpression(invocation.Receiver, functions),
                invocation.ReceiverBehavior,
                invocation.Arguments.Select(argument => LowerExpression(argument, functions)).ToArray(),
                invocation.ParameterTypes),
            PowerShellBoundInvocationExpression invocation when functions.TryGetValue(invocation.Target.StableKey, out var target) =>
                new PowerShellLoweredInvocationExpression(
                    invocation.Span,
                    target.ReturnType.ClrType,
                    invocation.Target,
                    invocation.Arguments.Select(argument => LowerExpression(argument, functions)).ToArray()),
            _ => throw new InvalidOperationException($"Bound expression '{expression.GetType().Name}' reached typed lowering without an owner.")
        };
}
