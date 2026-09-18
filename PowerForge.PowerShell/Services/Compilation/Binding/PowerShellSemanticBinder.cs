using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Converts parser-owned PowerShell syntax into the compiler's neutral bound representation.
/// Parser objects are consumed here and never become part of a bound node.
/// </summary>
internal sealed partial class PowerShellSemanticBinder
{
    private readonly PowerShellCommandSemanticRegistry _commandRegistry;
    private readonly PowerShellCommandSemanticResolver _commandResolver;
    private readonly PowerShellCompilationSemanticOracleProfile _semanticProfile;
    private PowerShellRuntimeFreeModuleDefinition? _runtimeFreeModule;

    internal PowerShellSemanticBinder()
        : this(PowerShellCommandSemanticRegistry.Default, PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId)
    {
    }

    internal PowerShellSemanticBinder(PowerShellCommandSemanticRegistry commandRegistry)
        : this(commandRegistry, PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId)
    {
    }

    internal PowerShellSemanticBinder(string semanticProfileId)
        : this(PowerShellCommandSemanticRegistry.Default, semanticProfileId)
    {
    }

    internal PowerShellSemanticBinder(PowerShellCommandSemanticRegistry commandRegistry, string semanticProfileId)
    {
        _commandRegistry = commandRegistry ?? throw new ArgumentNullException(nameof(commandRegistry));
        _commandResolver = new PowerShellCommandSemanticResolver(_commandRegistry);
        _semanticProfile = PowerShellCompilationSemanticOracleCatalog.Get(semanticProfileId);
    }

    private PowerShellBoundFunction? BindFunction(
        ParsedSourceDocument document,
        FunctionDefinitionAst function,
        PowerShellSymbolId functionSymbol,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        string? targetFramework,
        PowerShellCompilationCapability capabilities,
        IDictionary<string, PowerShellBoundRegionCandidate>? regionCandidates = null,
        IDictionary<string, PowerShellBoundRegionOpportunity>? regionOpportunities = null,
        bool requiresNativeInvocation = false)
    {
        var functionDiagnosticStart = diagnostics.Count;
        ClearFunctionRegionEvidence(regionCandidates, regionOpportunities, document.Path, functionSymbol.Name);
        var nativeFunctionBinding = PowerShellNativeFunctionBindingPolicy.Select(function, capabilities, requiresNativeInvocation);
        if (new[] { function.Body.BeginBlock, function.Body.ProcessBlock, function.Body.EndBlock, GetCleanBlock(function.Body) }
            .FirstOrDefault(block => block?.Traps is { Count: > 0 }) is { } trappedBlock)
        {
            RejectUnrepresentedTraps(document, trappedBlock.Traps, diagnostics);
            if (nativeFunctionBinding is not null) return null;
        }
        if (nativeFunctionBinding is null) capabilities &= ~PowerShellCompilationCapability.NativeFunctionBinding;
        // Command redirections already have target-specific binding diagnostics. Background
        // pipelines and expression redirections must be stopped before ordinary unwrapping.
        if (nativeFunctionBinding is null && PowerShellNativeFunctionBindingPolicy.FindNativePipelineOperator(function, includeCommandRedirections: false) is { } nativeOperator)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2945",
                "Background pipelines and stream redirection require native invocation binding; this target or function lifecycle does not provide that contract.",
                PowerShellSourceParser.GetSpan(document, nativeOperator.Extent)));
            return null;
        }
        if (!PowerShellOutputTypeSemanticPolicy.TryResolve(
                function.Body,
                targetFramework,
                capabilities,
                out var outputTypeContract,
                out var outputTypeErrorNode,
                out var outputTypeError))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB1201",
                outputTypeError!,
                PowerShellSourceParser.GetSpan(document, outputTypeErrorNode!.Extent)));
            return null;
        }
        var symbols = new Dictionary<string, PowerShellSemanticSymbolBinding>(StringComparer.OrdinalIgnoreCase);
        var hasRuntimeFreeLifecycle = PowerShellRuntimeFreePipelineLifecyclePolicy.TryGetPipelineParameter(
            function.Body,
            capabilities,
            out var pipelineParameter,
            out _);
        var bindingCapabilities = hasRuntimeFreeLifecycle
            ? capabilities | PowerShellCompilationCapability.PipelineParameterBinding
            : capabilities;
        var parameters = BindParameters(document, function, symbols, diagnostics, targetFramework, bindingCapabilities);
        if (parameters is null) return null;
        if (_runtimeFreeModule is not null)
            foreach (var field in _runtimeFreeModule.Fields)
                symbols.Add("script:" + field.Symbol.Name, new PowerShellSemanticSymbolBinding(field.Symbol, field.Type));
        if (nativeFunctionBinding is not null && PowerShellRuntimeFreePipelineLifecyclePolicy.HasNamedLifecycle(function.Body))
            return BindNativeLifecycleFunction(document, function, functionSymbol, functions, diagnostics, targetFramework,
                capabilities, symbols, parameters, nativeFunctionBinding, outputTypeContract.SemanticType,
                outputTypeContract.MetadataTypeName, functionDiagnosticStart);
        if (hasRuntimeFreeLifecycle)
            return BindRuntimeFreePipelineLifecycleFunction(
                document,
                function,
                functionSymbol,
                functions,
                diagnostics,
                targetFramework,
                bindingCapabilities,
                outputTypeContract.SemanticType,
                outputTypeContract.MetadataTypeName,
                symbols,
                parameters,
                pipelineParameter,
                functions[function.Name].PipelineLifecycleReturnsCollection,
                functionDiagnosticStart);
        var authoredStatements = function.Body.EndBlock?.Statements.ToArray() ?? Array.Empty<StatementAst>();
        var localFunctionNames = functions.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var runtimeTailStart = nativeFunctionBinding is null && capabilities.HasFlag(PowerShellCompilationCapability.PowerShellStreams)
            ? PowerShellCommandIslandPolicy.FindRuntimeTailStart(authoredStatements, function.Body, localFunctionNames, capabilities, _commandResolver)
            : -1;
        var runtimeTailOffset = runtimeTailStart >= 0 ? authoredStatements[runtimeTailStart].Extent.StartOffset : (int?)null;
        var locals = DeclareLocals(document, function, symbols, functions, capabilities, _commandResolver, runtimeTailOffset);
        var parametersByName = parameters.ToDictionary(static parameter => parameter.Symbol.Name, StringComparer.OrdinalIgnoreCase);

        var statements = new List<PowerShellBoundStatement>();
        var statementBindings = new List<PowerShellBoundStatementBinding>();
        var bodyIsValid = true;
        var lastFailedStatementIndex = -1;
        for (var index = 0; index < authoredStatements.Length; index++)
        {
            var authoredStatementIndex = index;
            var statement = authoredStatements[index];
            if (PowerShellObjectSemanticBinder.TryBindAddMember(
                    document,
                    statement,
                    symbols,
                    (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                    capabilities,
                    _commandResolver,
                    localFunctionNames,
                    diagnostics,
                    out var objectMutation))
            {
                if (objectMutation is null) bodyIsValid = false;
                else
                {
                    statements.Add(objectMutation);
                    statementBindings.Add(new PowerShellBoundStatementBinding(authoredStatementIndex, authoredStatementIndex, objectMutation));
                }
                if (objectMutation is null) lastFailedStatementIndex = authoredStatementIndex;
                continue;
            }
            var isModuleStateAssignment = statement is AssignmentStatementAst moduleStateAssignment &&
                                          PowerShellRuntimeStateIntrinsicPolicy.TryGetModuleVariableAssignmentName(
                                              moduleStateAssignment,
                                              capabilities,
                                              out _);
            if (!isModuleStateAssignment &&
                capabilities.HasFlag(PowerShellCompilationCapability.PowerShellStreams) &&
                PowerShellHostedStatementBinder.TryBind(
                    document,
                    authoredStatements,
                    function.Body,
                    localFunctionNames,
                    symbols,
                    parametersByName,
                    runtimeTailStart,
                    _commandResolver,
                    capabilities,
                    ref index,
                    out var hosted))
            {
                statements.Add(hosted!);
                statementBindings.Add(new PowerShellBoundStatementBinding(authoredStatementIndex, index, hosted!));
                continue;
            }
            var diagnosticCount = diagnostics.Count;
            var bound = BindStatement(document, statement, symbols, functions, diagnostics, index == authoredStatements.Length - 1, targetFramework, capabilities);
            if (bound is null)
            {
                if (diagnostics.Count == diagnosticCount)
                {
                    diagnostics.Add(new PowerShellSemanticDiagnostic(
                        "PSB2001",
                        $"Statement '{statement.GetType().Name}' is not yet represented by the bound pipeline.",
                        PowerShellSourceParser.GetSpan(document, statement.Extent)));
                }
                bodyIsValid = false;
                lastFailedStatementIndex = authoredStatementIndex;
                continue;
            }
            statements.Add(bound);
            statementBindings.Add(new PowerShellBoundStatementBinding(authoredStatementIndex, authoredStatementIndex, bound));
        }
        if (functions[function.Name].ClosedCollectionFactory is not null && bodyIsValid &&
            !capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding))
            PowerShellClosedLocalCollectionFactoryPolicy.NormalizeBoundReturn(statements, statementBindings);
        var refinedTypes = symbols.Values.ToDictionary(static binding => binding.Symbol.StableKey, static binding => binding.Type, StringComparer.Ordinal);
        locals = locals.Select(local => new PowerShellBoundLocal(local.Symbol, refinedTypes[local.Symbol.StableKey])).ToArray();
        // A fully bound body may still need its authored PowerShell header after cmdlet shaping.
        // Keep the ordinary terminal-region candidate so that final shaping can retain that header
        // while delegating the proven body through the same region ABI.
        if (regionCandidates is not null && bodyIsValid &&
            diagnostics.Skip(functionDiagnosticStart).All(static diagnostic =>
                diagnostic.Code == PowerShellNativeStringPipelineBindingPolicy.DiagnosticCode) &&
            PowerShellBoundRegionCandidateSelector.TryCreate(
                document, function, functionSymbol, parameters, locals, authoredStatements,
                statementBindings, -1, out var completeBodyCandidate))
            regionCandidates[completeBodyCandidate.RegionId] = completeBodyCandidate;
        // Preserve canonical runs even when binding succeeds: semantic analysis, call-graph
        // closure, and artifact shaping can still retain this function later in the pipeline.
        if (regionOpportunities is not null)
            AddRegionOpportunities(
                regionOpportunities,
                document,
                function,
                functionSymbol,
                parameters,
                symbols,
                locals,
                authoredStatements,
                statementBindings);
        if (regionCandidates is not null && functionSymbol.Kind == PowerShellSymbolKind.Function &&
            PowerShellBoundRegionCandidateSelector.TryCreateControlFlowEnvelope(
                document, function, functionSymbol, parameters, locals, authoredStatements, statementBindings,
                out var controlFlowCandidate))
            regionCandidates[controlFlowCandidate.RegionId] = controlFlowCandidate;
        PowerShellBoundRegionCandidate? guardedPrefixCandidate = null;
        if (regionCandidates is not null &&
            PowerShellBoundRegionCandidateSelector.TryCreateContinuation(
                document, function, functionSymbol, parameters, locals, authoredStatements, statementBindings,
                out var continuationCandidate))
        {
            regionCandidates[continuationCandidate.RegionId] = continuationCandidate;
            guardedPrefixCandidate = continuationCandidate;
        }
        if (regionCandidates is not null && functionSymbol.Kind == PowerShellSymbolKind.Function &&
            !bodyIsValid && guardedPrefixCandidate is null &&
            PowerShellBoundRegionCandidateSelector.TryCreateLaterContinuation(
                document, function, functionSymbol, parameters, locals, authoredStatements, statementBindings,
                out var laterContinuationCandidate))
        {
            regionCandidates[laterContinuationCandidate.RegionId] = laterContinuationCandidate;
            guardedPrefixCandidate = laterContinuationCandidate;
        }
        if (regionCandidates is not null && (!bodyIsValid || guardedPrefixCandidate is not null) &&
            PowerShellBoundRegionCandidateSelector.TryCreateDetachedContinuation(
                document, function, functionSymbol, parameters, locals, authoredStatements, statementBindings,
                guardedPrefixCandidate, out var detachedContinuationCandidate))
            regionCandidates[detachedContinuationCandidate.RegionId] = detachedContinuationCandidate;
        if (!bodyIsValid || diagnostics.Count > functionDiagnosticStart)
        {
            if (regionCandidates is not null && lastFailedStatementIndex >= 0 &&
                PowerShellBoundRegionCandidateSelector.TryCreate(
                    document,
                    function,
                    functionSymbol,
                    parameters,
                    locals,
                    authoredStatements,
                    statementBindings,
                    lastFailedStatementIndex,
                    out var candidate) &&
                !regionCandidates.ContainsKey(candidate.RegionId))
                regionCandidates[candidate.RegionId] = candidate;
            return null;
        }

        // Closed scalar/vector alternatives are a synthetic region-transfer representation,
        // not a PowerShell-visible whole-function value. Keep the authored function as the
        // runtime owner even when every statement bound successfully; promoted region helpers
        // are compiled independently from the candidates recorded above.
        if (locals.Any(static local => PowerShellRegionTransferTypePolicy.IsClosedValueAlternative(local.Type)))
            return null;

        var body = new PowerShellBoundBlock(PowerShellSourceParser.GetSpan(document, function.Body.Extent), statements.ToArray());
        var scopeSymbols = parameters.Select(static parameter => parameter.Symbol)
            .Concat(locals.Select(static local => local.Symbol))
            .OrderBy(static symbol => symbol.StableKey, StringComparer.Ordinal)
            .ToArray();
        return new PowerShellBoundFunction(
            functionSymbol,
            parameters,
            locals,
            new PowerShellLexicalScope(functionSymbol, scopeSymbols),
            PowerShellCommentHelpBinder.Bind(function, functionSymbol),
            PowerShellAdvancedFunctionPolicy.GetAliases(function),
            PowerShellAdvancedFunctionPolicy.GetBodyBinding(function.Body),
            outputTypeContract.SemanticType,
            outputTypeContract.MetadataTypeName,
            body,
            PowerShellTypeFact.Unknown,
            PowerShellOutputCardinality.Unknown,
            PowerShellSemanticEffect.None,
            PowerShellRequiredCapability.None,
            PowerShellExecutionDisposition.Typed,
            nativeFunctionBinding);
    }

    private PowerShellBoundParameter[]? BindParameters(
        ParsedSourceDocument document,
        FunctionDefinitionAst function,
        IDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        string? targetFramework,
        PowerShellCompilationCapability capabilities)
    {
        var parameters = new List<PowerShellBoundParameter>();
        var invalid = false;
        foreach (var parameter in PowerShellParameterSyntax.GetParameters(function.Body))
        {
            var name = parameter.Name.VariablePath.UserPath;
            var span = PowerShellSourceParser.GetSpan(document, parameter.Extent);
            if (symbols.ContainsKey(name))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB1001", $"Parameter '${name}' is declared more than once.", span));
                return null;
            }

            var contract = PowerShellParameterContractBinder.Bind(parameter, targetFramework, capabilities, _semanticProfile.ProfileId);
            var clrType = parameter.StaticType == typeof(System.Management.Automation.SwitchParameter)
                ? typeof(bool)
                : parameter.StaticType;
            if (!PowerShellCompilationParameterTypePolicy.CanUseInMethod(clrType, targetFramework, capabilities))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic(
                    PowerShellCompilationFeatureIds.ParameterType,
                    $"Parameter '${name}' has CLR type '{parameter.StaticType.FullName}' that requires a target capability unavailable to this compilation.",
                    span));
                invalid = true;
            }
            if (contract.Bindings.Any(static binding => binding.ValueFromPipeline || binding.ValueFromPipelineByPropertyName) &&
                !capabilities.HasFlag(PowerShellCompilationCapability.PipelineParameterBinding))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic(
                    PowerShellCompilationFeatureIds.ParameterMetadata,
                    $"Parameter '${name}' declares pipeline binding metadata through syntax node 'AttributeAst' that requires a pipeline-capable generated command host.",
                    span));
                invalid = true;
            }
            if (parameter.DefaultValue is not null && contract.DefaultValue is null &&
                !capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic(
                    PowerShellCompilationFeatureIds.ParameterDefault,
                    $"Parameter '${name}' has a runtime-evaluated default value that cannot be lowered into the typed parameter contract.",
                    PowerShellSourceParser.GetSpan(document, parameter.DefaultValue.Extent)));
                invalid = true;
            }
            var hasAuthoredType = parameter.Attributes.OfType<TypeConstraintAst>().Any();
            var type = clrType == typeof(object) && !hasAuthoredType &&
                       !PowerShellCompilationParameterTypePolicy.CanUseUntypedObject(capabilities)
                ? PowerShellTypeFact.Unknown
                : new PowerShellTypeFact(
                    clrType,
                    hasAuthoredType ? PowerShellTypeFactProvenance.Explicit : PowerShellTypeFactProvenance.Inferred,
                    parameter.StaticType == typeof(System.Management.Automation.SwitchParameter)
                        ? $"Parameter '${name}' has an authored SwitchParameter contract represented as Boolean only when its object identity is not observed."
                        : hasAuthoredType
                            ? $"Parameter '${name}' has an authored type constraint."
                            : $"Untyped parameter '${name}' preserves the PowerShell host's object-valued parameter contract.");
            var symbol = new PowerShellSymbolId(PowerShellSymbolKind.Parameter, document.DocumentId, name, span, function.Name + "/parameter/" + name);
            if (capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding)) type = PowerShellTypeFact.Unknown;
            var bound = new PowerShellBoundParameter(symbol, type, contract);
            symbols.Add(name, new PowerShellSemanticSymbolBinding(symbol, type));
            parameters.Add(bound);
        }
        foreach (var collision in parameters
                     .SelectMany(static parameter => parameter.Contract.Aliases
                         .Append(parameter.Contract.Name)
                         .Select(name => new { Name = name, Parameter = parameter.Contract.Name }))
                     .GroupBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
                     .Where(static group => group.Select(item => item.Parameter).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                PowerShellCompilationFeatureIds.ParameterBinding,
                $"Parameter name or alias '{collision.Key}' is ambiguous between {string.Join(", ", collision.Select(static item => "$" + item.Parameter).Distinct(StringComparer.OrdinalIgnoreCase))}.",
                PowerShellSourceParser.GetSpan(document, function.Extent)));
            invalid = true;
        }
        if (!capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding) && !PowerShellParameterSemanticValidator.Validate(
                document,
                function,
                parameters.Select(static parameter => parameter.Contract).ToArray(),
                targetFramework,
                capabilities,
                diagnostics))
            invalid = true;
        _ = invalid;
        return parameters.ToArray();
    }

    private static ScriptBlockAst? FindOwningFunctionBody(Ast syntax)
    {
        for (var current = syntax; current is not null; current = current.Parent)
        {
            if (current is FunctionDefinitionAst function) return function.Body;
        }
        return null;
    }

    private bool IsLocalFunctionPipeline(
        PipelineAst pipeline,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        PowerShellCompilationCapability capabilities)
        => pipeline.PipelineElements.Count == 1 &&
           pipeline.PipelineElements[0] is CommandAst command &&
           _commandResolver.Resolve(
               command,
               functions.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase),
               capabilities).Origin == PowerShellCommandSemanticOrigin.LocalFunction;

    private static bool TryGetLocalFunction(CommandAst command, IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions, out PowerShellLocalCallSignature target)
    {
        var name = command.GetCommandName();
        if (!string.IsNullOrWhiteSpace(name) && functions.TryGetValue(name, out target!)) return true;
        target = null!;
        return false;
    }

    private static PowerShellTypeFact LiteralType(Type type, string explanation)
        => new(type, PowerShellTypeFactProvenance.Literal, explanation);

    private static bool IsRuntimeOwnedScope(string name)
        => name.StartsWith("env:", StringComparison.OrdinalIgnoreCase) ||
           name.StartsWith("script:", StringComparison.OrdinalIgnoreCase) ||
           name.StartsWith("global:", StringComparison.OrdinalIgnoreCase) ||
           name.StartsWith("private:", StringComparison.OrdinalIgnoreCase) ||
           name.StartsWith("variable:", StringComparison.OrdinalIgnoreCase);

    private static PowerShellBoundExpression? BindConditionTruthiness(
        PowerShellBoundExpression condition,
        PowerShellCompilationCapability capabilities,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        ParsedSourceDocument document,
        Ast syntax, bool nativePostTestCondition = false)
    {
        var nativePosition = capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding);
        if (condition.Type.ClrType == typeof(bool) && !nativePosition) return condition;
        if (!nativePosition && PowerShellClrTypeSemantics.IsIntegral(condition.Type.ClrType))
        {
            var zero = Activator.CreateInstance(condition.Type.ClrType);
            return new PowerShellBoundBinaryExpression(
                condition.Span,
                PowerShellBoundBinaryOperator.NotEqual,
                condition,
                new PowerShellBoundLiteralExpression(
                    condition.Span,
                    zero,
                    new PowerShellTypeFact(condition.Type.ClrType, PowerShellTypeFactProvenance.Literal,
                        "Integral PowerShell truthiness compares the closed value with zero."),
                    PowerShellValueState.Known),
                new PowerShellTypeFact(typeof(bool), PowerShellTypeFactProvenance.Inferred,
                    "A nonzero integral value is true under PowerShell truthiness."));
        }
        if (condition.Type.ClrType != typeof(bool) && !capabilities.HasFlag(PowerShellCompilationCapability.PowerShellLanguageConversions))
        {
            var message = condition is PowerShellBoundMutationExpression { Operation: PowerShellBoundMutationOperator.Assign } mutation
                ? $"Local variable '${mutation.Target.Name}' may remain unassigned because its assignment occurs only while evaluating a dynamic-truthiness condition."
                : "PowerShell truthiness conversion is dynamic; typed conditions must already be Boolean.";
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2301", message, condition.Span));
            return null;
        }
        var span = nativePosition ? PowerShellSourceParser.GetSpan(document, syntax.Extent) : condition.Span;
        return new PowerShellBoundConversionExpression(
            span,
            new PowerShellTypeFact(typeof(bool), PowerShellTypeFactProvenance.Inferred, "PowerShell-hosted condition truthiness selects one Boolean result."),
            condition,
            usePowerShellTruthiness: condition.Type.ClrType != typeof(bool),
            nativeSourcePath: nativePosition ? document.Path : null,
            nativeSourceText: nativePosition ? PowerShellNativeFunctionBindingPolicy.SourceLines(document, span) : string.Empty,
            nativePostTestCondition: nativePostTestCondition);
    }

    private static Ast UnwrapExpression(Ast syntax, bool preservePipeline = false)
    {
        while (true)
        {
            switch (syntax)
            {
                case PipelineAst pipeline when preservePipeline && PowerShellCommandRegionSemanticBinder.RequiresPipelineSyntax(pipeline):
                    return pipeline;
                case CommandExpressionAst command when preservePipeline && command.Redirections.Count > 0:
                    return command;
                case PipelineAst pipeline when pipeline.PipelineElements.Count == 1 && pipeline.PipelineElements[0] is CommandExpressionAst command:
                    syntax = command.Expression;
                    continue;
                case PipelineAst pipeline when pipeline.PipelineElements.Count == 1 && pipeline.PipelineElements[0] is CommandAst command:
                    return command;
                case CommandExpressionAst command:
                    syntax = command.Expression;
                    continue;
                case ParenExpressionAst parenthesized:
                    syntax = parenthesized.Pipeline;
                    continue;
                default:
                    return syntax;
            }
        }
    }

    private static PowerShellSemanticDiagnostic[] OrderDiagnostics(IEnumerable<PowerShellSemanticDiagnostic> diagnostics)
        => diagnostics.OrderBy(static diagnostic => diagnostic.Span.DocumentId, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.Span.StartOffset)
            .ThenBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ToArray();

}
