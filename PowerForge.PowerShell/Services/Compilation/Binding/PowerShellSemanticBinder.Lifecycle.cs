using System.Globalization;
using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    private static IEnumerable<StatementAst> GetFunctionStatements(ScriptBlockAst body)
    {
        if (body.BeginBlock is not null)
            foreach (var statement in body.BeginBlock.Statements) yield return statement;
        if (body.ProcessBlock is not null)
            foreach (var statement in body.ProcessBlock.Statements) yield return statement;
        if (body.EndBlock is not null)
            foreach (var statement in body.EndBlock.Statements) yield return statement;
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
        Dictionary<string, PowerShellSemanticSymbolBinding> symbols,
        PowerShellBoundParameter[] sourceParameters,
        ParameterAst pipelineParameter,
        bool signatureReturnsCollection,
        int functionDiagnosticStart)
    {
        var sourceParameter = sourceParameters.Single(parameter => parameter.Symbol.Name.Equals(
            pipelineParameter.Name.VariablePath.UserPath,
            StringComparison.OrdinalIgnoreCase));
        var arrayType = sourceParameter.Type.ClrType.MakeArrayType();
        var locals = DeclareLocals(document, function, symbols, functions, capabilities)
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
            "The typed executable lifecycle ABI supplies the complete stable input collection.");
        var collectionParameter = new PowerShellBoundParameter(
            collectionSymbol,
            collectionType,
            new PowerShellCompilationParameter(collectionName, arrayType.FullName ?? arrayType.Name, hasDefaultValue: false));

        var semanticOutputType = declaredOutputType == typeof(void) ? null : declaredOutputType;
        var begin = BindLifecycleBlock(document, function.Body.BeginBlock!, symbols, functions, diagnostics, terminalLast: false, allowTopLevelSuccessOutput: false, successOutputType: null, targetFramework, capabilities);
        var processBaselineSymbols = CloneSymbols(symbols);
        var processSymbols = CloneSymbols(processBaselineSymbols);
        var process = BindLifecycleBlock(document, function.Body.ProcessBlock!, processSymbols, functions, diagnostics, terminalLast: false, allowTopLevelSuccessOutput: true, semanticOutputType, targetFramework, capabilities);
        MergeSymbolValueStates(symbols, processBaselineSymbols, processSymbols);
        var end = BindLifecycleBlock(document, function.Body.EndBlock!, symbols, functions, diagnostics, terminalLast: true, allowTopLevelSuccessOutput: false, successOutputType: null, targetFramework, capabilities);
        if (begin is null || process is null || end is null || diagnostics.Count > functionDiagnosticStart)
            return null;
        if (ContainsSuccessOutput(begin) || !HasOneDefiniteTerminalSuccessOutput(end))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2925",
                "Runtime-free pipeline lifecycle lowering requires an output-free begin block and exactly one terminal end-block success output.",
                PowerShellSourceParser.GetSpan(document, function.Body.Extent)));
            return null;
        }

        var processOutputs = GetSuccessOutputs(process);
        var returnsCollection = processOutputs.Length > 0;
        Type? outputElementType = null;
        if (returnsCollection != signatureReturnsCollection ||
            returnsCollection && !TryGetLifecycleOutputElementType(processOutputs, end, semanticOutputType, out outputElementType))
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
                out var nullCollectionElement))
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
            scalarString: false,
            process,
            declareVariable: true,
            nullCollectionElement);
        PowerShellBoundBlock body;
        if (returnsCollection)
        {
            var outputType = outputElementType!;
            var outputListType = typeof(List<>).MakeGenericType(outputType);
            var outputArrayType = outputType.MakeArrayType();
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
            var collectedProcess = RewriteLifecycleOutputs(process, outputVariable, outputListType, outputType);
            lifecycleLoop = new PowerShellBoundForEachStatement(
                lifecycleLoop.Span,
                lifecycleLoop.Variable,
                lifecycleLoop.ElementType,
                lifecycleLoop.Collection,
                lifecycleLoop.ScalarString,
                collectedProcess,
                lifecycleLoop.DeclareVariable,
                lifecycleLoop.NullCollectionElement,
                lifecycleLoop.SystemArray);
            var collectedEnd = RewriteLifecycleOutputs(end, outputVariable, outputListType, outputType);
            var outputArrayFact = new PowerShellTypeFact(
                outputArrayType,
                PowerShellTypeFactProvenance.Inferred,
                "The lifecycle ABI returns the ordered success stream as a typed array.");
            var returnOutput = new PowerShellBoundReturnStatement(
                end.Span,
                new PowerShellBoundClrInvocationExpression(
                    end.Span,
                    outputListType,
                    nameof(List<int>.ToArray),
                    PowerShellClrInvocationKind.InstanceMethod,
                    outputVariable,
                    PowerShellClrReceiverBehavior.None,
                    Array.Empty<PowerShellBoundExpression>(),
                    Type.EmptyTypes,
                    outputArrayFact));
            body = new PowerShellBoundBlock(
                PowerShellSourceParser.GetSpan(document, function.Body.Extent),
                new PowerShellBoundStatement[] { createOutput }
                    .Concat(begin.Statements)
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
        var scopeSymbols = new[] { collectionSymbol }
            .Concat(locals.Select(static local => local.Symbol))
            .OrderBy(static symbol => symbol.StableKey, StringComparer.Ordinal)
            .ToArray();
        return new PowerShellBoundFunction(
            functionSymbol,
            new[] { collectionParameter },
            locals,
            new PowerShellLexicalScope(functionSymbol, scopeSymbols),
            PowerShellCommentHelpBinder.Bind(function),
            PowerShellAdvancedFunctionPolicy.GetAliases(function),
            PowerShellAdvancedFunctionPolicy.GetBinding(function.Body.ParamBlock),
            declaredOutputType,
            body,
            PowerShellTypeFact.Unknown,
            PowerShellOutputCardinality.Unknown,
            PowerShellSemanticEffect.None,
            PowerShellRequiredCapability.None,
            PowerShellExecutionDisposition.Typed);
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
        if (pipeline.PipelineElements.Count != 2 ||
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
        if (input is not PowerShellBoundArrayExpression and not PowerShellBoundVariableExpression ||
            input.Type.ClrType != arrayType)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2922",
                $"Runtime-free lifecycle invocation of '{signature.Symbol.Name}' requires one statically typed '{arrayType.FullName}' array expression or variable.",
                PowerShellSourceParser.GetSpan(document, pipeline.Extent)));
            return null;
        }
        if (targetCommand.CommandElements.Count != 1)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2923",
                $"Runtime-free lifecycle invocation of '{signature.Symbol.Name}' binds its sole parameter from the pipeline and does not accept command arguments.",
                PowerShellSourceParser.GetSpan(document, targetCommand.Extent)));
            return null;
        }
        var invocationReturnType = signature.DeclaredReturnType is not null && signature.PipelineLifecycleReturnsCollection
            ? signature.DeclaredReturnType.MakeArrayType()
            : signature.DeclaredReturnType;
        var returnType = invocationReturnType is null
            ? PowerShellTypeFact.Unknown
            : new PowerShellTypeFact(
                invocationReturnType,
                PowerShellTypeFactProvenance.Explicit,
                signature.PipelineLifecycleReturnsCollection
                    ? $"Lifecycle function '{signature.Symbol.Name}' materializes ordered process/end success output."
                    : $"Lifecycle function '{signature.Symbol.Name}' declares one end-block success-output type.");
        return new PowerShellBoundInvocationExpression(
            PowerShellSourceParser.GetSpan(document, pipeline.Extent),
            signature.Symbol,
            new[] { input },
            returnType,
            new[] { 0 },
            new[] { parameter.Contract.Name });
    }

    private static bool IsRuntimeFreePipelineLifecycleInvocation(
        PipelineAst pipeline,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions)
        => pipeline.PipelineElements.Count == 2 &&
           pipeline.PipelineElements[1] is CommandAst command &&
           TryGetLocalFunction(command, functions, out var signature) &&
           signature.IsPipelineLifecycle;

    private static bool ContainsSuccessOutput(PowerShellBoundBlock block)
        => PowerShellSemanticAnalyzer.EnumerateStatements(block)
            .Any(statement => PowerShellSemanticAnalyzer.GetSuccessOutputExpression(statement) is not null);

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
        => PowerShellSemanticAnalyzer.EnumerateStatements(block)
            .Select(PowerShellSemanticAnalyzer.GetSuccessOutputExpression)
            .Where(static expression => expression is not null)
            .Cast<PowerShellBoundExpression>()
            .ToArray();

    private static bool TryGetLifecycleOutputElementType(
        PowerShellBoundExpression[] processOutputs,
        PowerShellBoundBlock end,
        Type? declaredOutputType,
        out Type? outputElementType)
    {
        outputElementType = null;
        var endOutput = GetSuccessOutputs(end);
        if (endOutput.Length != 1) return false;
        var types = processOutputs.Append(endOutput[0])
            .Select(static expression => expression.Type.ClrType)
            .Distinct()
            .ToArray();
        if (types.Length != 1 ||
            types[0] == typeof(object) ||
            types[0] == typeof(void) ||
            types[0].IsArray ||
            !PowerShellStableScalarTypePolicy.IsSupported(types[0]) ||
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
        if (statement is PowerShellBoundIfStatement conditional)
            return new PowerShellBoundIfStatement(
                conditional.Span,
                conditional.Clauses.Select(clause => new PowerShellBoundConditionalClause(
                    clause.Condition,
                    RewriteLifecycleOutputs(clause.Body, outputVariable, outputListType, outputElementType))).ToArray(),
                conditional.ElseBlock is null
                    ? null
                    : RewriteLifecycleOutputs(conditional.ElseBlock, outputVariable, outputListType, outputElementType));
        var expression = PowerShellSemanticAnalyzer.GetSuccessOutputExpression(statement);
        if (expression is null) return statement;
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
