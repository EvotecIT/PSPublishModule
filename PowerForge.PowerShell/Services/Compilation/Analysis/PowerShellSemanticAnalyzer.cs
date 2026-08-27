namespace PowerForge;

internal interface IPowerShellSemanticPass
{
    string Id { get; }
    PowerShellBoundProgram Run(PowerShellBoundProgram program);
}

/// <summary>
/// Runs semantic passes in canonical identity order and rejects duplicate owners.
/// </summary>
internal sealed class PowerShellSemanticAnalyzer
{
    private readonly IPowerShellSemanticPass[] _passes;

    internal PowerShellSemanticAnalyzer(IEnumerable<IPowerShellSemanticPass>? passes = null)
    {
        _passes = (passes ?? CreateDefaultPasses()).OrderBy(static pass => pass.Id, StringComparer.Ordinal).ToArray();
        var duplicate = _passes.GroupBy(static pass => pass.Id, StringComparer.Ordinal).FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Semantic pass '{duplicate.Key}' is registered more than once.");
    }

    internal PowerShellBoundProgram Analyze(PowerShellBoundProgram program)
    {
        var current = program ?? throw new ArgumentNullException(nameof(program));
        foreach (var pass in _passes) current = pass.Run(current);
        return current;
    }

    private static IEnumerable<IPowerShellSemanticPass> CreateDefaultPasses()
    {
        yield return new DefiniteAssignmentPass();
        yield return new LocalTypePass();
        yield return new CallGraphPass();
        yield return new ReturnTypePass();
        yield return new EffectPass();
        yield return new CapabilityPass();
        yield return new FallbackPass();
    }

    private sealed class DefiniteAssignmentPass : IPowerShellSemanticPass
    {
        public string Id => "10-definite-assignment";

        public PowerShellBoundProgram Run(PowerShellBoundProgram program)
        {
            var diagnostics = new List<PowerShellSemanticDiagnostic>(program.Diagnostics);
            foreach (var function in program.Functions)
            {
                var assigned = function.Parameters.Select(static parameter => parameter.Symbol.StableKey).ToHashSet(StringComparer.Ordinal);
                var locals = function.Locals.Select(static local => local.Symbol.StableKey).ToHashSet(StringComparer.Ordinal);
                AnalyzeDefiniteAssignment(function.Body, assigned, locals, diagnostics);
            }
            return program.WithDiagnostics(OrderDiagnostics(diagnostics));
        }

        private static void AnalyzeDefiniteAssignment(
            PowerShellBoundBlock block,
            ISet<string> assigned,
            ISet<string> locals,
            ICollection<PowerShellSemanticDiagnostic> diagnostics)
        {
            foreach (var statement in block.Statements)
            {
                if (statement is PowerShellBoundIfStatement conditional)
                {
                    foreach (var clause in conditional.Clauses) ReportReads(clause.Condition, assigned, locals, diagnostics);
                    var branchStates = conditional.Clauses.Select(clause =>
                    {
                        var state = assigned.ToHashSet(StringComparer.Ordinal);
                        AnalyzeDefiniteAssignment(clause.Body, state, locals, diagnostics);
                        return state;
                    }).ToList();
                    if (conditional.ElseBlock is null)
                        branchStates.Add(assigned.ToHashSet(StringComparer.Ordinal));
                    else
                    {
                        var elseState = assigned.ToHashSet(StringComparer.Ordinal);
                        AnalyzeDefiniteAssignment(conditional.ElseBlock, elseState, locals, diagnostics);
                        branchStates.Add(elseState);
                    }
                    if (branchStates.Count > 0)
                    {
                        var definitelyAssigned = branchStates.Aggregate((left, right) => left.Intersect(right, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal));
                        assigned.Clear();
                        assigned.UnionWith(definitelyAssigned);
                    }
                    continue;
                }
                if (statement is PowerShellBoundWhileStatement loop)
                {
                    ReportReads(loop.Condition, assigned, locals, diagnostics);
                    var loopState = assigned.ToHashSet(StringComparer.Ordinal);
                    AnalyzeDefiniteAssignment(loop.Body, loopState, locals, diagnostics);
                    continue;
                }
                if (statement is PowerShellBoundForStatement forLoop)
                {
                    if (forLoop.Initializer is not null)
                    {
                        ReportReads(forLoop.Initializer, assigned, locals, diagnostics);
                        if (forLoop.Initializer.Operation == PowerShellBoundMutationOperator.Assign)
                            assigned.Add(forLoop.Initializer.Target.StableKey);
                    }
                    if (forLoop.Condition is not null) ReportReads(forLoop.Condition, assigned, locals, diagnostics);
                    var loopState = assigned.ToHashSet(StringComparer.Ordinal);
                    AnalyzeDefiniteAssignment(forLoop.Body, loopState, locals, diagnostics);
                    if (forLoop.Iterator is not null) ReportReads(forLoop.Iterator, loopState, locals, diagnostics);
                    continue;
                }
                if (statement is PowerShellBoundForEachStatement forEachLoop)
                {
                    ReportReads(forEachLoop.Collection, assigned, locals, diagnostics);
                    var loopState = assigned.ToHashSet(StringComparer.Ordinal);
                    loopState.Add(forEachLoop.Variable.StableKey);
                    AnalyzeDefiniteAssignment(forEachLoop.Body, loopState, locals, diagnostics);
                    continue;
                }
                if (statement is PowerShellBoundSwitchStatement switchStatement)
                {
                    ReportReads(switchStatement.Value, assigned, locals, diagnostics);
                    foreach (var clause in switchStatement.Clauses) ReportReads(clause.Value, assigned, locals, diagnostics);
                    var branchStates = switchStatement.Clauses.Select(clause =>
                    {
                        var state = assigned.ToHashSet(StringComparer.Ordinal);
                        AnalyzeDefiniteAssignment(clause.Body, state, locals, diagnostics);
                        return state;
                    }).ToList();
                    if (switchStatement.DefaultBlock is null)
                        branchStates.Add(assigned.ToHashSet(StringComparer.Ordinal));
                    else
                    {
                        var defaultState = assigned.ToHashSet(StringComparer.Ordinal);
                        AnalyzeDefiniteAssignment(switchStatement.DefaultBlock, defaultState, locals, diagnostics);
                        branchStates.Add(defaultState);
                    }
                    if (branchStates.Count > 0)
                    {
                        var definitelyAssigned = branchStates.Aggregate((left, right) => left.Intersect(right, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal));
                        assigned.Clear();
                        assigned.UnionWith(definitelyAssigned);
                    }
                    continue;
                }
                if (statement is PowerShellBoundTryStatement tryStatement)
                {
                    var branchStates = new List<HashSet<string>>();
                    var tryState = assigned.ToHashSet(StringComparer.Ordinal);
                    AnalyzeDefiniteAssignment(tryStatement.Body, tryState, locals, diagnostics);
                    branchStates.Add(tryState);
                    foreach (var clause in tryStatement.Catches)
                    {
                        var catchState = assigned.ToHashSet(StringComparer.Ordinal);
                        AnalyzeDefiniteAssignment(clause.Body, catchState, locals, diagnostics);
                        branchStates.Add(catchState);
                    }
                    var definitelyAssigned = branchStates.Aggregate((left, right) => left.Intersect(right, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal));
                    if (tryStatement.FinallyBlock is not null)
                        AnalyzeDefiniteAssignment(tryStatement.FinallyBlock, definitelyAssigned, locals, diagnostics);
                    assigned.Clear();
                    assigned.UnionWith(definitelyAssigned);
                    continue;
                }
                var expression = GetExpression(statement);
                if (expression is not null) ReportReads(expression, assigned, locals, diagnostics);
                if (statement is PowerShellBoundAssignmentStatement assignment)
                {
                    if (assignment.Operation != PowerShellBoundMutationOperator.Assign &&
                        locals.Contains(assignment.Target.StableKey) &&
                        !assigned.Contains(assignment.Target.StableKey))
                    {
                        diagnostics.Add(new PowerShellSemanticDiagnostic(
                            "PSD1001",
                            $"Local variable '${assignment.Target.Name}' is read before its first definite assignment.",
                            assignment.Span));
                    }
                    assigned.Add(assignment.Target.StableKey);
                }
            }
        }

        private static void ReportReads(
            PowerShellBoundExpression expression,
            ISet<string> assigned,
            ISet<string> locals,
            ICollection<PowerShellSemanticDiagnostic> diagnostics)
        {
            foreach (var read in EnumerateVariableReads(expression).Where(read => locals.Contains(read.Symbol.StableKey)))
            {
                if (assigned.Contains(read.Symbol.StableKey)) continue;
                diagnostics.Add(new PowerShellSemanticDiagnostic(
                    "PSD1001",
                    $"Local variable '${read.Symbol.Name}' is read before its first definite assignment.",
                    read.Span));
            }
        }
    }

    private sealed class LocalTypePass : IPowerShellSemanticPass
    {
        public string Id => "20-local-type-propagation";

        public PowerShellBoundProgram Run(PowerShellBoundProgram program)
        {
            var diagnostics = new List<PowerShellSemanticDiagnostic>(program.Diagnostics);
            foreach (var function in program.Functions)
            {
                foreach (var local in function.Locals.Where(static local => local.Type.Provenance == PowerShellTypeFactProvenance.Unknown))
                {
                    diagnostics.Add(new PowerShellSemanticDiagnostic(
                        "PST2001",
                        $"Local variable '${local.Symbol.Name}' does not have one stable CLR representation.",
                        local.Symbol.Declaration));
                }
            }
            return program.WithDiagnostics(OrderDiagnostics(diagnostics));
        }
    }

    private sealed class CallGraphPass : IPowerShellSemanticPass
    {
        public string Id => "25-call-graph";

        public PowerShellBoundProgram Run(PowerShellBoundProgram program)
        {
            var edges = program.Functions.SelectMany(function => EnumerateStatements(function.Body)
                    .SelectMany(EnumerateDirectExpressions)
                    .SelectMany(EnumerateInvocations)
                    .Select(invocation => new PowerShellCallGraphEdge(function.Symbol, invocation.Target, invocation.Span)))
                .OrderBy(static edge => edge.StableKey, StringComparer.Ordinal)
                .ToArray();
            return program.WithCallGraph(edges);
        }
    }

    private sealed class ReturnTypePass : IPowerShellSemanticPass
    {
        public string Id => "30-return-type-fixed-point";

        public PowerShellBoundProgram Run(PowerShellBoundProgram program)
        {
            var functions = program.Functions.ToDictionary(static function => function.Symbol.StableKey, StringComparer.Ordinal);
            var maximumIterations = Math.Max(1, functions.Count + 1);
            for (var iteration = 0; iteration < maximumIterations; iteration++)
            {
                var changed = false;
                foreach (var key in functions.Keys.OrderBy(static key => key, StringComparer.Ordinal).ToArray())
                {
                    var current = functions[key];
                    var next = AnalyzeReturnType(current, functions);
                    if (next.ReturnType.ClrType != current.ReturnType.ClrType || next.ReturnType.Provenance != current.ReturnType.Provenance)
                    {
                        functions[key] = next;
                        changed = true;
                    }
                }
                if (!changed) break;
            }
            return program.WithFunctions(functions.Values.OrderBy(static function => function.Symbol.StableKey, StringComparer.Ordinal).ToArray());
        }

        private static PowerShellBoundFunction AnalyzeReturnType(
            PowerShellBoundFunction function,
            IReadOnlyDictionary<string, PowerShellBoundFunction> functions)
        {
            var expressions = EnumerateStatements(function.Body).Select(GetSuccessOutputExpression).Where(static expression => expression is not null).Cast<PowerShellBoundExpression>().ToArray();
            if (expressions.Length == 0)
                return function.WithAnalysis(returnType: new PowerShellTypeFact(typeof(void), PowerShellTypeFactProvenance.Inferred, "The function has no success output."));
            var facts = expressions.Select(expression => ResolveType(expression, functions)).ToArray();
            var known = facts.Where(static fact => fact.Provenance != PowerShellTypeFactProvenance.Unknown).ToArray();
            if (known.Length == 0) return function;
            var first = known[0];
            if (known.All(fact => fact.ClrType == first.ClrType))
                return function.WithAnalysis(returnType: new PowerShellTypeFact(first.ClrType, PowerShellTypeFactProvenance.Inferred, "All reachable success outputs have the same CLR type after call-graph propagation."));
            return function.WithAnalysis(
                returnType: PowerShellTypeFact.Unknown,
                disposition: new PowerShellExecutionDisposition(PowerShellExecutionDispositionKind.Fallback, "type.return.heterogeneous", "Reachable success outputs do not share one CLR type."));
        }
    }

    private sealed class EffectPass : IPowerShellSemanticPass
    {
        public string Id => "40-effects-fixed-point";

        public PowerShellBoundProgram Run(PowerShellBoundProgram program)
            => Propagate(program, static function => function.Effects, static (function, value) => function.WithAnalysis(effects: value));
    }

    private sealed class CapabilityPass : IPowerShellSemanticPass
    {
        public string Id => "50-capabilities-fixed-point";

        public PowerShellBoundProgram Run(PowerShellBoundProgram program)
        {
            var functions = program.Functions.ToDictionary(static function => function.Symbol.StableKey, StringComparer.Ordinal);
            RunFixedPoint(functions, (function, lookup) =>
            {
                var value = function.Body.Statements.Aggregate(PowerShellRequiredCapability.None, static (current, statement) => current | statement.Capabilities);
                foreach (var callee in GetCallees(function, lookup)) value |= callee.Capabilities;
                return function.WithAnalysis(capabilities: value);
            }, static (left, right) => left.Capabilities == right.Capabilities);
            return program.WithFunctions(functions.Values.OrderBy(static function => function.Symbol.StableKey, StringComparer.Ordinal).ToArray());
        }
    }

    private sealed class FallbackPass : IPowerShellSemanticPass
    {
        public string Id => "60-fallback-fixed-point";

        public PowerShellBoundProgram Run(PowerShellBoundProgram program)
        {
            var functions = program.Functions.ToDictionary(static function => function.Symbol.StableKey, StringComparer.Ordinal);
            RunFixedPoint(functions, (function, lookup) =>
            {
                if (function.Disposition.Kind != PowerShellExecutionDispositionKind.Typed) return function;
                if (function.ReturnType.Provenance == PowerShellTypeFactProvenance.Unknown)
                    return function.WithAnalysis(disposition: new PowerShellExecutionDisposition(PowerShellExecutionDispositionKind.Fallback, "type.return.unknown", "The function return type is not statically known."));
                var blocked = GetCallees(function, lookup).FirstOrDefault(static callee => callee.Disposition.Kind != PowerShellExecutionDispositionKind.Typed);
                return blocked is null
                    ? function
                    : function.WithAnalysis(disposition: new PowerShellExecutionDisposition(PowerShellExecutionDispositionKind.Fallback, "call.fallback", $"Local function '{blocked.Symbol.Name}' requires fallback."));
            }, static (left, right) => left.Disposition.Kind == right.Disposition.Kind && left.Disposition.ReasonCode == right.Disposition.ReasonCode);
            return program.WithFunctions(functions.Values.OrderBy(static function => function.Symbol.StableKey, StringComparer.Ordinal).ToArray());
        }
    }

    private static PowerShellBoundProgram Propagate(
        PowerShellBoundProgram program,
        Func<PowerShellBoundFunction, PowerShellSemanticEffect> selector,
        Func<PowerShellBoundFunction, PowerShellSemanticEffect, PowerShellBoundFunction> update)
    {
        var functions = program.Functions.ToDictionary(static function => function.Symbol.StableKey, StringComparer.Ordinal);
        RunFixedPoint(functions, (function, lookup) =>
        {
            var value = function.Body.Statements.Aggregate(PowerShellSemanticEffect.None, static (current, statement) => current | statement.Effects);
            foreach (var callee in GetCallees(function, lookup)) value |= selector(callee);
            return update(function, value);
        }, (left, right) => selector(left) == selector(right));
        return program.WithFunctions(functions.Values.OrderBy(static function => function.Symbol.StableKey, StringComparer.Ordinal).ToArray());
    }

    private static void RunFixedPoint(
        IDictionary<string, PowerShellBoundFunction> functions,
        Func<PowerShellBoundFunction, IReadOnlyDictionary<string, PowerShellBoundFunction>, PowerShellBoundFunction> update,
        Func<PowerShellBoundFunction, PowerShellBoundFunction, bool> equivalent)
    {
        var maximumIterations = Math.Max(1, functions.Count + 1);
        for (var iteration = 0; iteration < maximumIterations; iteration++)
        {
            var changed = false;
            foreach (var key in functions.Keys.OrderBy(static key => key, StringComparer.Ordinal).ToArray())
            {
                var current = functions[key];
                var next = update(current, (IReadOnlyDictionary<string, PowerShellBoundFunction>)functions);
                if (equivalent(current, next)) continue;
                functions[key] = next;
                changed = true;
            }
            if (!changed) break;
        }
    }

    private static IEnumerable<PowerShellBoundFunction> GetCallees(
        PowerShellBoundFunction function,
        IReadOnlyDictionary<string, PowerShellBoundFunction> functions)
        => EnumerateStatements(function.Body).SelectMany(EnumerateDirectExpressions)
            .SelectMany(EnumerateInvocations)
            .Select(invocation => functions.TryGetValue(invocation.Target.StableKey, out var callee) ? callee : null)
            .Where(static callee => callee is not null)
            .Cast<PowerShellBoundFunction>();

    private static PowerShellTypeFact ResolveType(
        PowerShellBoundExpression expression,
        IReadOnlyDictionary<string, PowerShellBoundFunction> functions)
        => expression switch
        {
            PowerShellBoundInvocationExpression invocation when functions.TryGetValue(invocation.Target.StableKey, out var target) => target.ReturnType,
            _ => expression.Type
        };

    private static PowerShellBoundExpression? GetExpression(PowerShellBoundStatement statement)
        => statement switch
        {
            PowerShellBoundAssignmentStatement assignment => assignment.Value,
            PowerShellBoundReturnStatement returned => returned.Expression,
            PowerShellBoundExpressionStatement expression => expression.Expression,
            _ => null
        };

    private static PowerShellBoundExpression? GetSuccessOutputExpression(PowerShellBoundStatement statement)
        => statement switch
        {
            PowerShellBoundReturnStatement returned => returned.Expression,
            PowerShellBoundExpressionStatement expression => expression.Expression,
            _ => null
        };

    private static IEnumerable<PowerShellBoundStatement> EnumerateStatements(PowerShellBoundBlock block)
    {
        foreach (var statement in block.Statements)
        {
            yield return statement;
            if (statement is PowerShellBoundIfStatement conditional)
            {
                foreach (var clause in conditional.Clauses)
                foreach (var nested in EnumerateStatements(clause.Body))
                    yield return nested;
                if (conditional.ElseBlock is not null)
                foreach (var nested in EnumerateStatements(conditional.ElseBlock))
                    yield return nested;
            }
            else if (statement is PowerShellBoundWhileStatement loop)
            {
                foreach (var nested in EnumerateStatements(loop.Body)) yield return nested;
            }
            else if (statement is PowerShellBoundForStatement forLoop)
            {
                foreach (var nested in EnumerateStatements(forLoop.Body)) yield return nested;
            }
            else if (statement is PowerShellBoundForEachStatement forEachLoop)
            {
                foreach (var nested in EnumerateStatements(forEachLoop.Body)) yield return nested;
            }
            else if (statement is PowerShellBoundSwitchStatement switchStatement)
            {
                foreach (var clause in switchStatement.Clauses)
                foreach (var nested in EnumerateStatements(clause.Body))
                    yield return nested;
                if (switchStatement.DefaultBlock is not null)
                foreach (var nested in EnumerateStatements(switchStatement.DefaultBlock))
                    yield return nested;
            }
            else if (statement is PowerShellBoundTryStatement tryStatement)
            {
                foreach (var nested in EnumerateStatements(tryStatement.Body)) yield return nested;
                foreach (var clause in tryStatement.Catches)
                foreach (var nested in EnumerateStatements(clause.Body))
                    yield return nested;
                if (tryStatement.FinallyBlock is not null)
                foreach (var nested in EnumerateStatements(tryStatement.FinallyBlock))
                    yield return nested;
            }
        }
    }

    private static IEnumerable<PowerShellBoundExpression> EnumerateDirectExpressions(PowerShellBoundStatement statement)
    {
        var expression = GetExpression(statement);
        if (expression is not null) yield return expression;
        if (statement is PowerShellBoundIfStatement conditional)
        {
            foreach (var clause in conditional.Clauses) yield return clause.Condition;
        }
        else if (statement is PowerShellBoundWhileStatement loop)
        {
            yield return loop.Condition;
        }
        else if (statement is PowerShellBoundForStatement forLoop)
        {
            if (forLoop.Initializer is not null) yield return forLoop.Initializer;
            if (forLoop.Condition is not null) yield return forLoop.Condition;
            if (forLoop.Iterator is not null) yield return forLoop.Iterator;
        }
        else if (statement is PowerShellBoundForEachStatement forEachLoop)
        {
            yield return forEachLoop.Collection;
        }
        else if (statement is PowerShellBoundSwitchStatement switchStatement)
        {
            yield return switchStatement.Value;
            foreach (var clause in switchStatement.Clauses) yield return clause.Value;
        }
        else if (statement is PowerShellBoundThrowStatement { Expression: not null } thrown)
        {
            yield return thrown.Expression;
        }
    }

    private static IEnumerable<PowerShellBoundVariableExpression> EnumerateVariableReads(PowerShellBoundExpression expression)
    {
        if (expression is PowerShellBoundVariableExpression variable) yield return variable;
        if (expression is PowerShellBoundConversionExpression conversion)
        {
            foreach (var read in EnumerateVariableReads(conversion.Operand)) yield return read;
        }
        if (expression is PowerShellBoundInvocationExpression invocation)
        {
            foreach (var argument in invocation.Arguments)
            foreach (var read in EnumerateVariableReads(argument))
                yield return read;
        }
        if (expression is PowerShellBoundBinaryExpression binary)
        {
            foreach (var read in EnumerateVariableReads(binary.Left)) yield return read;
            foreach (var read in EnumerateVariableReads(binary.Right)) yield return read;
        }
        if (expression is PowerShellBoundUnaryExpression unary)
        {
            foreach (var read in EnumerateVariableReads(unary.Operand)) yield return read;
        }
        if (expression is PowerShellBoundMutationExpression mutation)
        {
            if (mutation.Operation != PowerShellBoundMutationOperator.Assign)
                yield return new PowerShellBoundVariableExpression(mutation.Span, mutation.Target, mutation.Type);
            if (mutation.Value is not null)
            foreach (var read in EnumerateVariableReads(mutation.Value))
                yield return read;
        }
        if (expression is PowerShellBoundArrayExpression array)
        {
            foreach (var element in array.Elements)
            foreach (var read in EnumerateVariableReads(element))
                yield return read;
        }
        if (expression is PowerShellBoundClrMemberExpression { Receiver: not null } member)
        {
            foreach (var read in EnumerateVariableReads(member.Receiver)) yield return read;
        }
        if (expression is PowerShellBoundClrInvocationExpression clrInvocation)
        {
            if (clrInvocation.Receiver is not null)
            foreach (var read in EnumerateVariableReads(clrInvocation.Receiver))
                yield return read;
            foreach (var argument in clrInvocation.Arguments)
            foreach (var read in EnumerateVariableReads(argument))
                yield return read;
        }
    }

    private static IEnumerable<PowerShellBoundInvocationExpression> EnumerateInvocations(PowerShellBoundExpression expression)
    {
        if (expression is PowerShellBoundInvocationExpression invocation)
        {
            yield return invocation;
            foreach (var argument in invocation.Arguments)
            foreach (var nested in EnumerateInvocations(argument))
                yield return nested;
        }
        if (expression is PowerShellBoundConversionExpression conversion)
        {
            foreach (var nested in EnumerateInvocations(conversion.Operand)) yield return nested;
        }
        if (expression is PowerShellBoundBinaryExpression binary)
        {
            foreach (var nested in EnumerateInvocations(binary.Left)) yield return nested;
            foreach (var nested in EnumerateInvocations(binary.Right)) yield return nested;
        }
        if (expression is PowerShellBoundUnaryExpression unary)
        {
            foreach (var nested in EnumerateInvocations(unary.Operand)) yield return nested;
        }
        if (expression is PowerShellBoundMutationExpression { Value: not null } mutation)
        {
            foreach (var nested in EnumerateInvocations(mutation.Value)) yield return nested;
        }
        if (expression is PowerShellBoundArrayExpression array)
        {
            foreach (var element in array.Elements)
            foreach (var nested in EnumerateInvocations(element))
                yield return nested;
        }
        if (expression is PowerShellBoundClrMemberExpression { Receiver: not null } member)
        {
            foreach (var nested in EnumerateInvocations(member.Receiver)) yield return nested;
        }
        if (expression is PowerShellBoundClrInvocationExpression clrInvocation)
        {
            if (clrInvocation.Receiver is not null)
            foreach (var nested in EnumerateInvocations(clrInvocation.Receiver))
                yield return nested;
            foreach (var argument in clrInvocation.Arguments)
            foreach (var nested in EnumerateInvocations(argument))
                yield return nested;
        }
    }

    private static PowerShellSemanticDiagnostic[] OrderDiagnostics(IEnumerable<PowerShellSemanticDiagnostic> diagnostics)
        => diagnostics.OrderBy(static diagnostic => diagnostic.Span.DocumentId, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.Span.StartOffset)
            .ThenBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.Message, StringComparer.Ordinal)
            .ToArray();
}
