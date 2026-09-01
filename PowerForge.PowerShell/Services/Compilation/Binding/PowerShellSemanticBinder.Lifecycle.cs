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

        var begin = BindLifecycleBlock(document, function.Body.BeginBlock!, symbols, functions, diagnostics, terminalLast: false, targetFramework, capabilities);
        var process = BindLifecycleBlock(document, function.Body.ProcessBlock!, symbols, functions, diagnostics, terminalLast: false, targetFramework, capabilities);
        var end = BindLifecycleBlock(document, function.Body.EndBlock!, symbols, functions, diagnostics, terminalLast: true, targetFramework, capabilities);
        if (begin is null || process is null || end is null || diagnostics.Count > functionDiagnosticStart)
            return null;
        if (ContainsSuccessOutput(begin) || ContainsSuccessOutput(process) || !HasOneDefiniteTerminalSuccessOutput(end))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2925",
                "Runtime-free pipeline lifecycle lowering requires output-free begin/process blocks and exactly one terminal end-block success output.",
                PowerShellSourceParser.GetSpan(document, function.Body.Extent)));
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
        var body = new PowerShellBoundBlock(
            PowerShellSourceParser.GetSpan(document, function.Body.Extent),
            begin.Statements.Concat(new PowerShellBoundStatement[] { lifecycleLoop }).Concat(end.Statements).ToArray());
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
                capabilities);
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
        var returnType = signature.DeclaredReturnType is null
            ? PowerShellTypeFact.Unknown
            : new PowerShellTypeFact(
                signature.DeclaredReturnType,
                PowerShellTypeFactProvenance.Explicit,
                $"Lifecycle function '{signature.Symbol.Name}' declares one end-block success-output type.");
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
}
