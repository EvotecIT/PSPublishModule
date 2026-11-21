using System.Globalization;
using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    private static IEnumerable<StatementAst> GetFunctionStatements(ScriptBlockAst body, bool includeClean = false)
    {
        if (body.BeginBlock is not null)
            foreach (var statement in body.BeginBlock.Statements) yield return statement;
        if (body.ProcessBlock is not null)
            foreach (var statement in body.ProcessBlock.Statements) yield return statement;
        if (body.EndBlock is not null)
            foreach (var statement in body.EndBlock.Statements) yield return statement;
        if (includeClean && GetCleanBlock(body) is { } clean)
            foreach (var statement in clean.Statements) yield return statement;
    }

    private PowerShellBoundFunction? BindRuntimeFreePipelineLifecycleFunction(
        ParsedSourceDocument document,
        FunctionDefinitionAst function,
        PowerShellSymbolId functionSymbol,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        string? targetFramework,
        PowerShellCompilationCapability capabilities,
        Type? declaredOutputType,
        string declaredOutputTypeName,
        Dictionary<string, PowerShellSemanticSymbolBinding> symbols,
        PowerShellBoundParameter[] sourceParameters,
        ParameterAst pipelineParameter,
        bool signatureReturnsCollection,
        bool supportsOutputLimit,
        bool streamsSuccessOutput,
        int functionDiagnosticStart,
        PowerShellOutputTypeDeclaration[] outputTypeDeclarations)
    {
        var sourceParameter = sourceParameters.Single(parameter => parameter.Symbol.Name.Equals(
            pipelineParameter.Name.VariablePath.UserPath,
            StringComparison.OrdinalIgnoreCase));
        var arrayType = sourceParameter.Type.ClrType.MakeArrayType();
        var requiresNonNullCollection = PowerShellRuntimeFreePipelineLifecyclePolicy.RequiresNonNullCollection(
            sourceParameter.Type.ClrType, capabilities);
        var locals = DeclareLocals(document, function, symbols, functions, capabilities, targetFramework, _commandResolver)
            .Append(new PowerShellBoundLocal(sourceParameter.Symbol, sourceParameter.Type))
            .OrderBy(static local => local.Symbol.StableKey, StringComparer.Ordinal)
            .ToArray();
        var collectionName = CreatePipelineCollectionName(symbols, pipelineParameter.Extent.StartOffset);
        var collectionSymbol = new PowerShellSymbolId(
            PowerShellSymbolKind.Parameter,
            document.DocumentId,
            collectionName,
            sourceParameter.Symbol.Declaration,
            function.Name + "/pipeline-input/" + sourceParameter.Symbol.Name);
        var collectionType = new PowerShellTypeFact(
            arrayType,
            PowerShellTypeFactProvenance.Inferred,
            "The typed lifecycle ABI supplies the complete stable input collection.");
        var collectionParameter = new PowerShellBoundParameter(
            collectionSymbol,
            collectionType,
            new PowerShellCompilationParameter(
                collectionName, arrayType.FullName ?? arrayType.Name, hasDefaultValue: false,
                isMandatory: requiresNonNullCollection, isSwitch: false, aliases: null,
                allowNull: !requiresNonNullCollection, validations: null, allowEmptyCollection: true));

        var semanticOutputType = declaredOutputType == typeof(void) ? null : declaredOutputType;
        var strict = capabilities == PowerShellCompilationCapabilities.TypedExecutable;
        var composition = functions[function.Name].PipelineLifecycleSupportsComposition;
        var cleanSyntax = GetCleanBlock(function.Body);
        if (cleanSyntax is not null && _semanticProfile.Family != PowerShellCompilationSemanticHostFamily.PowerShell7)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2929", "Lifecycle clean is available only under the PowerShell 7 semantic profile.",
                PowerShellSourceParser.GetSpan(document, cleanSyntax.Extent)));
            return null;
        }
        var cleanSymbols = CloneSymbols(symbols);
        var begin = BindLifecycleBlock(document, function.Body.BeginBlock!, symbols, functions, diagnostics, terminalLast: false, allowTopLevelSuccessOutput: strict, semanticOutputType, targetFramework, capabilities);
        var processBaselineSymbols = CloneSymbols(symbols);
        var processSymbols = CloneSymbols(processBaselineSymbols);
        PrepareLoopFlowState(processSymbols, functions, capabilities, function.Body.ProcessBlock);
        var process = BindLifecycleBlock(document, function.Body.ProcessBlock!, processSymbols, functions, diagnostics, terminalLast: false, allowTopLevelSuccessOutput: true, semanticOutputType, targetFramework, capabilities);
        MergeSymbolValueStates(symbols, processBaselineSymbols, processSymbols);
        var end = function.Body.EndBlock is null
            ? new PowerShellBoundBlock(PowerShellSourceParser.GetSpan(document, function.Body.Extent), Array.Empty<PowerShellBoundStatement>())
            : BindLifecycleBlock(document, function.Body.EndBlock, symbols, functions, diagnostics, terminalLast: true, allowTopLevelSuccessOutput: false, successOutputType: null, targetFramework, capabilities);
        PowerShellBoundBlock? clean = null;
        if (cleanSyntax is not null)
        {
            MergeSymbolValueStates(cleanSymbols, cleanSymbols, symbols);
            clean = BindLifecycleBlock(document, cleanSyntax, cleanSymbols, functions, diagnostics, terminalLast: false,
                allowTopLevelSuccessOutput: true, successOutputType: null, targetFramework, capabilities);
            if (clean is null) return null;
            if (strict && !IsStrictCleanupClosed(clean))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2921",
                    "Strict pipeline clean requires total statements or an exhaustive local catch for fallible cleanup.",
                    clean.Span));
                return null;
            }
            clean = DiscardLifecycleBlockOutput(clean);
        }
        if (begin is null || process is null || end is null || diagnostics.Count > functionDiagnosticStart)
            return null;
        var refinedTypes = symbols.Values.ToDictionary(static binding => binding.Symbol.StableKey, static binding => binding.Type, StringComparer.Ordinal);
        locals = locals.Select(local => new PowerShellBoundLocal(local.Symbol, refinedTypes[local.Symbol.StableKey])).ToArray();
        if (sourceParameter.Type.ClrType == typeof(string))
        {
            var rawRecord = new PowerShellBoundVariableExpression(
                sourceParameter.Symbol.Declaration, sourceParameter.Symbol, sourceParameter.Type, PowerShellValueState.Unknown);
            var normalize = new PowerShellBoundAssignmentStatement(
                rawRecord.Span, sourceParameter.Symbol, PowerShellConversionSemanticBinder.BindClosedStringConversion(rawRecord)!);
            process = new PowerShellBoundBlock(process.Span, new PowerShellBoundStatement[] { normalize }.Concat(process.Statements).ToArray());
        }
        if ((supportsOutputLimit || composition) && new[] { begin, process, end }.Any(block =>
                PowerShellSemanticAnalyzer.EnumerateStatements(block, descendIntoCaptures: false).OfType<PowerShellBoundTryStatement>()
                    .Any(attempt => attempt.FinallyBlock is { } cleanup && GetSuccessOutputs(cleanup).Length != 0)))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2928", "Strict lifecycle early stopping cannot transfer control from finally output.", begin.Span));
            return null;
        }
        var beginOutputs = GetSuccessOutputs(begin);
        var processOutputs = strict ? beginOutputs.Concat(GetSuccessOutputs(process)).ToArray() : GetSuccessOutputs(process);
        if (!strict && beginOutputs.Length != 0 ||
            beginOutputs.Any(static output => output is PowerShellBoundInvocationExpression) ||
            !HasOneDefiniteTerminalSuccessOutput(end) && (end.Statements.Length != 0 || processOutputs.Length == 0))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2925",
                "Runtime-free lifecycle lowering requires a bounded begin/process success stream and an empty end or one terminal end-block record. Nested command output from begin is unsupported.",
                PowerShellSourceParser.GetSpan(document, function.Body.Extent)));
            return null;
        }

        var returnsCollection = processOutputs.Length > 0;
        Type? outputElementType = null;
        if ((!composition && returnsCollection != signatureReturnsCollection) ||
            (returnsCollection || composition) &&
            !TryGetLifecycleOutputElementType(processOutputs, end, semanticOutputType, composition, out outputElementType))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2928",
                "Runtime-free process output requires a statically inferred homogeneous stable-scalar contract that matches the terminal end-block output type.",
                PowerShellSourceParser.GetSpan(document, function.Body.ProcessBlock!.Extent)));
            return null;
        }

        var collection = new PowerShellBoundVariableExpression(
            sourceParameter.Symbol.Declaration,
            collectionSymbol,
            collectionType,
            PowerShellValueState.Known);
        if (!PowerShellPipelineNullInputSemanticPolicy.TryBindElement(
                sourceParameter.Type.ClrType,
                sourceParameter.Symbol.Declaration,
                out var nullCollectionElement) && !requiresNonNullCollection)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2927",
                $"A null pipeline collection cannot be converted to lifecycle parameter type '{sourceParameter.Type.ClrType.FullName}' without PowerShell parameter-binding error semantics.",
                sourceParameter.Symbol.Declaration));
            return null;
        }
        var lifecycleLoop = new PowerShellBoundForEachStatement(
            PowerShellSourceParser.GetSpan(document, function.Body.ProcessBlock!.Extent),
            sourceParameter.Symbol,
            sourceParameter.Type.ClrType,
            collection,
            PowerShellForEachEnumerationKind.TypedArray,
            process,
            declareVariable: true,
            nullCollectionElement,
            checkHostInterrupts: PowerShellLoopInterruptContract.IsAvailable(capabilities));
        var limitParameter = supportsOutputLimit
            ? CreateLifecycleLimitParameter(document, function, symbols, pipelineParameter, sourceParameter) : null;
        PowerShellBoundBlock body;
        var needsLiveOutput = supportsOutputLimit || composition && processOutputs.Any(static output => output.ValueState == PowerShellValueState.Null) ||
            (streamsSuccessOutput || composition) &&
            (beginOutputs.Length != 0 || new[] { begin, process, end }.Any(LifecycleHasObservableFailure) ||
             clean is not null && LifecycleHasObservableFailure(clean));
        if (returnsCollection && needsLiveOutput)
        {
            var limit = limitParameter is null || composition ? null : new PowerShellBoundVariableExpression(
                limitParameter.Symbol.Declaration, limitParameter.Symbol, limitParameter.Type, PowerShellValueState.Known);
            var outputName = CreatePipelineOutputName(symbols, pipelineParameter.Extent.StartOffset);
            var outputSymbol = new PowerShellSymbolId(PowerShellSymbolKind.Local, document.DocumentId,
                outputName, begin.Span, function.Name + "/pipeline-record");
            var outputType = StrictPipelineType(outputElementType!);
            if (limit is not null && !PowerShellClrTypeSemantics.IsNonNullableValueType(outputType.ClrType))
                locals = locals.Append(new PowerShellBoundLocal(outputSymbol, outputType)).ToArray();
            var output = new PowerShellBoundVariableExpression(begin.Span, outputSymbol, outputType, PowerShellValueState.Unknown);
            var liveBegin = RewriteLiveLifecycleOutputs(begin, output, limit);
            var liveProcess = RewriteLiveLifecycleOutputs(process, output, limit);
            var liveEnd = RewriteLiveLifecycleOutputs(end, output, limit);
            var liveLoop = new PowerShellBoundForEachStatement(lifecycleLoop.Span, lifecycleLoop.Variable,
                lifecycleLoop.ElementType, lifecycleLoop.Collection, lifecycleLoop.EnumerationKind, liveProcess,
                lifecycleLoop.DeclareVariable, lifecycleLoop.NullCollectionElement, lifecycleLoop.CheckHostInterrupts);
            body = new PowerShellBoundBlock(PowerShellSourceParser.GetSpan(document, function.Body.Extent),
                liveBegin.Statements.Append(liveLoop).Concat(liveEnd.Statements).ToArray());
        }
        else if (returnsCollection)
        {
            var outputType = outputElementType!;
            var outputListType = typeof(List<>).MakeGenericType(outputType);
            var outputName = CreatePipelineOutputName(symbols, pipelineParameter.Extent.StartOffset);
            var outputSymbol = new PowerShellSymbolId(
                PowerShellSymbolKind.Local,
                document.DocumentId,
                outputName,
                PowerShellSourceParser.GetSpan(document, function.Body.ProcessBlock!.Extent),
                function.Name + "/compiler/pipeline-output");
            var outputListFact = new PowerShellTypeFact(
                outputListType,
                PowerShellTypeFactProvenance.Inferred,
                "The compiler materializes ordered lifecycle success output in one typed collector.");
            locals = locals.Append(new PowerShellBoundLocal(outputSymbol, outputListFact))
                .OrderBy(static local => local.Symbol.StableKey, StringComparer.Ordinal)
                .ToArray();
            var outputVariable = new PowerShellBoundVariableExpression(outputSymbol.Declaration, outputSymbol, outputListFact, PowerShellValueState.Known);
            var createOutput = new PowerShellBoundAssignmentStatement(
                outputSymbol.Declaration,
                outputSymbol,
                new PowerShellBoundClrInvocationExpression(
                    outputSymbol.Declaration,
                    outputListType,
                    ".ctor",
                    PowerShellClrInvocationKind.Constructor,
                    receiver: null,
                    PowerShellClrReceiverBehavior.None,
                    Array.Empty<PowerShellBoundExpression>(),
                    Type.EmptyTypes,
                    outputListFact));
            var returnOutput = new PowerShellBoundReturnStatement(end.Span, LifecycleOutputArray(outputVariable, outputType));
            var collectedBegin = strict ? RewriteLifecycleOutputs(begin, outputVariable, outputListType, outputType) : begin;
            var collectedProcess = RewriteLifecycleOutputs(process, outputVariable, outputListType, outputType);
            lifecycleLoop = new PowerShellBoundForEachStatement(
                lifecycleLoop.Span,
                lifecycleLoop.Variable,
                lifecycleLoop.ElementType,
                lifecycleLoop.Collection,
                lifecycleLoop.EnumerationKind,
                collectedProcess,
                lifecycleLoop.DeclareVariable,
                lifecycleLoop.NullCollectionElement,
                lifecycleLoop.CheckHostInterrupts);
            var collectedEnd = RewriteLifecycleOutputs(end, outputVariable, outputListType, outputType);
            body = new PowerShellBoundBlock(
                PowerShellSourceParser.GetSpan(document, function.Body.Extent),
                new PowerShellBoundStatement[] { createOutput }
                    .Concat(collectedBegin.Statements)
                    .Concat(new PowerShellBoundStatement[] { lifecycleLoop })
                    .Concat(collectedEnd.Statements)
                    .Append(returnOutput)
                    .ToArray());
        }
        else
        {
            body = new PowerShellBoundBlock(
                PowerShellSourceParser.GetSpan(document, function.Body.Extent),
                begin.Statements.Concat(new PowerShellBoundStatement[] { lifecycleLoop }).Concat(end.Statements).ToArray());
        }
        if (clean is not null)
            body = new PowerShellBoundBlock(body.Span, new PowerShellBoundStatement[]
            {
                new PowerShellBoundTryStatement(body.Span, body, Array.Empty<PowerShellBoundCatchClause>(),
                    clean)
            });
        var lifecycleParameters = sourceParameters.Select(parameter => parameter.Symbol.Equals(sourceParameter.Symbol)
            ? collectionParameter : parameter).ToArray();
        if (limitParameter is not null) lifecycleParameters = lifecycleParameters.Append(limitParameter).ToArray();
        if (composition)
        {
            var name = CreatePipelineCollectionName(symbols, pipelineParameter.Extent.EndOffset);
            var symbol = new PowerShellSymbolId(PowerShellSymbolKind.Parameter, document.DocumentId, name,
                sourceParameter.Symbol.Declaration, function.Name + "/pipeline-stage");
            var type = typeof(Generated.Runtime.PowerShellStrictPipelineStage<>).MakeGenericType(sourceParameter.Type.ClrType);
            lifecycleParameters = lifecycleParameters.Append(new PowerShellBoundParameter(symbol, StrictPipelineType(type),
                new PowerShellCompilationParameter(name, type.FullName!, false, false, false, null, true, null))).ToArray();
        }
        var scopeSymbols = lifecycleParameters.Select(static parameter => parameter.Symbol)
            .Concat(locals.Select(static local => local.Symbol))
            .OrderBy(static symbol => symbol.StableKey, StringComparer.Ordinal)
            .ToArray();
        return new PowerShellBoundFunction(
            functionSymbol,
            lifecycleParameters,
            locals,
            new PowerShellLexicalScope(functionSymbol, scopeSymbols),
            PowerShellCommentHelpBinder.Bind(function, functionSymbol),
            PowerShellAdvancedFunctionPolicy.GetAliases(function),
            PowerShellAdvancedFunctionPolicy.GetBodyBinding(function.Body),
            declaredOutputType,
            declaredOutputTypeName,
            body,
            PowerShellTypeFact.Unknown,
            PowerShellOutputCardinality.Unknown,
            PowerShellSemanticEffect.None,
            PowerShellRequiredCapability.None,
            PowerShellExecutionDisposition.Typed,
            outputTypeDeclarations: outputTypeDeclarations);
    }

    private PowerShellBoundBlock? BindLifecycleBlock(
        ParsedSourceDocument document,
        NamedBlockAst block,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        bool terminalLast,
        bool allowTopLevelSuccessOutput,
        Type? successOutputType,
        string? targetFramework,
        PowerShellCompilationCapability capabilities)
    {
        if (RejectUnrepresentedTraps(document, block.Traps, diagnostics)) return null;
        var statements = new List<PowerShellBoundStatement>();
        for (var index = 0; index < block.Statements.Count; index++)
        {
            var syntax = block.Statements[index];
            var diagnosticCount = diagnostics.Count;
            var bound = BindStatement(
                document,
                syntax,
                symbols,
                functions,
                diagnostics,
                terminalLast && index == block.Statements.Count - 1,
                targetFramework,
                capabilities,
                allowTopLevelSuccessOutput,
                successOutputType);
            if (bound is null)
            {
                if (diagnostics.Count == diagnosticCount)
                    diagnostics.Add(new PowerShellSemanticDiagnostic(
                        "PSB2921",
                        $"Pipeline lifecycle statement '{syntax.GetType().Name}' is outside the bounded runtime-free begin/process/end contract.",
                        PowerShellSourceParser.GetSpan(document, syntax.Extent)));
                return null;
            }
            statements.Add(bound);
        }
        return new PowerShellBoundBlock(PowerShellSourceParser.GetSpan(document, block.Extent), statements.ToArray());
    }

    private PowerShellBoundInvocationExpression? BindRuntimeFreePipelineLifecycleInvocation(
        ParsedSourceDocument document,
        PipelineAst pipeline,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        string? targetFramework,
        PowerShellCompilationCapability capabilities)
    {
        if (!TryGetRuntimeFreePipelineLifecycleSelection(pipeline, functions, capabilities, out var limit) ||
            pipeline.PipelineElements[0] is not CommandExpressionAst inputCommand ||
            pipeline.PipelineElements[1] is not CommandAst targetCommand ||
            !TryGetLocalFunction(targetCommand, functions, out var signature) ||
            !signature.IsPipelineLifecycle)
            return null;
        if (inputCommand.Redirections.Count != 0 || targetCommand.Redirections.Count != 0)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2926",
                "Runtime-free pipeline lifecycle invocation does not support stream redirection.",
                PowerShellSourceParser.GetSpan(document, pipeline.Extent)));
            return null;
        }
        var parameter = signature.Parameters[signature.PipelineLifecycleParameterIndex];
        var arrayType = parameter.Type.MakeArrayType();
        var input = BindExpression(
            document,
            inputCommand.Expression,
            symbols,
            functions,
            diagnostics,
            arrayType,
            targetFramework,
            capabilities);
        if (input is not null && PowerShellModuleStateOriginPolicy.IsDerived(input))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2927",
                $"Runtime-free lifecycle invocation of '{signature.Symbol.Name}' cannot carry input derived from live Hybrid module state across a call boundary.",
                input.Span));
            return null;
        }
        if (input is not PowerShellBoundArrayExpression and not PowerShellBoundVariableExpression ||
            input.Type.ClrType != arrayType)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2922",
                $"Runtime-free lifecycle invocation of '{signature.Symbol.Name}' requires one statically typed '{arrayType.FullName}' array expression or variable.",
                PowerShellSourceParser.GetSpan(document, pipeline.Extent)));
            return null;
        }
        if (signature.PipelineLifecycleRequiresNonNullInput && input is not PowerShellBoundArrayExpression)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2927",
                $"The CLR collection argument for '{signature.Symbol.Name}' rejects null. An authored pipeline call requires a proven non-null array so PowerShell binding-error continuation cannot be changed.",
                input.Span));
            return null;
        }
        if (limit > 0 && input is PowerShellBoundArrayExpression inline &&
            !inline.Elements.All(static item => IsTotalStrictStageExpression(item, null)))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2922",
                "Strict lifecycle early stopping requires effect-free fixed input records.", input.Span));
            return null;
        }
        return PowerShellLocalCallSemanticBinder.Bind(
            document, targetCommand, signature,
            (syntax, expectedType) => BindExpression(document, syntax, symbols, functions, diagnostics, expectedType, targetFramework, capabilities),
            targetFramework, capabilities, diagnostics, input, limit);
    }

    private bool TryGetRuntimeFreePipelineLifecycleSelection(
        PipelineAst pipeline,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        PowerShellCompilationCapability capabilities,
        out int limit)
    {
        limit = 0;
        if (pipeline.PipelineElements.Count is < 2 or > 3 ||
            pipeline.PipelineElements[1] is not CommandAst command ||
            !TryGetLocalFunction(command, functions, out var signature) ||
            !signature.IsPipelineLifecycle)
            return false;
        if (pipeline.PipelineElements.Count == 2) return true;
        return capabilities == PowerShellCompilationCapabilities.TypedExecutable && signature.PipelineLifecycleReturnsCollection &&
            !PowerShellCommandRegionSemanticBinder.IsBackground(pipeline) &&
            TryGetLifecycleLimit(pipeline.PipelineElements[2], functions, out limit);
    }

    private bool TryGetLifecycleLimit(CommandBaseAst stage, IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        out int limit)
    {
        limit = 0;
        if (stage is not CommandAst { InvocationOperator: TokenKind.Unknown, Redirections.Count: 0 } selection ||
            ResolveCommand(selection, functions, PowerShellCompilationCapabilities.TypedExecutable) is not
                { IsProvider: true, Contract.ProviderId: "powerforge.command.projection.select-object" } ||
            selection.CommandElements.Count != 3 ||
            selection.CommandElements[1] is not CommandParameterAst { Argument: null } first ||
            !first.ParameterName.Equals("First", StringComparison.OrdinalIgnoreCase) ||
            selection.CommandElements[2] is not ConstantExpressionAst { Value: int count } || count <= 0)
            return false;
        limit = count;
        return true;
    }

    private static bool HasOneDefiniteTerminalSuccessOutput(PowerShellBoundBlock block)
    {
        if (block.Statements.Length == 0) return false;
        var terminal = block.Statements[block.Statements.Length - 1];
        var terminalOutput = PowerShellSemanticAnalyzer.GetSuccessOutputExpression(terminal);
        return terminalOutput is not null &&
               terminalOutput.Type.ClrType != typeof(object) &&
               terminalOutput.Type.ClrType != typeof(void) &&
               PowerShellSemanticAnalyzer.EnumerateStatements(block)
                   .Count(statement => PowerShellSemanticAnalyzer.GetSuccessOutputExpression(statement) is not null) == 1;
    }

    private static PowerShellBoundExpression[] GetSuccessOutputs(PowerShellBoundBlock block)
        => PowerShellSemanticAnalyzer.EnumerateStatements(block, descendIntoCaptures: false)
            .Select(PowerShellSemanticAnalyzer.GetSuccessOutputExpression)
            .Where(static expression => expression is not null)
            .Cast<PowerShellBoundExpression>()
            .ToArray();

    private static bool LifecycleHasObservableFailure(PowerShellBoundBlock block)
        => PowerShellSemanticAnalyzer.EnumerateStatements(block).Any(statement =>
            statement is PowerShellBoundThrowStatement or PowerShellBoundTryStatement or PowerShellBoundStatementErrorBoundary ||
            statement is PowerShellBoundAssignmentStatement { Operation: not PowerShellBoundMutationOperator.Assign } ||
            PowerShellSemanticAnalyzer.EnumerateDirectExpressions(statement)
                .SelectMany(PowerShellSemanticAnalyzer.EnumerateExpressions)
                .Any(static expression => expression is PowerShellBoundClrInvocationExpression or PowerShellBoundInvocationExpression or
                    PowerShellBoundClrMemberExpression or PowerShellBoundConversionExpression or PowerShellBoundMutationExpression or
                    PowerShellBoundRuntimeStateExpression));

    private static bool IsStrictCleanupClosed(PowerShellBoundBlock block)
        => block.Statements.All(static statement => statement switch
        {
            // An escaping PowerShell cleanup error has a separate route from the
            // invocation's errors. A local catch keeps that route out of the CLR finally.
            PowerShellBoundTryStatement tried =>
                (tried.Catches.Any(static clause => clause.ExceptionTypes.Length == 0) ||
                 IsStrictCleanupClosed(tried.Body)) &&
                tried.Catches.All(static clause => IsStrictCleanupClosed(clause.Body)) &&
                (tried.FinallyBlock is null || IsStrictCleanupClosed(tried.FinallyBlock)),
            _ => PowerShellSemanticAnalyzer.GetSuccessOutputExpression(statement) is { } output &&
                IsTotalStrictStageExpression(output, current: null)
        });

    private static bool TryGetLifecycleOutputElementType(
        PowerShellBoundExpression[] processOutputs,
        PowerShellBoundBlock end,
        Type? declaredOutputType,
        bool allowsExplicitNull,
        out Type? outputElementType)
    {
        outputElementType = null;
        var endOutput = GetSuccessOutputs(end);
        if (endOutput.Length > 1 || endOutput.Length == 0 && end.Statements.Length != 0) return false;
        var outputs = processOutputs.Concat(endOutput).ToArray();
        var hasExplicitNull = outputs.Any(static expression => expression.ValueState == PowerShellValueState.Null);
        var types = outputs.Where(expression => !allowsExplicitNull || expression.ValueState != PowerShellValueState.Null)
            .Select(static expression => expression.Type.ClrType)
            .Distinct()
            .ToArray();
        if (types.Length != 1 ||
            types[0] == typeof(object) ||
            types[0] == typeof(void) ||
            types[0].IsArray ||
            !PowerShellStableScalarTypePolicy.IsSupported(types[0]) ||
            allowsExplicitNull && hasExplicitNull && PowerShellClrTypeSemantics.IsNonNullableValueType(types[0]) ||
            declaredOutputType is not null && declaredOutputType != types[0])
            return false;
        outputElementType = types[0];
        return true;
    }

    private static PowerShellBoundBlock RewriteLifecycleOutputs(
        PowerShellBoundBlock block,
        PowerShellBoundExpression outputVariable,
        Type outputListType,
        Type outputElementType)
        => new(
            block.Span,
            block.Statements.Select(statement => RewriteLifecycleOutput(
                statement,
                outputVariable,
                outputListType,
                outputElementType)).ToArray());

    private static PowerShellBoundStatement RewriteLifecycleOutput(
        PowerShellBoundStatement statement,
        PowerShellBoundExpression outputVariable,
        Type outputListType,
        Type outputElementType)
    {
        if (statement is PowerShellBoundOutputCaptureStatement) return statement;
        var expression = PowerShellSemanticAnalyzer.GetSuccessOutputExpression(statement);
        if (expression is null)
            return PowerShellBoundStatementRewriter.RewriteNestedBlocks(statement,
                nested => RewriteLifecycleOutputs(nested, outputVariable, outputListType, outputElementType));
        var add = new PowerShellBoundClrInvocationExpression(
            statement.Span,
            outputListType,
            nameof(List<int>.Add),
            PowerShellClrInvocationKind.InstanceMethod,
            outputVariable,
            PowerShellClrReceiverBehavior.None,
            new[] { expression },
            new[] { outputElementType },
            new PowerShellTypeFact(typeof(void), PowerShellTypeFactProvenance.Inferred, "List.Add is output-free."));
        return new PowerShellBoundExpressionStatement(statement.Span, add, emitsOutput: false);
    }

    private static PowerShellBoundExpression LifecycleOutputArray(PowerShellBoundExpression output, Type element)
        => new PowerShellBoundClrInvocationExpression(output.Span, output.Type.ClrType, nameof(List<int>.ToArray),
            PowerShellClrInvocationKind.InstanceMethod, output, PowerShellClrReceiverBehavior.None,
            Array.Empty<PowerShellBoundExpression>(), Type.EmptyTypes, StrictPipelineType(element.MakeArrayType()));

    private static PowerShellBoundBlock RewriteLiveLifecycleOutputs(PowerShellBoundBlock block,
        PowerShellBoundVariableExpression record, PowerShellBoundVariableExpression? limit)
    {
        var statements = new List<PowerShellBoundStatement>();
        foreach (var statement in block.Statements)
        {
            if (PowerShellSemanticAnalyzer.GetSuccessOutputExpression(statement) is not { } expression)
            {
                statements.Add(statement is PowerShellBoundOutputCaptureStatement ? statement :
                    PowerShellBoundStatementRewriter.RewriteNestedBlocks(statement,
                        nested => RewriteLiveLifecycleOutputs(nested, record, limit)));
                continue;
            }
            if (limit is null)
            {
                statements.Add(StrictSuccessOutput(expression));
                continue;
            }
            var span = statement.Span;
            var zero = StrictIntLiteral(span, 0);
            var boolType = StrictPipelineType(typeof(bool));
            var hasLimit = new PowerShellBoundBinaryExpression(span, PowerShellBoundBinaryOperator.GreaterThan, limit, zero, boolType);
            var limitReached = new PowerShellBoundBinaryExpression(span, PowerShellBoundBinaryOperator.Equal, limit, zero, boolType);
            var countRecord = StrictPipelineIf(span, hasLimit, new PowerShellBoundStatement[]
            {
                new PowerShellBoundAssignmentStatement(span, limit.Symbol, new PowerShellBoundBinaryExpression(
                    span, PowerShellBoundBinaryOperator.Subtract, limit, StrictIntLiteral(span, 1), limit.Type)),
                StrictPipelineIf(span, limitReached, new PowerShellBoundStatement[] { new PowerShellBoundReturnStatement(span, null) })
            });
            if (PowerShellClrTypeSemantics.IsNonNullableValueType(record.Type.ClrType))
            {
                statements.Add(StrictSuccessOutput(expression));
                statements.Add(countRecord);
            }
            else
            {
                statements.Add(new PowerShellBoundAssignmentStatement(span, record.Symbol, expression));
                var eligible = new PowerShellBoundBinaryExpression(span, PowerShellBoundBinaryOperator.LogicalOr, limitReached,
                    StrictPipelineRecordIsNotNull(span, record), boolType);
                statements.Add(StrictPipelineIf(span, eligible, new PowerShellBoundStatement[] { StrictSuccessOutput(record), countRecord }));
            }
        }
        return new PowerShellBoundBlock(block.Span, statements.ToArray());
    }

    private static PowerShellBoundParameter CreateLifecycleLimitParameter(ParsedSourceDocument document,
        FunctionDefinitionAst function, IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        ParameterAst syntax, PowerShellBoundParameter source)
    {
        var name = CreatePipelineOutputName(symbols, syntax.Extent.EndOffset);
        var symbol = new PowerShellSymbolId(PowerShellSymbolKind.Parameter, document.DocumentId, name, source.Symbol.Declaration,
            function.Name + "/pipeline-output-limit");
        return new PowerShellBoundParameter(symbol, StrictPipelineType(typeof(int)),
            new PowerShellCompilationParameter(name, typeof(int).FullName!, hasDefaultValue: false,
                isMandatory: false, isSwitch: false, aliases: null, allowNull: false, validations: null));
    }

    private static PowerShellBoundBlock DiscardLifecycleBlockOutput(PowerShellBoundBlock block)
        => new(block.Span, block.Statements.Select(static statement =>
            PowerShellSemanticAnalyzer.GetSuccessOutputExpression(statement) is { } output
                ? new PowerShellBoundExpressionStatement(statement.Span, output, emitsOutput: false, discardsSuccessOutput: true)
                : statement is PowerShellBoundOutputCaptureStatement ? statement :
                  PowerShellBoundStatementRewriter.RewriteNestedBlocks(statement, DiscardLifecycleBlockOutput)).ToArray());

    private static string CreatePipelineCollectionName(
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        int offset)
    {
        var root = "__pf_pipeline_input_" + offset.ToString(CultureInfo.InvariantCulture);
        var candidate = root;
        var used = symbols.Values
            .Select(static binding => PowerShellCSharpSymbolRenderer.Identifier(binding.Symbol.Name))
            .ToHashSet(StringComparer.Ordinal);
        var sequence = 0;
        while (used.Contains(PowerShellCSharpSymbolRenderer.Identifier(candidate)))
            candidate = root + "_" + (++sequence).ToString(CultureInfo.InvariantCulture);
        return candidate;
    }

    private static string CreatePipelineOutputName(
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        int offset)
    {
        var root = "__pf_pipeline_output_" + offset.ToString(CultureInfo.InvariantCulture);
        var candidate = root;
        var used = symbols.Values
            .Select(static binding => PowerShellCSharpSymbolRenderer.Identifier(binding.Symbol.Name))
            .ToHashSet(StringComparer.Ordinal);
        var sequence = 0;
        while (used.Contains(PowerShellCSharpSymbolRenderer.Identifier(candidate)))
            candidate = root + "_" + (++sequence).ToString(CultureInfo.InvariantCulture);
        return candidate;
    }
}
