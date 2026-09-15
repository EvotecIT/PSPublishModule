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

    /// <summary>
    /// Computes canonical type, data-flow, call, output, effect, and capability facts for
    /// analysis-only regions without applying the whole-function fallback policy.
    /// </summary>
    internal PowerShellBoundProgram AnalyzeRegionOpportunities(PowerShellBoundProgram program)
    {
        var current = program ?? throw new ArgumentNullException(nameof(program));
        foreach (var pass in _passes.Where(static pass => pass is not FallbackPass))
            current = pass.Run(current);
        // Whole-function eligibility is deliberately excluded here, but a region
        // still needs every directly or transitively invoked function to survive.
        return PropagateFallbackCalls(current);
    }

    private static IEnumerable<IPowerShellSemanticPass> CreateDefaultPasses()
    {
        yield return new PowerShellStatementErrorCallPass();
        yield return new PowerShellImplicitOutputPass();
        yield return new PowerShellDefiniteAssignmentPass();
        yield return new LocalTypePass();
        yield return new CallGraphPass();
        yield return new ReturnTypePass();
        yield return new CardinalityPass();
        yield return new ValueConsumptionPass();
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
                foreach (var local in function.Locals.Where(local => function.NativeFunctionBinding is null && local.Type.Provenance == PowerShellTypeFactProvenance.Unknown))
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
                    .SelectMany(EnumerateFunctionReferences)
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
                // Binding callbacks and their storage belong to the native invocation even when the body is constant.
                if (function.NativeFunctionBinding is not null)
                    value |= PowerShellRequiredCapability.NativeFunctionBinding | PowerShellRequiredCapability.PowerShellHost;
                foreach (var callee in GetDependencies(function, lookup)) value |= callee.Capabilities;
                return function.WithAnalysis(capabilities: value);
            }, static (left, right) => left.Capabilities == right.Capabilities);
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
            foreach (var callee in GetDependencies(function, lookup)) value |= selector(callee);
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

    private static IEnumerable<PowerShellBoundFunction> GetDependencies(
        PowerShellBoundFunction function,
        IReadOnlyDictionary<string, PowerShellBoundFunction> functions)
        => EnumerateStatements(function.Body).SelectMany(EnumerateDirectExpressions)
            .SelectMany(EnumerateFunctionReferences)
            .Select(invocation => functions.TryGetValue(invocation.Target.StableKey, out var callee) ? callee : null)
            .Where(static callee => callee is not null)
            .Cast<PowerShellBoundFunction>();

    private static IEnumerable<(PowerShellSymbolId Target, SourceSpan Span)> EnumerateFunctionReferences(PowerShellBoundExpression root)
    {
        foreach (var expression in EnumerateExpressions(root))
        {
            if (expression is PowerShellBoundInvocationExpression call) yield return (call.Target, call.Span);
            else if (expression is PowerShellBoundNativeScriptBlockExpression block) yield return (block.Target, block.Span);
        }
    }

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
                var consumesValue = statement is not (PowerShellBoundReturnStatement or
                    PowerShellBoundExpressionStatement or PowerShellBoundStreamWriteStatement { Provider: null });
                var target = FindConsumedCall(root, consumesValue);
                if (target is not null) return target;
            }
        }
        return null;

        PowerShellBoundFunction? FindConsumedCall(PowerShellBoundExpression expression, bool consumesValue)
        {
            if (consumesValue && expression is PowerShellBoundInvocationExpression { CapturesSuccessOutput: false } invocation &&
                functions.TryGetValue(invocation.Target.StableKey, out var target) &&
                (target.ReturnType.ClrType.IsArray || target.Capabilities.HasFlag(PowerShellRequiredCapability.CommandRegion) ||
                 HasSuccessStreamOutput(target, functions)))
                return target;
            // Native collection items are statement-output roots, not scalar operands.
            // Their arguments still consume values and are checked recursively.
            foreach (var child in EnumerateExpressionChildren(expression))
            {
                var consumed = FindConsumedCall(child, expression is not PowerShellBoundNativeCollectionExpression);
                if (consumed is not null) return consumed;
            }
            return null;
        }
    }

    private static bool HasSuccessStreamOutput(
        PowerShellBoundFunction function,
        IReadOnlyDictionary<string, PowerShellBoundFunction> functions)
    {
        var pending = new Stack<PowerShellBoundFunction>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        pending.Push(function);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current.Symbol.StableKey)) continue;
            if (EnumerateStatements(current.Body).Any(static statement =>
                    statement is PowerShellBoundStreamWriteStatement { Kind: PowerShellStreamCommandKind.Success }))
                return true;
            foreach (var callee in GetCallees(current, functions)) pending.Push(callee);
        }
        return false;
    }

    private static bool BlockHasEffect(
        PowerShellBoundBlock block,
        PowerShellSemanticEffect effect,
        IReadOnlyDictionary<string, PowerShellBoundFunction> functions)
        => block.Effects.HasFlag(effect) || EnumerateStatements(block)
            .SelectMany(EnumerateDirectExpressions).SelectMany(EnumerateInvocations)
            .Any(invocation => functions.TryGetValue(invocation.Target.StableKey, out var target) && target.Effects.HasFlag(effect));

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
            .Any(expression => CarriesCompilerDictionary(expression!, dictionaryLocals));
    }

    private static bool CarriesCompilerDictionary(PowerShellBoundExpression expression, ISet<string> dictionaryLocals)
        => expression is PowerShellBoundDictionaryExpression || expression.Type.DictionaryValueKind != PowerShellDictionaryValueKind.None ||
           expression is PowerShellBoundVariableExpression variable && dictionaryLocals.Contains(variable.Symbol.StableKey) ||
           expression is PowerShellBoundArrayExpression array && array.Elements.Any(element => CarriesCompilerDictionary(element, dictionaryLocals)) ||
           expression is PowerShellBoundConversionExpression conversion && conversion.Type.ClrType == typeof(object) &&
               CarriesCompilerDictionary(conversion.Operand, dictionaryLocals);

    internal static IEnumerable<PowerShellBoundExpression> EnumerateExpressions(PowerShellBoundExpression expression)
    {
        yield return expression;
        foreach (var child in EnumerateExpressionChildren(expression))
        foreach (var nested in EnumerateExpressions(child))
            yield return nested;
    }

    internal static IEnumerable<PowerShellBoundExpression> EnumerateExpressionChildren(PowerShellBoundExpression expression)
        => expression switch
        {
            PowerShellBoundRuntimeStateExpression runtime => runtime.Arguments,
            PowerShellBoundConversionExpression conversion => new[] { conversion.Operand },
            PowerShellBoundCommandAvailabilityExpression discovery => new[] { discovery.Name },
            PowerShellBoundHostedBooleanCommandExpression hostedBoolean => hostedBoolean.Arguments
                .Where(static argument => argument.Value is not null)
                .Select(static argument => argument.Value!),
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
            PowerShellBoundMutationExpression mutation =>
                (mutation.Value is null ? Array.Empty<PowerShellBoundExpression>() : new[] { mutation.Value })
                .Concat(mutation.NativeTargetRead is null || mutation.Operation == PowerShellBoundMutationOperator.Assign
                    ? Array.Empty<PowerShellBoundExpression>() : new PowerShellBoundExpression[] { mutation.NativeTargetRead }),
            PowerShellBoundArrayExpression array => array.Elements,
            PowerShellBoundNativeMemberExpression memberRead => new[] { memberRead.Receiver },
            PowerShellBoundNativeInvocationExpression nativeInvocation =>
                (nativeInvocation.Receiver is null ? Array.Empty<PowerShellBoundExpression>() : new[] { nativeInvocation.Receiver }).Concat(nativeInvocation.Arguments),
            PowerShellBoundNativeIndexExpression nativeIndex => new[] { nativeIndex.Receiver }.Concat(nativeIndex.Arguments),
            PowerShellBoundNativeCollectionExpression collection => collection.Items.Select(static item => item.Value),
            PowerShellBoundArrayCopyExpression copy => new[] { copy.Source },
            PowerShellBoundArrayConcatenationExpression concatenation => new[] { concatenation.Left, concatenation.Right },
            PowerShellBoundDictionaryExpression dictionary => dictionary.Entries.SelectMany(static entry => new[] { entry.Key, entry.Value }),
            PowerShellBoundPowerShellObjectExpression powerShellObject => powerShellObject.Properties.Select(static property => property.Value),
            PowerShellBoundIndexExpression index => new[] { index.Target, index.Index },
            PowerShellBoundClrMemberExpression { Receiver: not null } member => new[] { member.Receiver },
            PowerShellBoundClrInvocationExpression invocation => (invocation.Receiver is null ? Array.Empty<PowerShellBoundExpression>() : new[] { invocation.Receiver }).Concat(invocation.Arguments),
            _ => Array.Empty<PowerShellBoundExpression>()
        };

    private static PowerShellTypeFact ResolveType(
        PowerShellBoundExpression expression,
        IReadOnlyDictionary<string, PowerShellBoundFunction> functions)
        => expression switch
        {
            PowerShellBoundInvocationExpression { CapturesSuccessOutput: true } invocation => invocation.Type,
            PowerShellBoundInvocationExpression invocation when functions.TryGetValue(invocation.Target.StableKey, out var target) => target.ReturnType,
            _ => expression.Type
        };

    internal static PowerShellBoundExpression? GetExpression(PowerShellBoundStatement statement)
        => statement switch
        {
            PowerShellBoundAssignmentStatement assignment => assignment.Value,
            PowerShellBoundNativeAssignmentStatement assignment => assignment.Value,
            PowerShellBoundModuleVariableAssignmentStatement assignment => assignment.Value,
            PowerShellBoundReturnStatement returned => returned.Expression,
            PowerShellBoundExpressionStatement expression => expression.Expression,
            PowerShellBoundStreamWriteStatement stream => stream.Message,
            _ => null
        };

    internal static PowerShellBoundExpression? GetSuccessOutputExpression(PowerShellBoundStatement statement)
        => statement switch
        {
            PowerShellBoundReturnStatement { EmitsSuccessOutput: true } returned => returned.Expression,
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
        => EnumerateStatements(block, descendIntoCaptures: true);

    internal static IEnumerable<PowerShellBoundStatement> EnumerateStatements(PowerShellBoundBlock block, bool descendIntoCaptures)
    {
        foreach (var statement in block.Statements)
        {
            yield return statement;
            if (descendIntoCaptures && statement is PowerShellBoundOutputCaptureStatement capture)
            {
                foreach (var nested in EnumerateStatements(capture.Body, descendIntoCaptures)) yield return nested;
            }
            if (statement is PowerShellBoundStatementErrorBoundary boundary)
            {
                foreach (var nested in EnumerateStatements(boundary.Body, descendIntoCaptures)) yield return nested;
            }
            else if (statement is PowerShellBoundIfStatement conditional)
            {
                foreach (var clause in conditional.Clauses)
                foreach (var nested in EnumerateStatements(clause.Body, descendIntoCaptures))
                    yield return nested;
                if (conditional.ElseBlock is not null)
                foreach (var nested in EnumerateStatements(conditional.ElseBlock, descendIntoCaptures))
                    yield return nested;
            }
            else if (statement is PowerShellBoundWhileStatement loop)
            {
                foreach (var nested in EnumerateStatements(loop.Body, descendIntoCaptures)) yield return nested;
            }
            else if (statement is PowerShellBoundForStatement forLoop)
            {
                foreach (var nested in EnumerateStatements(forLoop.Body, descendIntoCaptures)) yield return nested;
            }
            else if (statement is PowerShellBoundForEachStatement forEachLoop)
            {
                foreach (var nested in EnumerateStatements(forEachLoop.Body, descendIntoCaptures)) yield return nested;
            }
            else if (statement is PowerShellBoundSwitchStatement switchStatement)
            {
                foreach (var clause in switchStatement.Clauses)
                foreach (var nested in EnumerateStatements(clause.Body, descendIntoCaptures))
                    yield return nested;
                if (switchStatement.DefaultBlock is not null)
                foreach (var nested in EnumerateStatements(switchStatement.DefaultBlock, descendIntoCaptures))
                    yield return nested;
            }
            else if (statement is PowerShellBoundTryStatement tryStatement)
            {
                foreach (var nested in EnumerateStatements(tryStatement.Body, descendIntoCaptures)) yield return nested;
                foreach (var clause in tryStatement.Catches)
                foreach (var nested in EnumerateStatements(clause.Body, descendIntoCaptures))
                    yield return nested;
                if (tryStatement.FinallyBlock is not null)
                foreach (var nested in EnumerateStatements(tryStatement.FinallyBlock, descendIntoCaptures))
                    yield return nested;
            }
        }
    }

    internal static IEnumerable<PowerShellBoundExpression> EnumerateDirectExpressions(PowerShellBoundStatement statement)
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
            if (memberAssignment.Receiver is not null) yield return memberAssignment.Receiver;
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
            if (forEachLoop.NullCollectionElement is not null) yield return forEachLoop.NullCollectionElement;
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
        if (expression is PowerShellBoundRuntimeStateExpression runtime)
        {
            foreach (var argument in runtime.Arguments)
            foreach (var read in EnumerateVariableReads(argument))
                yield return read;
        }
        if (expression is PowerShellBoundCommandAvailabilityExpression discovery)
        {
            foreach (var read in EnumerateVariableReads(discovery.Name)) yield return read;
        }
        if (expression is PowerShellBoundHostedBooleanCommandExpression hostedBoolean)
        {
            foreach (var argument in hostedBoolean.Arguments.Where(static argument => argument.Value is not null))
            foreach (var read in EnumerateVariableReads(argument.Value!))
                yield return read;
        }
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
            if (!mutation.UsesNativeInvocation && mutation.Operation != PowerShellBoundMutationOperator.Assign)
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
        if (expression is PowerShellBoundNativeMemberExpression memberRead)
        {
            foreach (var read in EnumerateVariableReads(memberRead.Receiver)) yield return read;
        }
        if (expression is PowerShellBoundNativeIndexExpression or PowerShellBoundNativeInvocationExpression)
        {
            foreach (var child in EnumerateExpressionChildren(expression))
            foreach (var read in EnumerateVariableReads(child)) yield return read;
        }
        if (expression is PowerShellBoundNativeCollectionExpression collection)
        {
            foreach (var item in collection.Items)
            foreach (var read in EnumerateVariableReads(item.Value)) yield return read;
        }
        if (expression is PowerShellBoundArrayCopyExpression copy)
        {
            foreach (var read in EnumerateVariableReads(copy.Source)) yield return read;
        }
        if (expression is PowerShellBoundArrayConcatenationExpression concatenation)
        {
            foreach (var read in EnumerateVariableReads(concatenation.Left)) yield return read;
            foreach (var read in EnumerateVariableReads(concatenation.Right)) yield return read;
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
        if (expression is PowerShellBoundRuntimeStateExpression runtime)
        {
            foreach (var argument in runtime.Arguments)
            foreach (var nested in EnumerateInvocations(argument))
                yield return nested;
        }
        if (expression is PowerShellBoundCommandAvailabilityExpression discovery)
        {
            foreach (var nested in EnumerateInvocations(discovery.Name)) yield return nested;
        }
        if (expression is PowerShellBoundHostedBooleanCommandExpression hostedBoolean)
        {
            foreach (var argument in hostedBoolean.Arguments.Where(static argument => argument.Value is not null))
            foreach (var nested in EnumerateInvocations(argument.Value!))
                yield return nested;
        }
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
        if (expression is PowerShellBoundArrayCopyExpression copy)
        {
            foreach (var nested in EnumerateInvocations(copy.Source)) yield return nested;
        }
        if (expression is PowerShellBoundNativeMemberExpression memberRead)
        {
            foreach (var nested in EnumerateInvocations(memberRead.Receiver)) yield return nested;
        }
        if (expression is PowerShellBoundNativeIndexExpression or PowerShellBoundNativeInvocationExpression)
        {
            foreach (var child in EnumerateExpressionChildren(expression))
            foreach (var nested in EnumerateInvocations(child)) yield return nested;
        }
        if (expression is PowerShellBoundNativeCollectionExpression collection)
        {
            foreach (var item in collection.Items)
            foreach (var nested in EnumerateInvocations(item.Value)) yield return nested;
        }
        if (expression is PowerShellBoundArrayConcatenationExpression concatenation)
        {
            foreach (var nested in EnumerateInvocations(concatenation.Left)) yield return nested;
            foreach (var nested in EnumerateInvocations(concatenation.Right)) yield return nested;
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
