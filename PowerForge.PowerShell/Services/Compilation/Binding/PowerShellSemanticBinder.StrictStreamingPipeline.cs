using System.Globalization;
using System.Management.Automation.Language;
using PowerForge.Generated.Runtime;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    private sealed record StrictPipelineStage(CommandAst Command, NamedBlockAst? Predicate = null, StrictPipelineMap? Map = null,
        string? ExpandProperty = null, string[]? ProjectProperties = null, int? First = null,
        PowerShellLocalCallSignature? Lifecycle = null);

    private sealed record StrictPipelineMap(StatementAst Result, StatementAst? Counter = null, bool CounterAfterResult = false);

    private sealed record StrictStreamingBinding(ISet<string> LocalFunctionNames, List<PowerShellBoundLocal> GeneratedLocals);

    private bool TryBindStrictStreamingStatement(
        StrictStreamingBinding? binding, ParsedSourceDocument document, StatementAst statement,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions, IReadOnlyList<PowerShellBoundStatement> preceding,
        ICollection<PowerShellSemanticDiagnostic> diagnostics, bool isTerminal, string? targetFramework,
        out PowerShellBoundStatement[]? streaming)
    {
        streaming = null;
        var diagnosticCount = diagnostics.Count;
        if (binding is null || !TryGetStrictStreamingPipeline(statement, symbols, out var pipeline, out var capture) ||
            !TryBindStrictStreamingPipeline(document, pipeline, symbols, functions, binding.LocalFunctionNames, preceding,
                diagnostics, isTerminal || statement is ReturnStatementAst, targetFramework, binding.GeneratedLocals, capture, out streaming))
            return false;
        if (streaming is null)
        {
            if (diagnostics.Count == diagnosticCount)
                RejectStrictPipeline(diagnostics, PowerShellSourceParser.GetSpan(document, statement.Extent),
                    "could not prove the source and stage contracts");
        }
        else if (statement is ReturnStatementAst && streaming.LastOrDefault() is not PowerShellBoundReturnStatement)
        {
            streaming = streaming.Append(new PowerShellBoundReturnStatement(
                PowerShellSourceParser.GetSpan(document, statement.Extent), null)).ToArray();
        }
        return true;
    }

    private static bool TryGetStrictStreamingPipeline(
        StatementAst statement, IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        out PipelineAst pipeline, out PowerShellSemanticSymbolBinding? capture)
    {
        capture = null;
        if (statement is PipelineAst direct)
        {
            pipeline = direct;
            return true;
        }
        if (statement is ReturnStatementAst { Pipeline: PipelineAst returned })
        {
            pipeline = returned;
            return true;
        }

        pipeline = null!;
        if (statement is not AssignmentStatementAst
            {
                Operator: TokenKind.Equals,
                Left: AttributedExpressionAst
                {
                    Attribute: TypeConstraintAst constraint,
                    Child: VariableExpressionAst { VariablePath.IsUnqualified: true } variable
                }
            } assignment ||
            UnwrapExpression(assignment.Right) is not ArrayExpressionAst
            {
                SubExpression.Traps: null or { Count: 0 },
                SubExpression.Statements.Count: 1
            } array || array.SubExpression.Statements[0] is not PipelineAst collected ||
            !symbols.TryGetValue(variable.VariablePath.UserPath, out var target) ||
            target.Symbol.Kind != PowerShellSymbolKind.Local ||
            constraint.TypeName.GetReflectionType() is not { IsArray: true } vector ||
            vector != target.Type.ClrType ||
            vector.GetArrayRank() != 1 ||
            !PowerShellStableScalarTypePolicy.IsSupported(vector.GetElementType()!))
            return false;

        pipeline = collected;
        capture = target;
        return true;
    }

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
        PowerShellSemanticSymbolBinding? capture,
        out PowerShellBoundStatement[]? result)
    {
        result = null;
        if (pipeline.PipelineElements.Count < 2 ||
            !TryDescribeStrictPipeline(pipeline, localFunctionNames, functions, out var stages))
            return false;

        var span = PowerShellSourceParser.GetSpan(document, pipeline.Extent);
        if (pipeline.Find(static node => node is CommandBaseAst { Redirections.Count: > 0 } ||
                node is PipelineAst nested && PowerShellCommandRegionSemanticBinder.IsBackground(nested),
                searchNestedScriptBlocks: true) is not null)
            return RejectStrictPipeline(diagnostics, span, "requires foreground expressions without redirection");

        if (capture is not null && stages.Any(static stage => stage.Lifecycle is not null))
            return RejectStrictPipeline(diagnostics, span, "cannot collect the output of a local lifecycle");

        const PowerShellCompilationCapability capabilities = PowerShellCompilationCapabilities.TypedExecutable;
        PowerShellBoundExpression? input;
        Type element;
        var enumeration = PowerShellForEachEnumerationKind.TypedArray;
        if (pipeline.PipelineElements[0] is CommandExpressionAst { Redirections.Count: 0 } source)
        {
            input = BindExpression(document, source.Expression, symbols, functions, diagnostics,
                InferPipelineInputType(source.Expression, symbols), targetFramework, capabilities);
            if (input is null) return true;
            var type = input.Type.ClrType;
            if (input is not (PowerShellBoundArrayExpression or PowerShellBoundVariableExpression) ||
                !type.IsArray || type.GetArrayRank() != 1 || !PowerShellStableScalarTypePolicy.IsSupported(type.GetElementType()!))
                return RejectStrictPipeline(diagnostics, span, "requires a one-dimensional, statically typed stable-scalar array");
            element = type.GetElementType()!;
        }
        else if (pipeline.PipelineElements[0] is CommandAst command &&
                 PowerShellStrictFilePipelinePolicy.TryGetSource(command, _commandResolver.Resolve(command, localFunctionNames, capabilities),
                     out var authoredPath, out var filesOnly))
        {
            var path = BindExpression(document, authoredPath, symbols, functions, diagnostics, typeof(string), targetFramework, capabilities);
            if (path is null) return true;
            if (path.Type.ClrType != typeof(string))
                return RejectStrictPipeline(diagnostics, path.Span, "requires a string filesystem path");
            element = filesOnly ? typeof(FileInfo) : typeof(FileSystemInfo);
            var method = filesOnly ? nameof(PowerShellStrictFileSystemReader.ReadFiles) : nameof(PowerShellStrictFileSystemReader.ReadItems);
            input = new PowerShellBoundClrInvocationExpression(PowerShellSourceParser.GetSpan(document, command.Extent),
                typeof(PowerShellStrictFileSystemReader), method, PowerShellClrInvocationKind.StaticMethod, null,
                PowerShellClrReceiverBehavior.None, new[] { path }, new[] { typeof(string) },
                StrictPipelineType(typeof(IEnumerable<>).MakeGenericType(element)));
            enumeration = PowerShellForEachEnumerationKind.RuntimeFreeEnumerable;
        }
        else
        {
            return false;
        }

        if (input is PowerShellBoundArrayExpression inlineArray &&
            stages.Any(static stage => stage.Map?.Counter is not null || stage.Lifecycle is not null) &&
            !inlineArray.Elements.All(static item => IsTotalStrictStageExpression(item, null)))
            return RejectStrictPipeline(diagnostics, input.Span, "requires a closed inline source before a counter or lifecycle reduction");

        var initializer = input as PowerShellBoundArrayExpression ??
            (input is PowerShellBoundVariableExpression sourceVariable
                ? FindStrictPipelineInitializer(preceding, sourceVariable.Symbol) as PowerShellBoundArrayExpression : null);
        if (initializer is null && element.IsValueType && input.ValueState != PowerShellValueState.Known)
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

        var capturedElementType = capture?.Type.ClrType.GetElementType();
        var collector = capturedElementType is null
            ? null
            : CreateRecordCollector(CreatePipelineCollectionName(symbols, pipeline.Extent.StartOffset), capturedElementType, span);
        if (capture is not null && collector is not null && capturedElementType is not null)
        {
            var count = new PowerShellBoundClrMemberExpression(span, collector.Type.ClrType, nameof(List<int>.Count),
                false, collector, PowerShellClrReceiverBehavior.None, StrictPipelineType(typeof(int)));
            var isEmpty = new PowerShellBoundBinaryExpression(span, PowerShellBoundBinaryOperator.Equal,
                count, StrictIntLiteral(span, 0), StrictPipelineType(typeof(bool)));
            // PowerShell creates a new typed array for each @() collection, including empty collections.
            var empty = new PowerShellBoundArrayExpression(span, capture.Type.ClrType, PowerShellBoundArrayKind.CollectedExpression,
                Array.Empty<PowerShellBoundExpression>());
            suffix.Add(new PowerShellBoundIfStatement(span,
                new[]
                {
                    new PowerShellBoundConditionalClause(isEmpty,
                        new PowerShellBoundBlock(span, new[] { new PowerShellBoundAssignmentStatement(span, capture.Symbol, empty) }))
                },
                new PowerShellBoundBlock(span,
                    new[] { new PowerShellBoundAssignmentStatement(span, capture.Symbol, ToArray(collector, capturedElementType, span)) })));
        }

        var item = new PowerShellBoundVariableExpression(span, itemSymbol, itemType,
            enumeration == PowerShellForEachEnumerationKind.RuntimeFreeEnumerable ? PowerShellValueState.Known : PowerShellValueState.Unknown);
        var body = BindStages(item);
        if (body is null) return true;

        var nullRecord = initializer is null && input.ValueState != PowerShellValueState.Known &&
            enumeration == PowerShellForEachEnumerationKind.TypedArray
            ? new PowerShellBoundLiteralExpression(span, null, itemType, PowerShellValueState.Null)
            : null;
        var loop = new PowerShellBoundForEachStatement(span, itemSymbol, element, input,
            enumeration, new PowerShellBoundBlock(span, body),
            declareVariable: true, nullCollectionElement: nullRecord);
        result = setup.Append(loop).Concat(suffix).ToArray();
        generatedLocals.AddRange(pipelineLocals);
        if (capture is not null)
            capture.Refine(capture.Type, PowerShellValueState.Known);
        foreach (var symbol in symbols.Values)
            if (counterWrites.Contains(symbol.Symbol.StableKey))
                symbol.ForgetValueState();
        return true;

        PowerShellBoundStatement[]? BindStages(PowerShellBoundVariableExpression current)
        {
            var statements = new List<PowerShellBoundStatement>();
            var continuations = new Stack<(List<PowerShellBoundStatement> Parent, SourceSpan Span,
                PowerShellBoundExpression? Predicate, PowerShellBoundStatement? After)>();
            foreach (var stage in stages)
            {
                stageSymbols["_"] = new PowerShellSemanticSymbolBinding(current.Symbol, current.Type);
                stageSymbols["_"].Refine(current.Type, current.ValueState);
                stageSymbols["PSItem"] = stageSymbols["_"];
                var stageSpan = PowerShellSourceParser.GetSpan(document, stage.Command.Extent);
                if (stage.Predicate is { } predicate)
                {
                    var condition = BindStageExpression(predicate.Statements[0], current, typeof(bool));
                    if (condition is null) return null;
                    GuardFollowingStages(stageSpan, condition);
                }
                else if (stage.Map is { } map)
                {
                    var value = BindStageExpression(map.Result, current, null);
                    if (value is null) return null;
                    PowerShellBoundStatement? update = null;
                    if (map.Counter is { } counter)
                    {
                        update = BindStrictPipelineCounter(counter, symbols, preceding, initializer,
                            counterWrites, diagnostics, stageSpan,
                            (syntax, localSymbols) => BindStatement(document, syntax, localSymbols, functions, diagnostics,
                                false, targetFramework, capabilities),
                            enumeration == PowerShellForEachEnumerationKind.RuntimeFreeEnumerable &&
                            PowerShellStrictCatchObservation.HasContainingUntypedCatch(pipeline)
                                ? PowerShellStrictCatchObservation.GetAuthoredLine(document, counter.Extent)
                                : null);
                        if (update is null) return null;
                        if (!map.CounterAfterResult) statements.Add(update);
                    }
                    current = AssignStageResult(stage, stageSpan, value);
                    if (map.CounterAfterResult) ContinueAfterStages(stageSpan, null, update);
                }
                else if (stage.ExpandProperty is { } property)
                {
                    var value = BindStrictPipelineProperty(current, stageSpan, property);
                    if (value is null)
                    {
                        RejectStrictPipeline(diagnostics, stageSpan,
                            "requires an exact, known total scalar property for Select-Object -ExpandProperty");
                        return null;
                    }
                    if (!PowerShellClrTypeSemantics.IsNonNullableValueType(current.Type.ClrType))
                        GuardFollowingStages(stageSpan, StrictPipelineRecordIsNotNull(stageSpan, current));
                    current = AssignStageResult(stage, stageSpan, value);
                }
                else if (stage.ProjectProperties is { } properties)
                {
                    var value = BindStrictProjectionRecord(current, stageSpan, properties);
                    if (value is null)
                    {
                        RejectStrictPipeline(diagnostics, stageSpan,
                            "requires each selected property to be a known string field of the input record");
                        return null;
                    }
                    GuardFollowingStages(stageSpan, StrictPipelineRecordIsNotNull(stageSpan, current));
                    current = AssignStageResult(stage, stageSpan, value);
                }
                else if (stage.First is { } first)
                {
                    var take = BindTake(stage, current, stageSpan, first);
                    if (take is null) return null;
                    statements.AddRange(take);
                }
                else
                {
                    var append = BindReduction(stage, current, stageSpan);
                    if (append is null) return null;
                    statements.Add(append);
                }
            }
            if (stages.Last() is { First: null, Lifecycle: null })
            {
                var output = BindRecordOutput(current);
                if (output is null) return null;
                statements.Add(output);
            }

            while (continuations.Count > 0)
            {
                var continuation = continuations.Pop();
                if (continuation.Predicate is { } condition)
                    continuation.Parent.Add(StrictPipelineIf(continuation.Span, condition, statements.ToArray()));
                else
                {
                    continuation.Parent.AddRange(statements);
                    continuation.Parent.Add(continuation.After!);
                }
                statements = continuation.Parent;
            }
            return statements.ToArray();

            void GuardFollowingStages(SourceSpan stageSpan, PowerShellBoundExpression condition)
                => ContinueAfterStages(stageSpan, condition, null);

            void ContinueAfterStages(SourceSpan stageSpan, PowerShellBoundExpression? condition, PowerShellBoundStatement? after)
            {
                continuations.Push((statements, stageSpan, condition, after));
                statements = new List<PowerShellBoundStatement>();
            }

            PowerShellBoundVariableExpression AssignStageResult(
                StrictPipelineStage stage, SourceSpan stageSpan, PowerShellBoundExpression value)
            {
                var mapped = CreateStrictPipelineItem(document, symbols, stageSpan, stage.Command.Extent.StartOffset);
                pipelineLocals.Add(new PowerShellBoundLocal(mapped, value.Type));
                statements.Add(new PowerShellBoundAssignmentStatement(stageSpan, mapped, value));
                return new PowerShellBoundVariableExpression(stageSpan, mapped, value.Type, value.ValueState);
            }
        }

        PowerShellBoundStatement[]? BindTake(StrictPipelineStage stage, PowerShellBoundVariableExpression current, SourceSpan stageSpan, int first)
        {
            var output = BindRecordOutput(current);
            if (output is null) return null;
            var count = CreateLocal(CreatePipelineOutputName(symbols, stage.Command.Extent.StartOffset), typeof(int), stageSpan);
            var zero = StrictIntLiteral(stageSpan, 0);
            var one = StrictIntLiteral(stageSpan, 1);
            setup.Add(new PowerShellBoundAssignmentStatement(stageSpan, count.Symbol, StrictIntLiteral(stageSpan, first)));
            var take = new PowerShellBoundStatement[]
            {
                output,
                new PowerShellBoundAssignmentStatement(stageSpan, count.Symbol,
                    new PowerShellBoundBinaryExpression(stageSpan, PowerShellBoundBinaryOperator.Subtract, count, one, count.Type)),
                StrictPipelineIf(stageSpan,
                    new PowerShellBoundBinaryExpression(stageSpan, PowerShellBoundBinaryOperator.Equal, count, zero, StrictPipelineType(typeof(bool))),
                    new PowerShellBoundStatement[] { new PowerShellBoundBreakStatement(stageSpan) })
            };
            if (PowerShellClrTypeSemantics.IsNonNullableValueType(current.Type.ClrType)) return take;
            return new[] { StrictPipelineIf(stageSpan, StrictPipelineRecordIsNotNull(stageSpan, current), take) };
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
            var collection = CreateRecordCollector(CreatePipelineCollectionName(symbols, stage.Command.Extent.StartOffset), recordType, stageSpan);
            var inputArray = ToArray(collection, recordType, stageSpan);
            var call = PowerShellLocalCallSemanticBinder.Bind(document, stage.Command, lifecycle,
                (syntax, expectedType) => BindExpression(document, syntax, symbols, functions, diagnostics, expectedType, targetFramework, capabilities),
                targetFramework, capabilities, diagnostics, inputArray);
            if (call is null) return null;
            suffix.Add(new PowerShellBoundReturnStatement(stageSpan, call));
            return AppendRecord(collection, current);
        }

        PowerShellBoundStatement? BindRecordOutput(PowerShellBoundVariableExpression current)
        {
            if (collector is null) return StrictSuccessOutput(current);
            if (current.Type.ClrType != capturedElementType)
            {
                RejectStrictPipeline(diagnostics, current.Span, "requires captured records to match the declared array element type");
                return null;
            }
            if (capturedElementType != typeof(string)) return AppendRecord(collector, current);

            // PowerShell converts a null success record to an empty string when assigning to String[].
            var recordSpan = current.Span;
            var nullValue = new PowerShellBoundLiteralExpression(recordSpan, null, current.Type, PowerShellValueState.Null);
            var isNull = new PowerShellBoundBinaryExpression(recordSpan, PowerShellBoundBinaryOperator.NullEqual,
                current, nullValue, StrictPipelineType(typeof(bool)));
            var emptyString = new PowerShellBoundLiteralExpression(recordSpan, string.Empty, current.Type, PowerShellValueState.Known);
            return new PowerShellBoundIfStatement(recordSpan,
                new[] { new PowerShellBoundConditionalClause(isNull,
                    new PowerShellBoundBlock(recordSpan, new[] { AppendRecord(collector, emptyString) })) },
                new PowerShellBoundBlock(recordSpan, new[] { AppendRecord(collector, current) }));
        }

        PowerShellBoundVariableExpression CreateRecordCollector(string name, Type recordType, SourceSpan collectorSpan)
        {
            var listType = typeof(List<>).MakeGenericType(recordType);
            var collection = CreateLocal(name, listType, collectorSpan);
            setup.Add(new PowerShellBoundAssignmentStatement(collectorSpan, collection.Symbol,
                new PowerShellBoundClrInvocationExpression(collectorSpan, listType, ".ctor", PowerShellClrInvocationKind.Constructor,
                    null, PowerShellClrReceiverBehavior.None, Array.Empty<PowerShellBoundExpression>(), Type.EmptyTypes, collection.Type)));
            return collection;
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

    private static PowerShellBoundExpression? BindStrictPipelineProperty(
        PowerShellBoundVariableExpression record, SourceSpan span, string property)
    {
        // These getters are total on their exact CLR record types and have the
        // same scalar shape on both pinned hosts. Other properties can be
        // adapted, invoke user code or report a nonterminating error per record.
        var type = record.Type.ClrType;
        if (type == typeof(FileSystemInfo) || type == typeof(FileInfo))
        {
            var fileProperty = property.Equals(nameof(FileSystemInfo.FullName), StringComparison.OrdinalIgnoreCase)
                ? nameof(FileSystemInfo.FullName)
                : property.Equals(nameof(FileSystemInfo.Name), StringComparison.OrdinalIgnoreCase) ? nameof(FileSystemInfo.Name) : null;
            return fileProperty is null ? null : new PowerShellBoundClrMemberExpression(span, type, fileProperty, false,
                record, PowerShellClrReceiverBehavior.None, StrictPipelineType(typeof(string)));
        }
        var memberName = type == typeof(string) && property.Equals(nameof(string.Length), StringComparison.OrdinalIgnoreCase)
            ? nameof(string.Length)
            : type == typeof(TimeSpan) && property.Equals(nameof(TimeSpan.Ticks), StringComparison.OrdinalIgnoreCase)
                ? nameof(TimeSpan.Ticks)
                : null;
        if (memberName is null) return null;
        var resultType = type == typeof(string) ? typeof(int) : typeof(long);
        var receiver = type == typeof(string) ? PowerShellClrReceiverBehavior.NormalizeNullString : PowerShellClrReceiverBehavior.None;
        return new PowerShellBoundClrMemberExpression(span, type, memberName, false, record, receiver, StrictPipelineType(resultType));
    }

    private static PowerShellBoundExpression StrictPipelineRecordIsNotNull(SourceSpan span, PowerShellBoundVariableExpression current)
    {
        var nullValue = new PowerShellBoundLiteralExpression(span, null, current.Type, PowerShellValueState.Null);
        return new PowerShellBoundBinaryExpression(span, PowerShellBoundBinaryOperator.NullNotEqual,
            current, nullValue, StrictPipelineType(typeof(bool)));
    }

    private static PowerShellBoundExpression? BindStrictProjectionRecord(
        PowerShellBoundVariableExpression record, SourceSpan span, string[] properties)
    {
        if (record.Type.ClrType != typeof(FileSystemInfo) && record.Type.ClrType != typeof(FileInfo)) return null;
        var values = new List<PowerShellBoundExpression>(properties.Length);
        var names = new List<PowerShellBoundExpression>(properties.Length);
        foreach (var property in properties)
        {
            if (BindStrictPipelineProperty(record, span, property) is not PowerShellBoundClrMemberExpression member ||
                member.Type.ClrType != typeof(string)) return null;
            names.Add(new PowerShellBoundLiteralExpression(span, property, StrictPipelineType(typeof(string)), PowerShellValueState.Known));
            values.Add(member);
        }
        var argumentType = typeof(string[]);
        var arguments = new PowerShellBoundExpression[]
        {
            new PowerShellBoundArrayExpression(span, argumentType, PowerShellBoundArrayKind.Literal, names.ToArray()),
            new PowerShellBoundArrayExpression(span, argumentType, PowerShellBoundArrayKind.Literal, values.ToArray())
        };
        var resultType = typeof(PowerShellStrictProjectionRecord);
        return new PowerShellBoundClrInvocationExpression(span, resultType, ".ctor", PowerShellClrInvocationKind.Constructor,
            null, PowerShellClrReceiverBehavior.None, arguments, new[] { argumentType, argumentType }, StrictPipelineType(resultType));
    }

    private static PowerShellBoundExpression ToArray(PowerShellBoundVariableExpression collection, Type recordType, SourceSpan span)
        => new PowerShellBoundClrInvocationExpression(span, collection.Type.ClrType, nameof(List<int>.ToArray),
            PowerShellClrInvocationKind.InstanceMethod, collection, PowerShellClrReceiverBehavior.None,
            Array.Empty<PowerShellBoundExpression>(), Type.EmptyTypes, StrictPipelineType(recordType.MakeArrayType()));

    private static PowerShellBoundStatement AppendRecord(PowerShellBoundVariableExpression collection, PowerShellBoundExpression record)
        => new PowerShellBoundExpressionStatement(record.Span,
            new PowerShellBoundClrInvocationExpression(record.Span, collection.Type.ClrType, nameof(List<int>.Add),
                PowerShellClrInvocationKind.InstanceMethod, collection, PowerShellClrReceiverBehavior.None,
                new[] { record }, new[] { record.Type.ClrType }, StrictPipelineType(typeof(void))), emitsOutput: false);

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
            if (provider is not null)
            {
                switch (provider.ProviderId)
                {
                    case "powerforge.command.filtering.where-object" when
                        PowerShellMappingCommandSemanticBinder.TryGetRuntimeFreeScriptBlock(command, "FilterScript", out var predicate) &&
                        predicate is { Unnamed: true, Statements.Count: 1 } && IsStrictStageResult(predicate.Statements[0]):
                        discovered.Add(new(command, Predicate: predicate));
                        continue;
                    case "powerforge.command.mapping.foreach-object" when
                        PowerShellMappingCommandSemanticBinder.TryGetRuntimeFreeScriptBlock(command, "Process", out var map) &&
                        map.Unnamed && DescribeStrictMap(map) is { } mapping:
                        discovered.Add(new(command, Map: mapping));
                        continue;
                    case "powerforge.command.projection.select-object" when
                        command.CommandElements.Count == 3 &&
                        command.CommandElements[1] is CommandParameterAst { Argument: null } expandParameter &&
                        expandParameter.ParameterName.Equals("ExpandProperty", StringComparison.OrdinalIgnoreCase) &&
                        command.CommandElements[2] is StringConstantExpressionAst { Value.Length: > 0 } property:
                        discovered.Add(new(command, ExpandProperty: property.Value));
                        continue;
                    case "powerforge.command.projection.select-object" when terminal &&
                        command.CommandElements.Count == 3 &&
                        command.CommandElements[1] is CommandParameterAst { Argument: null } firstParameter &&
                        firstParameter.ParameterName.Equals("First", StringComparison.OrdinalIgnoreCase) &&
                        command.CommandElements[2] is ConstantExpressionAst { Value: int first } && first > 0:
                        discovered.Add(new(command, First: first));
                        continue;
                    case "powerforge.command.projection.select-object" when terminal &&
                        TryGetStrictProjectionProperties(command, out var properties):
                        discovered.Add(new(command, ProjectProperties: properties));
                        continue;
                }
            }
            if (terminal && index > 1 && TryGetLocalFunction(command, functions, out var lifecycle) && lifecycle.IsPipelineLifecycle)
            {
                discovered.Add(new(command, Lifecycle: lifecycle));
                continue;
            }
            return false;
        }
        stages = discovered.ToArray();
        return true;
    }

    private static bool TryGetStrictProjectionProperties(CommandAst command, out string[] properties)
    {
        properties = Array.Empty<string>();
        var elements = command.CommandElements;
        var operand = elements.Count == 2 ? elements[1] :
            elements.Count == 3 && elements[1] is CommandParameterAst { Argument: null } named &&
            named.ParameterName.Equals("Property", StringComparison.OrdinalIgnoreCase) ? elements[2] : null;
        if (operand is StringConstantExpressionAst { Value.Length: > 0 } property)
        {
            properties = new[] { property.Value };
            return true;
        }
        if (operand is not ArrayLiteralAst { Elements.Count: >= 2 and <= 4 } array ||
            array.Elements.Any(static item => item is not StringConstantExpressionAst { Value.Length: > 0 }))
            return false;
        properties = array.Elements.Cast<StringConstantExpressionAst>().Select(static item => item.Value).ToArray();
        return properties.Distinct(StringComparer.OrdinalIgnoreCase).Count() == properties.Length;
    }

    private bool HasStrictPipelineItemOwner(VariableExpressionAst variable, SwitchStatementAst scope, StrictStreamingBinding binding,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions)
    {
        for (var ancestor = variable.Parent; ancestor is not null && !ReferenceEquals(ancestor, scope); ancestor = ancestor.Parent)
        {
            if (ancestor is SwitchStatementAst or CatchClauseAst) return false;
            if (ancestor is not ScriptBlockAst block) continue;
            return block.Parent is ScriptBlockExpressionAst { Parent: CommandAst { Parent: PipelineAst pipeline } command } &&
                TryDescribeStrictPipeline(pipeline, binding.LocalFunctionNames, functions, out var stages) &&
                stages.Any(stage => ReferenceEquals(stage.Command, command) && (stage.Predicate is not null || stage.Map is not null));
        }
        return false;
    }

    private static StrictPipelineMap? DescribeStrictMap(NamedBlockAst block)
    {
        var statements = block.Statements;
        if (statements.Count is < 1 or > 2) return null;
        var first = statements[0];
        if (statements.Count == 1)
            return IsStrictStageResult(first) ? new(first) : null;
        var second = statements[1];
        if (TryGetStrictStageIncrement(first, out _) && IsStrictStageResult(second))
            return new(second, first);
        if (IsStrictStageResult(first) && TryGetStrictStageIncrement(second, out _))
            return new(first, second, CounterAfterResult: true);
        return null;
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

    private static PowerShellBoundStreamWriteStatement StrictSuccessOutput(PowerShellBoundExpression value)
        => new(value.Span, PowerShellStreamCommandKind.Success, null, value, runtimeFree: true);

    private static PowerShellSymbolId CreateStrictPipelineItem(ParsedSourceDocument document,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols, SourceSpan span, int offset)
        => new(PowerShellSymbolKind.PipelineVariable, document.DocumentId, CreatePipelineItemName(symbols, offset), span,
            "pipeline/" + offset.ToString(CultureInfo.InvariantCulture) + "/current");
}
