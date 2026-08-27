namespace PowerForge;

internal interface IPowerShellSemanticPass
{
    string Id { get; }
    PowerShellBoundProgram Run(PowerShellBoundProgram program);
}

/// <summary>
/// Runs semantic passes in canonical identity order and rejects duplicate owners.
/// </summary>
internal sealed partial class PowerShellSemanticAnalyzer
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
        yield return new PowerShellDefiniteAssignmentPass();
        yield return new LocalTypePass();
        yield return new CallGraphPass();
        yield return new ReturnTypePass();
        yield return new CardinalityPass();
        yield return new EffectPass();
        yield return new CapabilityPass();
        yield return new FallbackPass();
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
                var blockingDiagnostic = program.Diagnostics.FirstOrDefault(diagnostic =>
                    diagnostic.Span.DocumentId.Equals(function.Symbol.DocumentId, StringComparison.Ordinal) &&
                    diagnostic.Span.StartOffset >= function.Symbol.Declaration.StartOffset &&
                    diagnostic.Span.StartOffset <= function.Symbol.Declaration.EndOffset);
                if (blockingDiagnostic is not null)
                {
                    return function.WithAnalysis(disposition: new PowerShellExecutionDisposition(
                        PowerShellExecutionDispositionKind.Fallback,
                        blockingDiagnostic.Code,
                        blockingDiagnostic.Message));
                }
                if ((function.ReturnType.ClrType == typeof(Dictionary<string, string>) ||
                     function.ReturnType.ClrType == typeof(System.Collections.Hashtable) ||
                     function.ReturnType.ClrType == typeof(System.Collections.Specialized.OrderedDictionary)) &&
                    ReturnsCompilerDictionary(function))
                {
                    return function.WithAnalysis(disposition: new PowerShellExecutionDisposition(
                        PowerShellExecutionDispositionKind.Fallback,
                        PowerShellCompilationFeatureIds.ForSyntax("VariableExpressionAst"),
                        "Typed dictionaries are lookup-only locals and cannot escape through the current public CLR return contract."));
                }
                if (IsMutuallyRecursive(function.Symbol, program.CallGraph))
                {
                    return function.WithAnalysis(disposition: new PowerShellExecutionDisposition(
                        PowerShellExecutionDispositionKind.Fallback,
                        PowerShellCompilationFeatureIds.FunctionGraph,
                        $"Function '{function.Symbol.Name}' participates in a mutually recursive local-call cycle, which is not supported by the typed ABI."));
                }
                if (function.ReturnType.Provenance == PowerShellTypeFactProvenance.Unknown)
                    return function.WithAnalysis(disposition: IsRecursive(function.Symbol, program.CallGraph)
                        ? new PowerShellExecutionDisposition(
                            PowerShellExecutionDispositionKind.Fallback,
                            PowerShellCompilationFeatureIds.FunctionGraph,
                            $"Function '{function.Symbol.Name}' participates in a recursive local-call cycle without a declared return contract.")
                        : new PowerShellExecutionDisposition(PowerShellExecutionDispositionKind.Fallback, "type.return.unknown", "The function return type is not statically known."));
                if (function.ReturnType.ClrType != typeof(void) && !BlockReturnsValue(function.Body))
                {
                    return function.WithAnalysis(disposition: new PowerShellExecutionDisposition(
                        PowerShellExecutionDispositionKind.Fallback,
                        "control.return.fallthrough",
                        $"Typed non-void unit '{function.Symbol.Name}' must end with an explicit return statement on every reachable path."));
                }
                var unresolvedCall = EnumerateStatements(function.Body)
                    .SelectMany(EnumerateDirectExpressions)
                    .SelectMany(EnumerateInvocations)
                    .FirstOrDefault(invocation => !lookup.ContainsKey(invocation.Target.StableKey));
                if (unresolvedCall is not null)
                {
                    return function.WithAnalysis(disposition: new PowerShellExecutionDisposition(
                        PowerShellExecutionDispositionKind.Fallback,
                        "call.binding.unavailable",
                        $"Local function '{unresolvedCall.Target.Name}' did not produce a bound function contract."));
                }
                var shouldProcessTarget = GetCallees(function, lookup).FirstOrDefault(ContainsShouldProcess);
                if (shouldProcessTarget is not null)
                {
                    return function.WithAnalysis(disposition: new PowerShellExecutionDisposition(
                        PowerShellExecutionDispositionKind.Fallback,
                        "call.should-process.command-identity",
                        $"Local function '{shouldProcessTarget.Symbol.Name}' uses ShouldProcess and must remain on the PowerShell command path so its command identity and ConfirmImpact are preserved."));
                }
                var validationTarget = GetValidationCallInsideTypeDiscriminatingTry(function, lookup);
                if (validationTarget is not null)
                {
                    return function.WithAnalysis(disposition: new PowerShellExecutionDisposition(
                        PowerShellExecutionDispositionKind.Fallback,
                        "call.validation.binding-exception",
                        $"Local function '{validationTarget.Symbol.Name}' performs parameter validation inside a typed try/catch, whose PowerShell binding-exception identity must remain on the PowerShell command path."));
                }
                var consumedTarget = GetConsumedCollectionOrHostedCall(function, lookup);
                if (consumedTarget is not null)
                {
                    var hosted = consumedTarget.Capabilities.HasFlag(PowerShellRequiredCapability.CommandRegion);
                    return function.WithAnalysis(disposition: new PowerShellExecutionDisposition(
                        PowerShellExecutionDispositionKind.Fallback,
                        hosted ? "call.command-region.cardinality" : "call.collection.cardinality",
                        hosted
                            ? $"Local function '{consumedTarget.Symbol.Name}' emits PowerShell command-region success output whose pipeline cardinality cannot be preserved when the call result is consumed."
                            : $"Local function '{consumedTarget.Symbol.Name}' returns an array whose PowerShell pipeline cardinality cannot be preserved when the result is consumed."));
                }
                var blocked = GetCallees(function, lookup).FirstOrDefault(static callee => callee.Disposition.Kind != PowerShellExecutionDispositionKind.Typed);
                return blocked is null
                    ? function
                    : function.WithAnalysis(disposition: new PowerShellExecutionDisposition(PowerShellExecutionDispositionKind.Fallback, "call.fallback", $"Local function '{blocked.Symbol.Name}' requires fallback."));
            }, static (left, right) => left.Disposition.Kind == right.Disposition.Kind && left.Disposition.ReasonCode == right.Disposition.ReasonCode);
            return program.WithFunctions(functions.Values.OrderBy(static function => function.Symbol.StableKey, StringComparer.Ordinal).ToArray());
        }

        private static bool BlockReturnsValue(PowerShellBoundBlock block)
            => block.Statements.LastOrDefault() switch
            {
                PowerShellBoundReturnStatement { EmitsValue: true } => true,
                PowerShellBoundExpressionStatement { EmitsOutput: true } => true,
                PowerShellBoundThrowStatement => true,
                PowerShellBoundIfStatement conditional => conditional.ElseBlock is not null &&
                    conditional.Clauses.All(static clause => BlockReturnsValue(clause.Body)) &&
                    BlockReturnsValue(conditional.ElseBlock),
                PowerShellBoundSwitchStatement switchStatement => switchStatement.DefaultBlock is not null &&
                    switchStatement.Clauses.All(static clause => BlockReturnsValue(clause.Body)) &&
                    BlockReturnsValue(switchStatement.DefaultBlock),
                PowerShellBoundTryStatement tryStatement =>
                    BlockReturnsValue(tryStatement.Body) &&
                    tryStatement.Catches.All(static clause => BlockReturnsValue(clause.Body)),
                _ => false
            };

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

    private static PowerShellBoundFunction? GetValidationCallInsideTypeDiscriminatingTry(
        PowerShellBoundFunction function,
        IReadOnlyDictionary<string, PowerShellBoundFunction> functions)
    {
        foreach (var tryStatement in EnumerateStatements(function.Body).OfType<PowerShellBoundTryStatement>()
                     .Where(static statement => statement.Catches.Any(static clause => clause.ExceptionTypes.Length > 0)))
        {
            foreach (var invocation in EnumerateStatements(tryStatement.Body)
                         .SelectMany(EnumerateDirectExpressions)
                         .SelectMany(EnumerateInvocations))
            {
                if (functions.TryGetValue(invocation.Target.StableKey, out var target) &&
                    target.Parameters.Any(static parameter => parameter.Contract.Validations.Length > 0))
                    return target;
            }
        }
        return null;
    }

    private static PowerShellBoundFunction? GetConsumedCollectionOrHostedCall(
        PowerShellBoundFunction function,
        IReadOnlyDictionary<string, PowerShellBoundFunction> functions)
    {
        foreach (var statement in EnumerateStatements(function.Body))
        {
            foreach (var root in EnumerateDirectExpressions(statement))
            {
                foreach (var invocation in EnumerateInvocations(root))
                {
                    if (ReferenceEquals(root, invocation) && statement is PowerShellBoundReturnStatement or PowerShellBoundExpressionStatement { EmitsOutput: true })
                        continue;
                    if (functions.TryGetValue(invocation.Target.StableKey, out var target) &&
                        (target.ReturnType.ClrType.IsArray || target.Capabilities.HasFlag(PowerShellRequiredCapability.CommandRegion)))
                        return target;
                }
            }
        }
        return null;
    }

    private static bool ContainsShouldProcess(PowerShellBoundFunction function)
        => EnumerateStatements(function.Body)
            .SelectMany(EnumerateDirectExpressions)
            .SelectMany(EnumerateExpressions)
            .OfType<PowerShellBoundRuntimeStateExpression>()
            .Any(static expression => expression.Kind is PowerShellRuntimeStateIntrinsicKind.ShouldProcessTarget or PowerShellRuntimeStateIntrinsicKind.ShouldProcessAction);

    private static bool ReturnsCompilerDictionary(PowerShellBoundFunction function)
    {
        var dictionaryLocals = EnumerateStatements(function.Body)
            .OfType<PowerShellBoundAssignmentStatement>()
            .Where(static assignment => assignment.Value is PowerShellBoundDictionaryExpression)
            .Select(static assignment => assignment.Target.StableKey)
            .ToHashSet(StringComparer.Ordinal);
        return EnumerateStatements(function.Body)
            .Select(GetSuccessOutputExpression)
            .Where(static expression => expression is not null)
            .Any(expression => expression is PowerShellBoundDictionaryExpression ||
                               expression is PowerShellBoundVariableExpression variable && dictionaryLocals.Contains(variable.Symbol.StableKey));
    }

    private static IEnumerable<PowerShellBoundExpression> EnumerateExpressions(PowerShellBoundExpression expression)
    {
        yield return expression;
        IEnumerable<PowerShellBoundExpression> children = expression switch
        {
            PowerShellBoundConversionExpression conversion => new[] { conversion.Operand },
            PowerShellBoundInvocationExpression invocation => invocation.Arguments,
            PowerShellBoundBinaryExpression binary => new[] { binary.Left, binary.Right },
            PowerShellBoundUnaryExpression unary => new[] { unary.Operand },
            PowerShellBoundTypeTestExpression typeTest => new[] { typeTest.Operand },
            PowerShellBoundRegexExpression regex => new[] { regex.Input, regex.Pattern }.Concat(regex.Replacement is null ? Array.Empty<PowerShellBoundExpression>() : new[] { regex.Replacement }),
            PowerShellBoundWildcardExpression wildcard => new[] { wildcard.Input, wildcard.Pattern },
            PowerShellBoundMembershipExpression membership => new[] { membership.Left, membership.Right },
            PowerShellBoundStringSplitExpression split => new[] { split.Input, split.Pattern },
            PowerShellBoundStringJoinExpression join => new[] { join.Values, join.Separator },
            PowerShellBoundInterpolatedStringExpression interpolated => interpolated.Parts.Where(static part => part.Expression is not null).Select(static part => part.Expression!),
            PowerShellBoundMutationExpression mutation when mutation.Value is not null => new[] { mutation.Value },
            PowerShellBoundArrayExpression array => array.Elements,
            PowerShellBoundDictionaryExpression dictionary => dictionary.Entries.SelectMany(static entry => new[] { entry.Key, entry.Value }),
            PowerShellBoundPowerShellObjectExpression powerShellObject => powerShellObject.Properties.Select(static property => property.Value),
            PowerShellBoundIndexExpression index => new[] { index.Target, index.Index },
            PowerShellBoundClrMemberExpression { Receiver: not null } member => new[] { member.Receiver },
            PowerShellBoundClrInvocationExpression invocation => (invocation.Receiver is null ? Array.Empty<PowerShellBoundExpression>() : new[] { invocation.Receiver }).Concat(invocation.Arguments),
            _ => Array.Empty<PowerShellBoundExpression>()
        };
        foreach (var child in children)
        foreach (var nested in EnumerateExpressions(child))
            yield return nested;
    }

    private static PowerShellTypeFact ResolveType(
        PowerShellBoundExpression expression,
        IReadOnlyDictionary<string, PowerShellBoundFunction> functions)
        => expression switch
        {
            PowerShellBoundInvocationExpression invocation when functions.TryGetValue(invocation.Target.StableKey, out var target) => target.ReturnType,
            _ => expression.Type
        };

    internal static PowerShellBoundExpression? GetExpression(PowerShellBoundStatement statement)
        => statement switch
        {
            PowerShellBoundAssignmentStatement assignment => assignment.Value,
            PowerShellBoundReturnStatement returned => returned.Expression,
            PowerShellBoundExpressionStatement expression => expression.Expression,
            _ => null
        };

    internal static PowerShellBoundExpression? GetSuccessOutputExpression(PowerShellBoundStatement statement)
        => statement switch
        {
            PowerShellBoundReturnStatement { EmitsValue: true } returned => returned.Expression,
            PowerShellBoundExpressionStatement { EmitsOutput: true } expression => expression.Expression,
            PowerShellBoundStreamWriteStatement { Kind: PowerShellStreamCommandKind.Success } stream => stream.Message,
            _ => null
        };

    private static PowerShellBoundExpression? GetCallableReturnExpression(PowerShellBoundStatement statement)
        => statement switch
        {
            PowerShellBoundReturnStatement { EmitsValue: true } returned => returned.Expression,
            PowerShellBoundExpressionStatement { EmitsOutput: true } expression => expression.Expression,
            _ => null
        };

    internal static IEnumerable<PowerShellBoundStatement> EnumerateStatements(PowerShellBoundBlock block)
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
        if (statement is PowerShellBoundIndexAssignmentStatement indexAssignment)
        {
            yield return indexAssignment.Target;
            yield return indexAssignment.Index;
            yield return indexAssignment.Value;
        }
        else if (statement is PowerShellBoundClrMemberAssignmentStatement memberAssignment)
        {
            yield return memberAssignment.Receiver;
            yield return memberAssignment.Value;
        }
        else if (statement is PowerShellBoundIfStatement conditional)
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

    internal static IEnumerable<PowerShellBoundVariableExpression> EnumerateVariableReads(PowerShellBoundExpression expression)
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
        if (expression is PowerShellBoundTypeTestExpression typeTest)
        {
            foreach (var read in EnumerateVariableReads(typeTest.Operand)) yield return read;
        }
        if (expression is PowerShellBoundRegexExpression regex)
        {
            foreach (var read in EnumerateVariableReads(regex.Input)) yield return read;
            foreach (var read in EnumerateVariableReads(regex.Pattern)) yield return read;
            if (regex.Replacement is not null)
            foreach (var read in EnumerateVariableReads(regex.Replacement))
                yield return read;
        }
        if (expression is PowerShellBoundWildcardExpression wildcard)
        {
            foreach (var read in EnumerateVariableReads(wildcard.Input)) yield return read;
            foreach (var read in EnumerateVariableReads(wildcard.Pattern)) yield return read;
        }
        if (expression is PowerShellBoundMembershipExpression membership)
        {
            foreach (var read in EnumerateVariableReads(membership.Left)) yield return read;
            foreach (var read in EnumerateVariableReads(membership.Right)) yield return read;
        }
        if (expression is PowerShellBoundStringSplitExpression split)
        {
            foreach (var read in EnumerateVariableReads(split.Input)) yield return read;
            foreach (var read in EnumerateVariableReads(split.Pattern)) yield return read;
        }
        if (expression is PowerShellBoundStringJoinExpression join)
        {
            foreach (var read in EnumerateVariableReads(join.Values)) yield return read;
            foreach (var read in EnumerateVariableReads(join.Separator)) yield return read;
        }
        if (expression is PowerShellBoundInterpolatedStringExpression interpolated)
        {
            foreach (var part in interpolated.Parts.Where(static part => part.Expression is not null))
            foreach (var read in EnumerateVariableReads(part.Expression!))
                yield return read;
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
        if (expression is PowerShellBoundDictionaryExpression dictionary)
        {
            foreach (var entry in dictionary.Entries)
            {
                foreach (var read in EnumerateVariableReads(entry.Key)) yield return read;
                foreach (var read in EnumerateVariableReads(entry.Value)) yield return read;
            }
        }
        if (expression is PowerShellBoundPowerShellObjectExpression powerShellObject)
        {
            foreach (var property in powerShellObject.Properties)
            foreach (var read in EnumerateVariableReads(property.Value))
                yield return read;
        }
        if (expression is PowerShellBoundIndexExpression index)
        {
            foreach (var read in EnumerateVariableReads(index.Target)) yield return read;
            foreach (var read in EnumerateVariableReads(index.Index)) yield return read;
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
        if (expression is PowerShellBoundTypeTestExpression typeTest)
        {
            foreach (var nested in EnumerateInvocations(typeTest.Operand)) yield return nested;
        }
        if (expression is PowerShellBoundRegexExpression regex)
        {
            foreach (var nested in EnumerateInvocations(regex.Input)) yield return nested;
            foreach (var nested in EnumerateInvocations(regex.Pattern)) yield return nested;
            if (regex.Replacement is not null)
            foreach (var nested in EnumerateInvocations(regex.Replacement))
                yield return nested;
        }
        if (expression is PowerShellBoundWildcardExpression wildcard)
        {
            foreach (var nested in EnumerateInvocations(wildcard.Input)) yield return nested;
            foreach (var nested in EnumerateInvocations(wildcard.Pattern)) yield return nested;
        }
        if (expression is PowerShellBoundMembershipExpression membership)
        {
            foreach (var nested in EnumerateInvocations(membership.Left)) yield return nested;
            foreach (var nested in EnumerateInvocations(membership.Right)) yield return nested;
        }
        if (expression is PowerShellBoundStringSplitExpression split)
        {
            foreach (var nested in EnumerateInvocations(split.Input)) yield return nested;
            foreach (var nested in EnumerateInvocations(split.Pattern)) yield return nested;
        }
        if (expression is PowerShellBoundStringJoinExpression join)
        {
            foreach (var nested in EnumerateInvocations(join.Values)) yield return nested;
            foreach (var nested in EnumerateInvocations(join.Separator)) yield return nested;
        }
        if (expression is PowerShellBoundInterpolatedStringExpression interpolated)
        {
            foreach (var part in interpolated.Parts.Where(static part => part.Expression is not null))
            foreach (var nested in EnumerateInvocations(part.Expression!))
                yield return nested;
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
        if (expression is PowerShellBoundDictionaryExpression dictionary)
        {
            foreach (var entry in dictionary.Entries)
            {
                foreach (var nested in EnumerateInvocations(entry.Key)) yield return nested;
                foreach (var nested in EnumerateInvocations(entry.Value)) yield return nested;
            }
        }
        if (expression is PowerShellBoundPowerShellObjectExpression powerShellObject)
        {
            foreach (var property in powerShellObject.Properties)
            foreach (var nested in EnumerateInvocations(property.Value))
                yield return nested;
        }
        if (expression is PowerShellBoundIndexExpression index)
        {
            foreach (var nested in EnumerateInvocations(index.Target)) yield return nested;
            foreach (var nested in EnumerateInvocations(index.Index)) yield return nested;
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

    internal static PowerShellSemanticDiagnostic[] OrderDiagnostics(IEnumerable<PowerShellSemanticDiagnostic> diagnostics)
        => diagnostics.OrderBy(static diagnostic => diagnostic.Span.DocumentId, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.Span.StartOffset)
            .ThenBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.Message, StringComparer.Ordinal)
            .ToArray();
}
