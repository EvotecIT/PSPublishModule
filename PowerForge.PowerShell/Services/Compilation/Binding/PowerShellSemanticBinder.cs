using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Converts parser-owned PowerShell syntax into the compiler's neutral bound representation.
/// Parser objects are consumed here and never become part of a bound node.
/// </summary>
internal sealed partial class PowerShellSemanticBinder
{
    private readonly PowerShellCommandSemanticRegistry _commandRegistry;
    private readonly PowerShellCompilationSemanticOracleProfile _semanticProfile;

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
        _semanticProfile = PowerShellCompilationSemanticOracleCatalog.Get(semanticProfileId);
    }

    private PowerShellBoundFunction? BindFunction(
        ParsedSourceDocument document,
        FunctionDefinitionAst function,
        PowerShellSymbolId functionSymbol,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        string? targetFramework,
        PowerShellCompilationCapability capabilities)
    {
        var functionDiagnosticStart = diagnostics.Count;
        if (!PowerShellOutputTypeSemanticPolicy.TryResolve(
                function.Body,
                targetFramework,
                capabilities,
                out var declaredOutputType,
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
        if (hasRuntimeFreeLifecycle)
            return BindRuntimeFreePipelineLifecycleFunction(
                document,
                function,
                functionSymbol,
                functions,
                diagnostics,
                targetFramework,
                bindingCapabilities,
                declaredOutputType,
                symbols,
                parameters,
                pipelineParameter,
                functionDiagnosticStart);
        var authoredStatements = function.Body.EndBlock?.Statements.ToArray() ?? Array.Empty<StatementAst>();
        var localFunctionNames = functions.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var runtimeTailStart = capabilities.HasFlag(PowerShellCompilationCapability.PowerShellStreams)
            ? PowerShellCommandIslandPolicy.FindRuntimeTailStart(authoredStatements, function.Body, localFunctionNames, _commandRegistry)
            : -1;
        var runtimeTailOffset = runtimeTailStart >= 0 ? authoredStatements[runtimeTailStart].Extent.StartOffset : (int?)null;
        var locals = DeclareLocals(document, function, symbols, functions, capabilities, runtimeTailOffset);
        var parametersByName = parameters.ToDictionary(static parameter => parameter.Symbol.Name, StringComparer.OrdinalIgnoreCase);

        var statements = new List<PowerShellBoundStatement>();
        var bodyIsValid = true;
        for (var index = 0; index < authoredStatements.Length; index++)
        {
            var statement = authoredStatements[index];
            if (PowerShellObjectSemanticBinder.TryBindAddMember(
                    document,
                    statement,
                    symbols,
                    (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                    capabilities,
                    _commandRegistry,
                    diagnostics,
                    out var objectMutation))
            {
                if (objectMutation is null) bodyIsValid = false;
                else statements.Add(objectMutation);
                continue;
            }
            if (capabilities.HasFlag(PowerShellCompilationCapability.PowerShellStreams) &&
                PowerShellHostedStatementBinder.TryBind(
                    document,
                    authoredStatements,
                    function.Body,
                    localFunctionNames,
                    symbols,
                    parametersByName,
                    runtimeTailStart,
                    _commandRegistry,
                    ref index,
                    out var hosted))
            {
                statements.Add(hosted!);
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
                continue;
            }
            statements.Add(bound);
        }
        if (!bodyIsValid || diagnostics.Count > functionDiagnosticStart) return null;

        var refinedTypes = symbols.Values.ToDictionary(static binding => binding.Symbol.StableKey, static binding => binding.Type, StringComparer.Ordinal);
        locals = locals.Select(local => new PowerShellBoundLocal(local.Symbol, refinedTypes[local.Symbol.StableKey])).ToArray();

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

    private static PowerShellBoundParameter[]? BindParameters(
        ParsedSourceDocument document,
        FunctionDefinitionAst function,
        IDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        string? targetFramework,
        PowerShellCompilationCapability capabilities)
    {
        var parameters = new List<PowerShellBoundParameter>();
        var invalid = false;
        foreach (var parameter in function.Body.ParamBlock?.Parameters.ToArray() ?? Array.Empty<ParameterAst>())
        {
            var name = parameter.Name.VariablePath.UserPath;
            var span = PowerShellSourceParser.GetSpan(document, parameter.Extent);
            if (symbols.ContainsKey(name))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB1001", $"Parameter '${name}' is declared more than once.", span));
                return null;
            }

            var contract = PowerShellParameterContractBinder.Bind(parameter, targetFramework);
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
            if (parameter.DefaultValue is not null && contract.DefaultValue is null)
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic(
                    PowerShellCompilationFeatureIds.ParameterDefault,
                    $"Parameter '${name}' has a runtime-evaluated default value that cannot be lowered into the typed parameter contract.",
                    PowerShellSourceParser.GetSpan(document, parameter.DefaultValue.Extent)));
                invalid = true;
            }
            var hasAuthoredType = parameter.Attributes.OfType<TypeConstraintAst>().Any();
            var type = clrType == typeof(object) && !hasAuthoredType
                ? PowerShellTypeFact.Unknown
                : new PowerShellTypeFact(
                    clrType,
                    PowerShellTypeFactProvenance.Explicit,
                    parameter.StaticType == typeof(System.Management.Automation.SwitchParameter)
                        ? $"Parameter '${name}' has an authored SwitchParameter contract represented as Boolean only when its object identity is not observed."
                        : $"Parameter '${name}' has an authored type constraint.");
            var symbol = new PowerShellSymbolId(PowerShellSymbolKind.Parameter, document.DocumentId, name, span, function.Name + "/parameter/" + name);
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
        if (!PowerShellParameterSemanticValidator.Validate(
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

    private PowerShellBoundStatement? BindStatement(
        ParsedSourceDocument document,
        StatementAst statement,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        bool isTerminal,
        string? targetFramework,
        PowerShellCompilationCapability capabilities)
    {
        if (statement is AssignmentStatementAst assignment)
        {
            if (PowerShellAssignmentTargetPolicy.FindDirectVariable(assignment.Left) is { } scopedTarget &&
                IsRuntimeOwnedScope(scopedTarget.VariablePath.UserPath))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic(
                    PowerShellCompilationFeatureIds.RuntimeScope,
                    $"Assignment to runtime-owned scope '${scopedTarget.VariablePath.UserPath}' is outside the bounded read-only runtime-state contract.",
                    PowerShellSourceParser.GetSpan(document, assignment.Extent)));
                return null;
            }
            if (PowerShellAssignmentTargetPolicy.FindDirectVariable(assignment.Left) is { } automatic &&
                PowerShellAssignmentTargetPolicy.IsReadOnlyAutomaticVariable(automatic.VariablePath.UserPath))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic(
                    PowerShellCompilationFeatureIds.AutomaticVariableAssignment,
                    $"Assignment to read-only automatic variable '${automatic.VariablePath.UserPath}' cannot be preserved by a typed artifact.",
                    PowerShellSourceParser.GetSpan(document, assignment.Extent)));
                return null;
            }
            if (PowerShellAssignmentTargetPolicy.FindDirectVariable(assignment.Left) is { } discarded &&
                discarded.VariablePath.UserPath.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                if (assignment.Operator.ToString() != "Equals")
                {
                    diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2405", "The $null discard target supports simple '=' assignment only.", PowerShellSourceParser.GetSpan(document, assignment.Extent)));
                    return null;
                }
                var discardedValue = BindExpression(document, assignment.Right, symbols, functions, diagnostics, targetFramework: targetFramework, capabilities: capabilities);
                return discardedValue is null
                    ? null
                    : new PowerShellBoundExpressionStatement(PowerShellSourceParser.GetSpan(document, assignment.Extent), discardedValue, emitsOutput: false);
            }
            if (assignment.Left is IndexExpressionAst index)
            {
                return PowerShellDictionarySemanticBinder.BindAssignment(
                    document,
                    assignment,
                    index,
                    (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                    capabilities,
                    diagnostics);
            }
            if (assignment.Left is MemberExpressionAst member)
            {
                return PowerShellClrMemberSemanticBinder.BindAssignment(
                    document,
                    assignment,
                    member,
                    (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                    targetFramework,
                    capabilities,
                    diagnostics);
            }
            var mutation = PowerShellMutationSemanticBinder.BindAssignment(
                document,
                assignment,
                symbols,
                (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                diagnostics);
            return mutation is null
                ? null
                : new PowerShellBoundAssignmentStatement(
                    mutation.Span,
                    mutation.Target,
                    mutation.Value!,
                    mutation.Operation,
                    mutation.NormalizeNullString,
                    mutation.CheckedIntegral);
        }
        if (statement is ReturnStatementAst returnStatement)
        {
            var expression = returnStatement.Pipeline is null
                ? null
                : BindExpression(document, returnStatement.Pipeline, symbols, functions, diagnostics, targetFramework: targetFramework, capabilities: capabilities);
            return returnStatement.Pipeline is null || expression is not null
                ? new PowerShellBoundReturnStatement(
                    PowerShellSourceParser.GetSpan(document, returnStatement.Extent),
                    expression,
                    expression is not PowerShellBoundMutationExpression && expression?.Type.ClrType != typeof(void))
                : null;
        }
        if (statement is IfStatementAst ifStatement)
        {
            var clauses = new List<PowerShellBoundConditionalClause>();
            foreach (var clause in ifStatement.Clauses)
            {
                var condition = BindExpression(document, clause.Item1, symbols, functions, diagnostics, targetFramework: targetFramework, capabilities: capabilities);
                if (condition is null) return null;
                condition = BindConditionTruthiness(condition, capabilities, diagnostics);
                if (condition is null) return null;
                var body = BindBlock(document, clause.Item2, symbols, functions, diagnostics, targetFramework, capabilities);
                if (body is null) return null;
                clauses.Add(new PowerShellBoundConditionalClause(condition, body));
            }
            var elseBlock = ifStatement.ElseClause is null
                ? null
                : BindBlock(document, ifStatement.ElseClause, symbols, functions, diagnostics, targetFramework, capabilities);
            if (ifStatement.ElseClause is not null && elseBlock is null) return null;
            return new PowerShellBoundIfStatement(PowerShellSourceParser.GetSpan(document, statement.Extent), clauses.ToArray(), elseBlock);
        }
        if (statement is WhileStatementAst whileStatement)
        {
            var condition = BindExpression(document, whileStatement.Condition, symbols, functions, diagnostics, targetFramework: targetFramework, capabilities: capabilities);
            if (condition is null) return null;
            condition = BindConditionTruthiness(condition, capabilities, diagnostics);
            if (condition is null) return null;
            var body = BindBlock(document, whileStatement.Body, symbols, functions, diagnostics, targetFramework, capabilities);
            return body is null ? null : new PowerShellBoundWhileStatement(PowerShellSourceParser.GetSpan(document, statement.Extent), condition, body);
        }
        if (statement is ForStatementAst forStatement)
        {
            var initializer = forStatement.Initializer is null
                ? null
                : BindExpression(document, forStatement.Initializer, symbols, functions, diagnostics, targetFramework: targetFramework, capabilities: capabilities) as PowerShellBoundMutationExpression;
            if (forStatement.Initializer is not null && initializer is null) return null;
            var condition = forStatement.Condition is null
                ? null
                : BindExpression(document, forStatement.Condition, symbols, functions, diagnostics, targetFramework: targetFramework, capabilities: capabilities);
            if (condition is not null) condition = BindConditionTruthiness(condition, capabilities, diagnostics);
            if (forStatement.Condition is not null && condition is null) return null;
            var iterator = forStatement.Iterator is null
                ? null
                : BindExpression(document, forStatement.Iterator, symbols, functions, diagnostics, targetFramework: targetFramework, capabilities: capabilities) as PowerShellBoundMutationExpression;
            if (forStatement.Iterator is not null && iterator is null) return null;
            var body = BindBlock(document, forStatement.Body, symbols, functions, diagnostics, targetFramework, capabilities);
            return body is null ? null : new PowerShellBoundForStatement(PowerShellSourceParser.GetSpan(document, statement.Extent), initializer, condition, iterator, body);
        }
        if (statement is ForEachStatementAst forEachStatement)
        {
            var variableSpan = PowerShellSourceParser.GetSpan(document, forEachStatement.Variable.Extent);
            if (!symbols.TryGetValue(forEachStatement.Variable.VariablePath.UserPath, out var target))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2302", $"foreach variable '${forEachStatement.Variable.VariablePath.UserPath}' has no function-scope semantic symbol.", variableSpan));
                return null;
            }
            var collection = BindExpression(document, forEachStatement.Condition, symbols, functions, diagnostics, targetFramework: targetFramework, capabilities: capabilities);
            if (collection is null) return null;
            var collectionType = collection.Type.ClrType;
            var scalarString = collectionType == typeof(string) && collection.Type.Provenance is PowerShellTypeFactProvenance.Explicit or PowerShellTypeFactProvenance.Literal;
            var elementType = collectionType.IsArray && collectionType.GetArrayRank() == 1
                ? collectionType.GetElementType()
                : scalarString ? typeof(string) : null;
            if (elementType is null || elementType != target.Type.ClrType)
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2303", "foreach requires a statically typed one-dimensional array or explicitly typed scalar string.", collection.Span));
                return null;
            }
            var body = BindBlock(document, forEachStatement.Body, symbols, functions, diagnostics, targetFramework, capabilities);
            return body is null ? null : new PowerShellBoundForEachStatement(PowerShellSourceParser.GetSpan(document, statement.Extent), target.Symbol, elementType, collection, scalarString, body);
        }
        if (statement is SwitchStatementAst switchStatement)
            return BindSwitchStatement(document, switchStatement, symbols, functions, diagnostics, targetFramework, capabilities);
        if (statement is ThrowStatementAst throwStatement)
        {
            if (throwStatement.IsRethrow)
            {
                if (!PowerShellControlFlowBindingPolicy.HasAncestor<CatchClauseAst>(throwStatement))
                {
                    diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2307", "A bare typed rethrow is valid only inside a catch clause.", PowerShellSourceParser.GetSpan(document, throwStatement.Extent)));
                    return null;
                }
                return new PowerShellBoundThrowStatement(PowerShellSourceParser.GetSpan(document, statement.Extent), null);
            }
            if (throwStatement.Pipeline is null) return null;
            var expression = BindExpression(document, throwStatement.Pipeline, symbols, functions, diagnostics, targetFramework: targetFramework, capabilities: capabilities);
            if (expression is null) return null;
            if (!typeof(Exception).IsAssignableFrom(expression.Type.ClrType))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2308", $"Typed throw requires a CLR exception expression; resolved type was '{expression.Type.ClrType.FullName}'.", expression.Span));
                return null;
            }
            return new PowerShellBoundThrowStatement(PowerShellSourceParser.GetSpan(document, statement.Extent), expression);
        }
        if (statement is TryStatementAst tryStatement)
        {
            if (tryStatement.Finally?.FindAll(static node => node is ReturnStatementAst or BreakStatementAst or ContinueStatementAst, searchNestedScriptBlocks: true).Any() == true)
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2309", "Typed finally blocks cannot alter enclosing return, break, or continue control flow.", PowerShellSourceParser.GetSpan(document, tryStatement.Finally.Extent)));
                return null;
            }
            var body = BindBlock(document, tryStatement.Body, symbols, functions, diagnostics, targetFramework, capabilities, terminalOutputReturns: isTerminal);
            if (body is null) return null;
            var catches = new List<PowerShellBoundCatchClause>();
            foreach (var clause in tryStatement.CatchClauses)
            {
                var types = new List<Type>();
                foreach (var constraint in clause.CatchTypes)
                {
                    var type = constraint.TypeName.GetReflectionType();
                    var supportedPowerShellRuntimeException = type == typeof(System.Management.Automation.RuntimeException) &&
                                                               capabilities.HasFlag(PowerShellCompilationCapability.PowerShellObjects);
                    if (type is null || !typeof(Exception).IsAssignableFrom(type) ||
                        !supportedPowerShellRuntimeException && !PowerShellGeneratedTypePolicy.IsSupported(type, targetFramework))
                    {
                        diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2310", $"Typed catch '{constraint.TypeName.FullName}' is outside the generated project reference set.", PowerShellSourceParser.GetSpan(document, constraint.Extent)));
                        return null;
                    }
                    types.Add(type);
                }
                var catchBody = BindBlock(document, clause.Body, symbols, functions, diagnostics, targetFramework, capabilities, terminalOutputReturns: isTerminal);
                if (catchBody is null) return null;
                catches.Add(new PowerShellBoundCatchClause(types.ToArray(), catchBody));
            }
            var catchAll = catches.FindIndex(static clause => clause.ExceptionTypes.Length == 0);
            if (catchAll >= 0 && catchAll != catches.Count - 1)
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2311", "A catch-all clause must follow all typed catches on the conservative typed path.", PowerShellSourceParser.GetSpan(document, tryStatement.CatchClauses[catchAll].Extent)));
                return null;
            }
            var flattened = catches.SelectMany((clause, clauseIndex) =>
                clause.ExceptionTypes.Select(type => new { ClauseIndex = clauseIndex, Type = type })).ToArray();
            for (var index = 0; index < flattened.Length; index++)
            {
                if (flattened.Take(index).Any(previous => previous.Type.IsAssignableFrom(flattened[index].Type)))
                {
                    diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2312", $"Typed catch '{flattened[index].Type.FullName}' is unreachable after a broader earlier catch.", PowerShellSourceParser.GetSpan(document, tryStatement.CatchClauses[flattened[index].ClauseIndex].Extent)));
                    return null;
                }
            }
            var finallyBlock = tryStatement.Finally is null
                ? null
                : BindBlock(document, tryStatement.Finally, symbols, functions, diagnostics, targetFramework, capabilities);
            if (tryStatement.Finally is not null && finallyBlock is null) return null;
            return new PowerShellBoundTryStatement(PowerShellSourceParser.GetSpan(document, statement.Extent), body, catches.ToArray(), finallyBlock);
        }
        if (statement is BreakStatementAst { Label: null } breakStatement && PowerShellControlFlowBindingPolicy.HasBreakableAncestor(breakStatement))
            return new PowerShellBoundBreakStatement(PowerShellSourceParser.GetSpan(document, statement.Extent));
        if (statement is ContinueStatementAst { Label: null } continueStatement && PowerShellControlFlowBindingPolicy.HasContinuableAncestor(continueStatement))
            return new PowerShellBoundContinueStatement(PowerShellSourceParser.GetSpan(document, statement.Extent));
        if (statement is BreakStatementAst labeledBreak && labeledBreak.Label is not null)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2313",
                "Labeled break is not supported by the typed compiler.",
                PowerShellSourceParser.GetSpan(document, labeledBreak.Extent)));
            return null;
        }
        if (statement is BreakStatementAst invalidBreak)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2314",
                "break must be inside a supported loop or scalar switch.",
                PowerShellSourceParser.GetSpan(document, invalidBreak.Extent)));
            return null;
        }
        if (statement is ContinueStatementAst labeledContinue && labeledContinue.Label is not null)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2315",
                "Labeled continue is not supported by the typed compiler.",
                PowerShellSourceParser.GetSpan(document, labeledContinue.Extent)));
            return null;
        }
        if (statement is ContinueStatementAst invalidContinue)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2316",
                "continue must be inside a supported loop.",
                PowerShellSourceParser.GetSpan(document, invalidContinue.Extent)));
            return null;
        }
        if (statement is PipelineAst { PipelineElements.Count: 1 } streamPipeline &&
            streamPipeline.PipelineElements[0] is CommandAst streamCommand &&
            PowerShellCommandIslandPolicy.TryGetTargetStreamCommand(
                streamCommand,
                capabilities,
                out var streamKind,
                out var messageSyntax,
                out var streamProvider,
                _commandRegistry))
        {
            var expectedType = streamKind == PowerShellStreamCommandKind.Success ? null : typeof(string);
            var message = BindExpression(document, messageSyntax, symbols, functions, diagnostics, expectedType, targetFramework, capabilities);
            return message is null
                ? null
                : new PowerShellBoundStreamWriteStatement(PowerShellSourceParser.GetSpan(document, statement.Extent), streamKind, streamProvider!, message);
        }
        if (statement is PipelineAst mappingPipeline &&
            TryBindRuntimeFreePipelineEnumeration(
                document,
                mappingPipeline,
                symbols,
                functions,
                diagnostics,
                targetFramework,
                capabilities,
                out var enumeration))
            return enumeration;
        if (statement is PipelineAst lifecyclePipeline &&
            IsRuntimeFreePipelineLifecycleInvocation(lifecyclePipeline, functions))
        {
            var invocation = BindRuntimeFreePipelineLifecycleInvocation(
                document,
                lifecyclePipeline,
                symbols,
                functions,
                diagnostics,
                targetFramework,
                capabilities);
            if (invocation is null) return null;
            if (!isTerminal)
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic(
                    "PSB2924",
                    "Runtime-free lifecycle success output must be the terminal result of its enclosing typed function.",
                    PowerShellSourceParser.GetSpan(document, lifecyclePipeline.Extent)));
                return null;
            }
            return new PowerShellBoundReturnStatement(
                PowerShellSourceParser.GetSpan(document, lifecyclePipeline.Extent),
                invocation,
                emitsValue: invocation.Type.ClrType != typeof(void));
        }
        if (statement is PipelineAst pipeline)
        {
            var expression = BindExpression(document, pipeline, symbols, functions, diagnostics, targetFramework: targetFramework, capabilities: capabilities);
            if (expression is null) return null;
            var emitsOutput = expression is not PowerShellBoundMutationExpression && expression.Type.ClrType != typeof(void);
            if (!isTerminal && emitsOutput && !IsLocalFunctionPipeline(pipeline, functions)) return null;
            if (isTerminal && IsLocalFunctionPipeline(pipeline, functions))
                return new PowerShellBoundReturnStatement(PowerShellSourceParser.GetSpan(document, statement.Extent), expression, emitsOutput);
            return expression is null
                ? null
                : new PowerShellBoundExpressionStatement(PowerShellSourceParser.GetSpan(document, statement.Extent), expression, emitsOutput);
        }
        return null;
    }

    private PowerShellBoundBlock? BindBlock(
        ParsedSourceDocument document,
        StatementBlockAst syntax,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        string? targetFramework,
        PowerShellCompilationCapability capabilities,
        bool terminalOutputReturns = false)
    {
        var statements = new List<PowerShellBoundStatement>();
        for (var index = 0; index < syntax.Statements.Count; index++)
        {
            var statement = syntax.Statements[index];
            var diagnosticCount = diagnostics.Count;
            var bound = BindStatement(
                document,
                statement,
                symbols,
                functions,
                diagnostics,
                isTerminal: terminalOutputReturns && index == syntax.Statements.Count - 1,
                targetFramework,
                capabilities);
            if (bound is null)
            {
                if (diagnostics.Count == diagnosticCount)
                    diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2001", $"Statement '{statement.GetType().Name}' is not yet represented by the bound pipeline.", PowerShellSourceParser.GetSpan(document, statement.Extent)));
                return null;
            }
            statements.Add(bound);
        }
        return new PowerShellBoundBlock(PowerShellSourceParser.GetSpan(document, syntax.Extent), statements.ToArray());
    }

    private static ScriptBlockAst? FindOwningFunctionBody(Ast syntax)
    {
        for (var current = syntax; current is not null; current = current.Parent)
        {
            if (current is FunctionDefinitionAst function) return function.Body;
        }
        return null;
    }

    private static bool IsLocalFunctionPipeline(PipelineAst pipeline, IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions)
        => pipeline.PipelineElements.Count == 1 && pipeline.PipelineElements[0] is CommandAst command && TryGetLocalFunction(command, functions, out _);

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
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        if (condition.Type.ClrType == typeof(bool)) return condition;
        if (!capabilities.HasFlag(PowerShellCompilationCapability.PowerShellLanguageConversions))
        {
            var message = condition is PowerShellBoundMutationExpression { Operation: PowerShellBoundMutationOperator.Assign } mutation
                ? $"Local variable '${mutation.Target.Name}' may remain unassigned because its assignment occurs only while evaluating a dynamic-truthiness condition."
                : "PowerShell truthiness conversion is dynamic; typed conditions must already be Boolean.";
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2301", message, condition.Span));
            return null;
        }
        return new PowerShellBoundConversionExpression(
            condition.Span,
            new PowerShellTypeFact(typeof(bool), PowerShellTypeFactProvenance.Inferred, "PowerShell-hosted condition truthiness selects one Boolean result."),
            condition,
            usePowerShellTruthiness: true);
    }

    private static Ast UnwrapExpression(Ast syntax)
    {
        while (true)
        {
            switch (syntax)
            {
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
