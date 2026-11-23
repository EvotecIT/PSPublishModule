using System.Management.Automation.Language;
using PowerForge.Generated.Runtime;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    private sealed record ComposedLifecycle(CommandAst UpstreamCommand, PowerShellLocalCallSignature Upstream,
        CommandAst DownstreamCommand, PowerShellLocalCallSignature Downstream, int First);

    private static bool IsTotalCompositionClause(PowerShellBoundBlock block)
        => PowerShellSemanticAnalyzer.EnumerateStatements(block, descendIntoCaptures: false).All(static statement => statement switch
        {
            PowerShellBoundTryStatement => true,
            PowerShellBoundIfStatement branch => branch.Clauses.All(static clause => IsTotalRecordExpression(clause.Condition)),
            PowerShellBoundAssignmentStatement { Operation: PowerShellBoundMutationOperator.Assign, PreserveStatementErrors: false } assignment
                => IsTotalRecordExpression(assignment.Value),
            PowerShellBoundReturnStatement { Expression: null } => true,
            _ => PowerShellSemanticAnalyzer.GetSuccessOutputExpression(statement) is { } output && IsTotalRecordExpression(output)
        });

    private static bool IsTotalRecordExpression(PowerShellBoundExpression expression)
    {
        if (expression is PowerShellBoundParameterPresenceExpression) return true;
        var reads = PowerShellSemanticAnalyzer.EnumerateVariableReads(expression).ToArray();
        if (reads.Any(static read => !PowerShellStableScalarTypePolicy.IsSupported(read.Type))) return false;
        var variables = reads.Select(static read => read.Symbol).Distinct().Take(2).ToArray();
        return variables.Length <= 1 && IsTotalStrictStageExpression(expression, variables.FirstOrDefault());
    }

    private static void ExcludeUnprovedFinallyCompositions(
        List<PowerShellBoundFunction> functions,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> signatures,
        Dictionary<string, PowerShellSemanticDiagnostic[]> failures)
        => functions.RemoveAll(function =>
        {
            foreach (var statement in PowerShellSemanticAnalyzer.EnumerateStatements(function.Body, descendIntoCaptures: false))
            {
                if (PowerShellSuccessOutputTypePolicy.GetStrictPipelineSink(statement) is not { } sink ||
                    !signatures.TryGetValue(sink.Upstream.Name, out var upstream) ||
                    !signatures.TryGetValue(sink.Downstream.Name, out var downstream) ||
                    !(upstream.PipelineLifecycleWritesFinallyRecords || downstream.PipelineLifecycleWritesFinallyRecords) ||
                    upstream.PipelineLifecycleProvesTotalTransfer && downstream.PipelineLifecycleProvesTotalTransfer)
                    continue;
                failures[function.Symbol.Name] = new[]
                {
                    new PowerShellSemanticDiagnostic("PSB2928",
                        "Strict composition with stage-level finally output requires both stages to have no terminating error path during record transfer.",
                        sink.Span)
                };
                return true;
            }
            return false;
        });

    private bool TryDescribeComposedLifecycle(PipelineAst pipeline,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions, out ComposedLifecycle composition)
    {
        composition = null!;
        if (pipeline.PipelineElements.Count is not (3 or 4) ||
            PowerShellCommandRegionSemanticBinder.IsBackground(pipeline) ||
            pipeline.PipelineElements[0] is not CommandExpressionAst { Redirections.Count: 0 } ||
            pipeline.PipelineElements[1] is not CommandAst { InvocationOperator: TokenKind.Unknown, Redirections.Count: 0 } left ||
            pipeline.PipelineElements[2] is not CommandAst { InvocationOperator: TokenKind.Unknown, Redirections.Count: 0 } right ||
            !TryGetLocalFunction(left, functions, out var upstream) || !upstream.IsPipelineLifecycle ||
            !TryGetLocalFunction(right, functions, out var downstream) || !downstream.IsPipelineLifecycle)
            return false;
        var first = 0;
        if (pipeline.PipelineElements.Count == 4 &&
            !TryGetLifecycleLimit(pipeline.PipelineElements[3], functions, out first)) return false;
        composition = new ComposedLifecycle(left, upstream, right, downstream, first);
        return true;
    }

    private PowerShellBoundStatement[]? BindComposedLifecycle(ComposedLifecycle composition, PipelineAst pipeline,
        ParsedSourceDocument document, IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics, string? targetFramework, List<PowerShellBoundLocal> locals)
    {
        const PowerShellCompilationCapability capabilities = PowerShellCompilationCapabilities.TypedExecutable;
        var span = PowerShellSourceParser.GetSpan(document, pipeline.Extent);
        var inputType = composition.Upstream.Parameters[composition.Upstream.PipelineLifecycleParameterIndex].Type;
        var intermediate = composition.Downstream.Parameters[composition.Downstream.PipelineLifecycleParameterIndex].Type;
        var outputType = composition.Downstream.DeclaredReturnType;
        if (composition.Upstream.DeclaredReturnType != intermediate)
        {
            RejectStrictPipeline(diagnostics, span, "requires the upstream output type to match the downstream by-value parameter");
            return null;
        }
        if (outputType is null || !PowerShellStableScalarTypePolicy.IsSupported(outputType))
        {
            RejectStrictPipeline(diagnostics, span, "requires a statically typed stable scalar from the downstream stage");
            return null;
        }

        var arrayType = inputType.MakeArrayType();
        var input = BindExpression(document, ((CommandExpressionAst)pipeline.PipelineElements[0]).Expression,
            symbols, functions, diagnostics, arrayType, targetFramework, capabilities);
        if (input is not (PowerShellBoundArrayExpression or PowerShellBoundVariableExpression) ||
            input.Type.ClrType != arrayType ||
            composition.Upstream.PipelineLifecycleRequiresNonNullInput && input.ValueState != PowerShellValueState.Known)
        {
            RejectStrictPipeline(diagnostics, span, "requires a typed array source with a proven input binding");
            return null;
        }
        if (input is PowerShellBoundArrayExpression array &&
            !array.Elements.All(static item => IsTotalStrictStageExpression(item, null)))
        {
            RejectStrictPipeline(diagnostics, input.Span, "requires an effect-free inline array before composed lifecycle stages");
            return null;
        }

        var setup = new List<PowerShellBoundStatement>();
        var source = Allocate(input.Span, input.Type.ClrType, pipeline.Extent.StartOffset, input);
        var left = AllocateStage(composition.UpstreamCommand, inputType);
        var right = AllocateStage(composition.DownstreamCommand, intermediate);
        var leftSetup = BindStage(composition.UpstreamCommand, composition.Upstream, left, inputType);
        var rightSetup = BindStage(composition.DownstreamCommand, composition.Downstream, right, intermediate);
        if (leftSetup is null || rightSetup is null) return null;
        setup.Add(new PowerShellBoundExpressionStatement(leftSetup.Span, leftSetup, emitsOutput: false));
        setup.Add(new PowerShellBoundExpressionStatement(rightSetup.Span, rightSetup, emitsOutput: false));

        var processesNull = PowerShellPipelineNullInputSemanticPolicy.TryBindElement(inputType, span, out var nullRecord) &&
            !composition.Upstream.PipelineLifecycleRequiresNonNullInput;
        var acceptsNullIntermediate = PowerShellPipelineNullInputSemanticPolicy.TryBindElement(intermediate, span, out var nullIntermediate) &&
            !composition.Downstream.PipelineLifecycleRequiresNonNullInput;
        var arguments = new PowerShellBoundExpression[]
        {
            source, new PowerShellBoundLiteralExpression(span, processesNull, StrictPipelineType(typeof(bool)), PowerShellValueState.Known),
            nullRecord ?? DefaultRecord(inputType), nullIntermediate ?? DefaultRecord(intermediate),
            left, right, StrictIntLiteral(span, composition.First), new PowerShellBoundStrictSuccessSinkExpression(span,
                composition.Upstream.Symbol, intermediate, composition.Downstream.Symbol, outputType,
                acceptsNullIntermediate, filtersNull: composition.First > 0)
        };
        var run = new PowerShellBoundClrInvocationExpression(span, typeof(PowerShellStrictPipelineComposition),
            nameof(PowerShellStrictPipelineComposition.Run), PowerShellClrInvocationKind.StaticMethod, null,
            PowerShellClrReceiverBehavior.None, arguments, arguments.Select(static argument => argument.Type.ClrType).ToArray(),
            StrictPipelineType(typeof(void)));
        setup.Add(StrictSuccessOutput(run));
        return setup.ToArray();

        PowerShellBoundExpression DefaultRecord(Type type)
            => new PowerShellBoundLiteralExpression(span, type.IsValueType ? Activator.CreateInstance(type) : null,
                StrictPipelineType(type), PowerShellValueState.Unknown);

        PowerShellBoundInvocationExpression? BindStage(CommandAst command, PowerShellLocalCallSignature signature,
            PowerShellBoundVariableExpression stage, Type element)
        {
            var noInput = new PowerShellBoundArrayExpression(stage.Span, element.MakeArrayType(),
                PowerShellBoundArrayKind.CollectedExpression, Array.Empty<PowerShellBoundExpression>());
            return PowerShellLocalCallSemanticBinder.Bind(document, command, signature,
                (syntax, expected) => BindExpression(document, syntax, symbols, functions, diagnostics, expected, targetFramework, capabilities),
                targetFramework, capabilities, diagnostics, noInput, lifecycleStage: stage);
        }

        PowerShellBoundVariableExpression AllocateStage(CommandAst command, Type element)
        {
            var stageSpan = PowerShellSourceParser.GetSpan(document, command.Extent);
            var type = typeof(PowerShellStrictPipelineStage<>).MakeGenericType(element);
            var instance = new PowerShellBoundClrInvocationExpression(stageSpan, type, ".ctor",
                PowerShellClrInvocationKind.Constructor, null, PowerShellClrReceiverBehavior.None,
                Array.Empty<PowerShellBoundExpression>(), Type.EmptyTypes, StrictPipelineType(type));
            return Allocate(stageSpan, type, command.Extent.StartOffset, instance);
        }

        PowerShellBoundVariableExpression Allocate(SourceSpan location, Type type, int offset, PowerShellBoundExpression value)
        {
            var symbol = CreateStrictPipelineItem(document, symbols, location, offset);
            var fact = StrictPipelineType(type);
            locals.Add(new PowerShellBoundLocal(symbol, fact));
            setup.Add(new PowerShellBoundAssignmentStatement(location, symbol, value));
            return new PowerShellBoundVariableExpression(location, symbol, fact, value.ValueState);
        }
    }
}
