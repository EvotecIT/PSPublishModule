using System.Globalization;
using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    private sealed record StrictPipelineStage(CommandAst Command, NamedBlockAst? Predicate, NamedBlockAst? Map, int? First,
        PowerShellLocalCallSignature? Lifecycle);

    /// <summary>Lowers proven stages into one CLR record loop before ordinary statement binding.</summary>
    private bool TryBindStrictStreamingPipeline(
        ParsedSourceDocument document,
        PipelineAst pipeline,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ISet<string> localFunctionNames,
        IReadOnlyList<PowerShellBoundStatement> preceding,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        bool isTerminal,
        string? targetFramework,
        List<PowerShellBoundLocal> generatedLocals,
        out PowerShellBoundStatement[]? result)
    {
        result = null;
        if (pipeline.PipelineElements.Count < 2 ||
            pipeline.PipelineElements[0] is not CommandExpressionAst { Redirections.Count: 0 } source ||
            !TryDescribeStrictPipeline(pipeline, localFunctionNames, functions, out var stages))
            return false;

        var span = PowerShellSourceParser.GetSpan(document, pipeline.Extent);
        if (pipeline.Find(static node => node is CommandBaseAst { Redirections.Count: > 0 } ||
                node is PipelineAst nested && PowerShellCommandRegionSemanticBinder.IsBackground(nested),
                searchNestedScriptBlocks: true) is not null)
            return RejectStrictPipeline(diagnostics, span, "requires foreground expressions without redirection");

        const PowerShellCompilationCapability capabilities = PowerShellCompilationCapabilities.TypedExecutable;
        var input = BindExpression(document, source.Expression, symbols, functions, diagnostics,
            InferPipelineInputType(source.Expression, symbols), targetFramework, capabilities);
        if (input is null) return true;

        var type = input.Type.ClrType;
        if (input is not (PowerShellBoundArrayExpression or PowerShellBoundVariableExpression) ||
            !type.IsArray || type.GetArrayRank() != 1 || !PowerShellStableScalarTypePolicy.IsSupported(type.GetElementType()!))
            return RejectStrictPipeline(diagnostics, span, "requires a one-dimensional, statically typed stable-scalar array");

        if (input is PowerShellBoundArrayExpression inlineArray &&
            stages.Any(static stage => stage.Map?.Statements.Count == 2 || stage.Lifecycle is not null) &&
            !inlineArray.Elements.All(static item => IsTotalStrictStageExpression(item, null)))
            return RejectStrictPipeline(diagnostics, input.Span, "requires a closed inline source before a counter or lifecycle reduction");

        var element = type.GetElementType()!;
        var initializer = input as PowerShellBoundArrayExpression ??
            (input is PowerShellBoundVariableExpression sourceVariable
                ? FindStrictPipelineInitializer(preceding, sourceVariable.Symbol) as PowerShellBoundArrayExpression : null);
        if (initializer is null && element.IsValueType)
            return RejectStrictPipeline(diagnostics, span, "requires a proven non-null array for value-type records");

        var pipelineLocals = new List<PowerShellBoundLocal>();
        var setup = new List<PowerShellBoundStatement>();
        var suffix = new List<PowerShellBoundStatement>();
        var stageSymbols = CloneSymbols(symbols);
        var counterWrites = new HashSet<string>(StringComparer.Ordinal);
        var itemSymbol = CreateStrictPipelineItem(document, symbols, span, pipeline.Extent.StartOffset);
        var itemType = StrictPipelineType(element);
        if (element == typeof(int) && initializer is not null && GetStrictElementRange(initializer) is { } range)
            itemType = itemType.WithInt32Range(range);

        var item = new PowerShellBoundVariableExpression(span, itemSymbol, itemType, PowerShellValueState.Unknown);
        var body = BindStages(item);
        if (body is null) return true;

        var nullRecord = initializer is null
            ? new PowerShellBoundLiteralExpression(span, null, itemType, PowerShellValueState.Null)
            : null;
        var loop = new PowerShellBoundForEachStatement(span, itemSymbol, element, input,
            PowerShellForEachEnumerationKind.TypedArray, new PowerShellBoundBlock(span, body),
            declareVariable: true, nullCollectionElement: nullRecord);
        result = setup.Append(loop).Concat(suffix).ToArray();
        generatedLocals.AddRange(pipelineLocals);
        foreach (var symbol in symbols.Values)
            if (counterWrites.Contains(symbol.Symbol.StableKey))
                symbol.ForgetValueState();
        return true;

        PowerShellBoundStatement[]? BindStages(PowerShellBoundVariableExpression current)
        {
            var statements = new List<PowerShellBoundStatement>();
            var guards = new Stack<(List<PowerShellBoundStatement> Parent, SourceSpan Span, PowerShellBoundExpression Predicate)>();
            foreach (var stage in stages)
            {
                stageSymbols["_"] = new PowerShellSemanticSymbolBinding(current.Symbol, current.Type);
                stageSymbols["PSItem"] = stageSymbols["_"];
                var stageSpan = PowerShellSourceParser.GetSpan(document, stage.Command.Extent);
                if (stage.Predicate is { } predicate)
                {
                    var condition = BindStageExpression(predicate.Statements[0], current, typeof(bool));
                    if (condition is null) return null;
                    guards.Push((statements, stageSpan, condition));
                    statements = new List<PowerShellBoundStatement>();
                }
                else if (stage.Map is { } map)
                {
                    var value = BindStageExpression(map.Statements.Last(), current, null);
                    if (value is null) return null;
                    if (map.Statements.Count == 2)
                    {
                        var update = BindStrictPipelineCounter(map.Statements[0], symbols, preceding, initializer,
                            counterWrites, diagnostics, stageSpan,
                            (syntax, localSymbols) => BindStatement(document, syntax, localSymbols, functions, diagnostics,
                                false, targetFramework, capabilities));
                        if (update is null) return null;
                        statements.Add(update);
                    }
                    var mapped = CreateStrictPipelineItem(document, symbols, stageSpan, stage.Command.Extent.StartOffset);
                    pipelineLocals.Add(new PowerShellBoundLocal(mapped, value.Type));
                    statements.Add(new PowerShellBoundAssignmentStatement(stageSpan, mapped, value));
                    current = new PowerShellBoundVariableExpression(stageSpan, mapped, value.Type, value.ValueState);
                }
                else if (stage.First is { } first)
                {
                    statements.AddRange(BindTake(stage, current, stageSpan, first));
                }
                else
                {
                    var append = BindReduction(stage, current, stageSpan);
                    if (append is null) return null;
                    statements.Add(append);
                }
            }
            if (stages.Last() is { First: null, Lifecycle: null })
                statements.Add(StrictPipelineOutput(current));

            while (guards.Count > 0)
            {
                var guard = guards.Pop();
                guard.Parent.Add(StrictPipelineIf(guard.Span, guard.Predicate, statements.ToArray()));
                statements = guard.Parent;
            }
            return statements.ToArray();
        }

        PowerShellBoundStatement[] BindTake(StrictPipelineStage stage, PowerShellBoundVariableExpression current, SourceSpan stageSpan, int first)
        {
            var count = CreateLocal(CreatePipelineOutputName(symbols, stage.Command.Extent.StartOffset), typeof(int), stageSpan);
            var zero = StrictIntLiteral(stageSpan, 0);
            var one = StrictIntLiteral(stageSpan, 1);
            setup.Add(new PowerShellBoundAssignmentStatement(stageSpan, count.Symbol, StrictIntLiteral(stageSpan, first)));
            var take = new PowerShellBoundStatement[]
            {
                StrictPipelineOutput(current),
                new PowerShellBoundAssignmentStatement(stageSpan, count.Symbol,
                    new PowerShellBoundBinaryExpression(stageSpan, PowerShellBoundBinaryOperator.Subtract, count, one, count.Type)),
                StrictPipelineIf(stageSpan,
                    new PowerShellBoundBinaryExpression(stageSpan, PowerShellBoundBinaryOperator.Equal, count, zero, StrictPipelineType(typeof(bool))),
                    new PowerShellBoundStatement[] { new PowerShellBoundBreakStatement(stageSpan) })
            };
            if (PowerShellClrTypeSemantics.IsNonNullableValueType(current.Type.ClrType)) return take;
            var nullValue = new PowerShellBoundLiteralExpression(stageSpan, null, current.Type, PowerShellValueState.Null);
            var notNull = new PowerShellBoundBinaryExpression(stageSpan, PowerShellBoundBinaryOperator.NullNotEqual,
                current, nullValue, StrictPipelineType(typeof(bool)));
            return new[] { StrictPipelineIf(stageSpan, notNull, take) };
        }

        PowerShellBoundStatement? BindReduction(StrictPipelineStage stage, PowerShellBoundVariableExpression current, SourceSpan stageSpan)
        {
            var lifecycle = stage.Lifecycle!;
            if (!isTerminal || stage.Command.CommandElements.Count != 1 ||
                stages.Take(stages.Length - 1).Any(static prior => prior.Predicate is null) ||
                lifecycle.PipelineLifecycleReturnsCollection ||
                lifecycle.Parameters[lifecycle.PipelineLifecycleParameterIndex].Type != current.Type.ClrType)
            {
                RejectStrictPipeline(diagnostics, stageSpan,
                    "requires only pure filters before a terminal scalar-output local lifecycle with the exact by-value element type");
                return null;
            }

            if (initializer is null || current.Type.Int32Range is not { } recordRange ||
                lifecycle.StrictPipelineReduction?.IsTotal(recordRange, initializer.Elements.Length) != true)
            {
                RejectStrictPipeline(diagnostics, stageSpan,
                    "requires lifecycle begin, process and end operations proven total for the bounded Int32 input");
                return null;
            }
            var recordType = current.Type.ClrType;
            var listType = typeof(List<>).MakeGenericType(recordType);
            var collection = CreateLocal(CreatePipelineCollectionName(symbols, stage.Command.Extent.StartOffset), listType, stageSpan);
            setup.Add(new PowerShellBoundAssignmentStatement(stageSpan, collection.Symbol,
                new PowerShellBoundClrInvocationExpression(stageSpan, listType, ".ctor", PowerShellClrInvocationKind.Constructor,
                    null, PowerShellClrReceiverBehavior.None, Array.Empty<PowerShellBoundExpression>(), Type.EmptyTypes, collection.Type)));
            var inputArray = new PowerShellBoundClrInvocationExpression(stageSpan, listType, nameof(List<int>.ToArray),
                PowerShellClrInvocationKind.InstanceMethod, collection, PowerShellClrReceiverBehavior.None,
                Array.Empty<PowerShellBoundExpression>(), Type.EmptyTypes, StrictPipelineType(recordType.MakeArrayType()));
            var call = PowerShellLocalCallSemanticBinder.Bind(document, stage.Command, lifecycle,
                (syntax, expectedType) => BindExpression(document, syntax, symbols, functions, diagnostics, expectedType, targetFramework, capabilities),
                targetFramework, capabilities, diagnostics, inputArray);
            if (call is null) return null;
            suffix.Add(new PowerShellBoundReturnStatement(stageSpan, call));
            var add = new PowerShellBoundClrInvocationExpression(stageSpan, listType, nameof(List<int>.Add),
                PowerShellClrInvocationKind.InstanceMethod, collection, PowerShellClrReceiverBehavior.None,
                new[] { current }, new[] { recordType }, StrictPipelineType(typeof(void)));
            return new PowerShellBoundExpressionStatement(stageSpan, add, emitsOutput: false);
        }

        PowerShellBoundExpression? BindStageExpression(StatementAst syntax, PowerShellBoundVariableExpression current, Type? requiredType)
        {
            var expression = BindExpression(document, syntax, stageSymbols, functions, diagnostics,
                targetFramework: targetFramework, capabilities: capabilities);
            if (expression is null) return null;
            if (requiredType is not null && expression.Type.ClrType != requiredType ||
                !PowerShellStableScalarTypePolicy.IsSupported(expression.Type) ||
                !IsTotalStrictStageExpression(expression, current.Symbol))
            {
                RejectStrictPipeline(diagnostics, expression.Span, "requires a total, stable-scalar expression in each filter and map");
                return null;
            }
            return expression;
        }

        PowerShellBoundVariableExpression CreateLocal(string name, Type type, SourceSpan declaration)
        {
            var symbol = new PowerShellSymbolId(PowerShellSymbolKind.Local, document.DocumentId, name, declaration,
                "pipeline/" + pipeline.Extent.StartOffset.ToString(CultureInfo.InvariantCulture) + "/" + name);
            var fact = StrictPipelineType(type);
            pipelineLocals.Add(new PowerShellBoundLocal(symbol, fact));
            return new PowerShellBoundVariableExpression(declaration, symbol, fact, PowerShellValueState.Known);
        }
    }

    private bool TryDescribeStrictPipeline(
        PipelineAst pipeline,
        ISet<string> localFunctionNames,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        out StrictPipelineStage[] stages)
    {
        var discovered = new List<StrictPipelineStage>();
        stages = Array.Empty<StrictPipelineStage>();
        for (var index = 1; index < pipeline.PipelineElements.Count; index++)
        {
            if (pipeline.PipelineElements[index] is not CommandAst { InvocationOperator: TokenKind.Unknown, Redirections.Count: 0 } command)
                return false;
            var terminal = index == pipeline.PipelineElements.Count - 1;
            var resolution = _commandResolver.Resolve(command, localFunctionNames, PowerShellCompilationCapabilities.TypedExecutable);
            var provider = resolution.IsProvider ? resolution.Contract : null;
            var name = command.GetCommandName()?.Split('\\').Last();
            if (provider?.CommandName.Equals(name, StringComparison.OrdinalIgnoreCase) == true)
            {
                switch (provider.ProviderId)
                {
                    case "powerforge.command.filtering.where-object" when
                        PowerShellMappingCommandSemanticBinder.TryGetRuntimeFreeScriptBlock(command, "FilterScript", out var predicate) &&
                        predicate is { Unnamed: true, Statements.Count: 1 } && IsStrictStageResult(predicate.Statements[0]):
                        discovered.Add(new(command, predicate, null, null, null));
                        continue;
                    case "powerforge.command.mapping.foreach-object" when
                        PowerShellMappingCommandSemanticBinder.TryGetRuntimeFreeScriptBlock(command, "Process", out var map) &&
                        map.Unnamed && map.Statements.Count is 1 or 2 && IsStrictStageResult(map.Statements.Last()) &&
                        (map.Statements.Count == 1 || TryGetStrictStageIncrement(map.Statements[0], out _)):
                        discovered.Add(new(command, null, map, null, null));
                        continue;
                    case "powerforge.command.projection.select-object" when terminal &&
                        command.CommandElements.Count == 3 &&
                        command.CommandElements[1] is CommandParameterAst { Argument: null } firstParameter &&
                        firstParameter.ParameterName.Equals("First", StringComparison.OrdinalIgnoreCase) &&
                        command.CommandElements[2] is ConstantExpressionAst { Value: int first } && first > 0:
                        discovered.Add(new(command, null, null, first, null));
                        continue;
                }
            }
            if (terminal && index > 1 && TryGetLocalFunction(command, functions, out var lifecycle) && lifecycle.IsPipelineLifecycle)
            {
                discovered.Add(new(command, null, null, null, lifecycle));
                continue;
            }
            return false;
        }
        stages = discovered.ToArray();
        return true;
    }

    private static bool IsStrictStageResult(StatementAst statement)
        => statement is PipelineAst { PipelineElements.Count: 1 } pipeline &&
           pipeline.PipelineElements[0] is CommandExpressionAst { Redirections.Count: 0, Expression: not UnaryExpressionAst
               { TokenKind: TokenKind.PlusPlus or TokenKind.PostfixPlusPlus or TokenKind.MinusMinus or TokenKind.PostfixMinusMinus } };

    private static bool TryGetStrictStageIncrement(StatementAst statement, out VariableExpressionAst variable)
    {
        variable = null!;
        if (statement is not PipelineAst { PipelineElements.Count: 1 } pipeline ||
            pipeline.PipelineElements[0] is not CommandExpressionAst { Redirections.Count: 0, Expression: UnaryExpressionAst
            {
                TokenKind: TokenKind.PlusPlus or TokenKind.PostfixPlusPlus,
                Child: VariableExpressionAst { VariablePath.IsUnqualified: true } counter
            } })
            return false;
        variable = counter;
        return true;
    }

    private static bool RejectStrictPipeline(ICollection<PowerShellSemanticDiagnostic> diagnostics, SourceSpan span, string reason)
    {
        diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2947", $"Strict streaming pipeline lowering {reason}.", span));
        return true;
    }

    private static PowerShellTypeFact StrictPipelineType(Type type)
        => new(type, PowerShellTypeFactProvenance.Inferred, "The Strict pipeline has a fixed CLR record contract.");

    private static PowerShellBoundLiteralExpression StrictIntLiteral(SourceSpan span, int value)
        => new(span, value, StrictPipelineType(typeof(int)), PowerShellValueState.Known);

    private static PowerShellBoundIfStatement StrictPipelineIf(SourceSpan span, PowerShellBoundExpression condition, PowerShellBoundStatement[] statements)
        => new(span, new[] { new PowerShellBoundConditionalClause(condition, new PowerShellBoundBlock(span, statements)) }, null);

    private static PowerShellBoundStreamWriteStatement StrictPipelineOutput(PowerShellBoundExpression value)
        => new(value.Span, PowerShellStreamCommandKind.Success, null, value, runtimeFree: true);

    private static PowerShellSymbolId CreateStrictPipelineItem(ParsedSourceDocument document,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols, SourceSpan span, int offset)
        => new(PowerShellSymbolKind.PipelineVariable, document.DocumentId, CreatePipelineItemName(symbols, offset), span,
            "pipeline/" + offset.ToString(CultureInfo.InvariantCulture) + "/current");
}
