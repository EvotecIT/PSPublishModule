using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    private PowerShellBoundExpression? BindExpression(
        ParsedSourceDocument document,
        Ast syntax,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        Type? contextualType = null,
        string? targetFramework = null,
        PowerShellCompilationCapability capabilities = PowerShellCompilationCapability.None)
    {
        syntax = UnwrapExpression(syntax);
        var span = PowerShellSourceParser.GetSpan(document, syntax.Extent);
        var functionBody = FindOwningFunctionBody(syntax);
        if (functionBody is not null &&
            PowerShellRuntimeStateSemanticBinder.TryBind(
                document,
                syntax,
                functionBody,
                targetFramework,
                _semanticProfile,
                capabilities,
                (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                diagnostics,
                out var runtimeState))
            return runtimeState;
        if (syntax is MemberExpressionAst knownPropertyAccess &&
            PowerShellObjectSemanticBinder.TryBindKnownPropertiesValue(
                document,
                knownPropertyAccess,
                (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                capabilities,
                out var knownProperty))
            return knownProperty;
        switch (syntax)
        {
            case ExpandableStringExpressionAst expandable:
                return PowerShellStringSemanticBinder.BindInterpolated(
                    document,
                    expandable,
                    (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                    diagnostics);
            case StringConstantExpressionAst text:
                return new PowerShellBoundLiteralExpression(span, text.Value, LiteralType(typeof(string), "String literal syntax determines the CLR representation."), PowerShellValueState.Known);
            case ConstantExpressionAst constant:
                return new PowerShellBoundLiteralExpression(span, constant.Value, LiteralType(constant.Value?.GetType() ?? typeof(object), "Literal syntax determines the CLR representation."), constant.Value is null ? PowerShellValueState.Null : PowerShellValueState.Known);
            case ArrayLiteralAst array:
                return PowerShellArraySemanticBinder.Bind(
                    document,
                    array,
                    array.Elements,
                    PowerShellBoundArrayKind.Literal,
                    contextualType,
                    (item, elementType) => BindExpression(document, item, symbols, functions, diagnostics, elementType, targetFramework, capabilities),
                    diagnostics);
            case ArrayExpressionAst array:
            {
                var elements = new List<ExpressionAst>();
                foreach (var statement in array.SubExpression.Statements)
                {
                    if (statement is not PipelineAst { PipelineElements.Count: 1 } pipeline ||
                        pipeline.PipelineElements[0] is not CommandExpressionAst command)
                    {
                        diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2501", "Typed @() expressions accept only side-effect-free expression statements.", PowerShellSourceParser.GetSpan(document, statement.Extent)));
                        return null;
                    }
                    if (command.Expression is ArrayLiteralAst literal) elements.AddRange(literal.Elements);
                    else elements.Add(command.Expression);
                }
                return PowerShellArraySemanticBinder.Bind(
                    document,
                    array,
                    elements,
                    PowerShellBoundArrayKind.CollectedExpression,
                    contextualType,
                    (item, elementType) => BindExpression(document, item, symbols, functions, diagnostics, elementType, targetFramework, capabilities),
                    diagnostics);
            }
            case HashtableAst hashtable:
                return PowerShellDictionarySemanticBinder.BindLiteral(
                    document,
                    hashtable,
                    ordered: false,
                    contextualType,
                    (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                    diagnostics);
            case VariableExpressionAst variable when variable.VariablePath.UserPath.Equals("true", StringComparison.OrdinalIgnoreCase):
                return new PowerShellBoundLiteralExpression(span, true, LiteralType(typeof(bool), "$true is a Boolean literal."), PowerShellValueState.Known);
            case VariableExpressionAst variable when variable.VariablePath.UserPath.Equals("false", StringComparison.OrdinalIgnoreCase):
                return new PowerShellBoundLiteralExpression(span, false, LiteralType(typeof(bool), "$false is a Boolean literal."), PowerShellValueState.Known);
            case VariableExpressionAst variable when variable.VariablePath.UserPath.Equals("null", StringComparison.OrdinalIgnoreCase):
                var nullType = contextualType is not null &&
                               !contextualType.IsValueType &&
                               PowerShellCompilationParameterTypePolicy.CanUseInMethod(contextualType, targetFramework, capabilities)
                    ? contextualType
                    : typeof(object);
                return new PowerShellBoundLiteralExpression(
                    span,
                    null,
                    LiteralType(
                        nullType,
                        nullType == typeof(object)
                            ? "$null has no narrower CLR representation."
                            : "$null is represented by the exact contextual reference type."),
                    PowerShellValueState.Null);
            case VariableExpressionAst variable when symbols.TryGetValue(variable.VariablePath.UserPath, out var symbol):
                return new PowerShellBoundVariableExpression(span, symbol.Symbol, symbol.Type, symbol.ValueState);
            case VariableExpressionAst variable:
                diagnostics.Add(new PowerShellSemanticDiagnostic(
                    PowerShellCompilationFeatureIds.RuntimeScope,
                    $"Variable '${variable.VariablePath.UserPath}' requires dynamic PowerShell scope or runtime-owned automatic state.",
                    span));
                return null;
            case ScriptBlockExpressionAst:
                diagnostics.Add(new PowerShellSemanticDiagnostic(
                    PowerShellCompilationFeatureIds.ScriptBlock,
                    "Nested script blocks require a typed delegate or explicit hosted PowerShell boundary.",
                    span));
                return null;
            case ConvertExpressionAst conversion when PowerShellDictionarySemanticBinder.IsOrderedHashtableConversion(conversion):
                return PowerShellDictionarySemanticBinder.BindLiteral(
                    document,
                    (HashtableAst)conversion.Child,
                    ordered: true,
                    contextualType,
                    (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                    diagnostics);
            case ConvertExpressionAst conversion when PowerShellObjectConstructionPolicy.IsLiteral(conversion):
                return PowerShellObjectSemanticBinder.Bind(
                    document,
                    conversion,
                    (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                    capabilities,
                    diagnostics);
            case ConvertExpressionAst conversion:
                return PowerShellConversionSemanticBinder.Bind(
                    document,
                    conversion,
                    (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                    targetFramework,
                    capabilities,
                    diagnostics);
            case BinaryExpressionAst binary:
                var rightOperandSymbols = RefineShortCircuitRightOperandSymbols(binary, symbols);
                return PowerShellOperatorSemanticBinder.BindBinary(
                    binary,
                    span,
                    operand => BindExpression(
                        document,
                        operand,
                        ReferenceEquals(operand, binary.Right) ? rightOperandSymbols : symbols,
                        functions,
                        diagnostics,
                        targetFramework: targetFramework,
                        capabilities: capabilities),
                    diagnostics,
                    targetFramework,
                    capabilities);
            case UnaryExpressionAst unary:
                if (PowerShellMutationSemanticBinder.TryBindIncrement(document, unary, symbols, out var mutation, diagnostics)) return mutation;
                return PowerShellOperatorSemanticBinder.BindUnary(
                    unary,
                    span,
                    operand => BindExpression(document, operand, symbols, functions, diagnostics, targetFramework: targetFramework, capabilities: capabilities),
                    diagnostics,
                    capabilities);
            case AssignmentStatementAst assignment:
                return PowerShellMutationSemanticBinder.BindAssignment(
                    document,
                    assignment,
                    symbols,
                    (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                    diagnostics);
            case IndexExpressionAst index:
                return PowerShellDictionarySemanticBinder.BindIndex(
                    document,
                    index,
                    (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                    capabilities,
                    diagnostics);
            case InvokeMemberExpressionAst invocation when PowerShellBoundParametersPolicy.TryGetContainsKey(invocation, out var parameterName):
                if (functionBody?.ParamBlock?.Parameters.Any(parameter =>
                        parameter.Name.VariablePath.UserPath.Equals(parameterName, StringComparison.OrdinalIgnoreCase)) != true)
                {
                    diagnostics.Add(new PowerShellSemanticDiagnostic(
                        "PSB2502",
                        $"$PSBoundParameters.ContainsKey requires the literal canonical name of a declared parameter; '{parameterName}' is not declared by this function.",
                        span));
                    return null;
                }
                return new PowerShellBoundParameterPresenceExpression(span, parameterName);
            case InvokeMemberExpressionAst invocation:
                return PowerShellClrMemberSemanticBinder.BindInvocation(
                    document,
                    invocation,
                    (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                    targetFramework,
                    capabilities,
                    diagnostics);
            case MemberExpressionAst member:
                return PowerShellClrMemberSemanticBinder.BindMember(
                    document,
                    member,
                    (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                    targetFramework,
                    capabilities,
                    diagnostics);
            case CommandAst command when PowerShellCommentHelpSemanticBinder.IsCommand(command):
                return PowerShellCommentHelpSemanticBinder.Bind(
                    document,
                    command,
                    functions,
                    capabilities,
                    diagnostics);
            case CommandAst command when
                ResolveCommand(command, functions, capabilities).Origin == PowerShellCommandSemanticOrigin.LocalFunction &&
                TryGetLocalFunction(command, functions, out var target):
                return PowerShellLocalCallSemanticBinder.Bind(
                    document,
                    command,
                    target,
                    (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                    targetFramework,
                    capabilities,
                    diagnostics);
            case CommandAst command when
                ResolveCommand(command, functions, capabilities) is
                {
                    IsProvider: true,
                    Contract.Family: PowerShellCompilationCommandFamily.CommandDiscovery
                } discovery:
                return PowerShellCommandDiscoverySemanticBinder.Bind(
                    document,
                    command,
                    discovery.Contract!,
                    (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                    contextualType,
                    capabilities,
                    diagnostics);
            case CommandAst command when
                ResolveCommand(command, functions, capabilities) is
                {
                    IsProvider: true,
                    Contract.Family: PowerShellCompilationCommandFamily.HostedBooleanQuery
                } hostedBoolean:
                return PowerShellHostedBooleanCommandSemanticBinder.Bind(
                    document,
                    command,
                    hostedBoolean.Contract!,
                    (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                    contextualType,
                    capabilities,
                    diagnostics);
            case CommandAst command when
                ResolveCommand(command, functions, capabilities) is
                {
                    IsProvider: true,
                    Contract.Family: PowerShellCompilationCommandFamily.RuntimeState
                } runtimeStateCommand:
                return PowerShellRuntimeStateCommandSemanticBinder.Bind(
                    document,
                    command,
                    runtimeStateCommand.Contract!,
                    targetFramework,
                    _semanticProfile.ProfileId,
                    capabilities,
                    diagnostics);
            case CommandAst command when
                ResolveCommand(command, functions, capabilities) is
                {
                    IsProvider: true,
                    Contract.Family: PowerShellCompilationCommandFamily.ClrConstruction
                } construction:
                return PowerShellNewObjectSemanticBinder.Bind(
                    document,
                    command,
                    construction.Contract!,
                    (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                    targetFramework,
                    diagnostics);
            case CommandAst command:
                var commandName = command.GetCommandName();
                var commandResolution = ResolveCommand(command, functions, capabilities);
                var featureId = commandResolution.Contract is not null
                    ? commandResolution.Contract!.FeatureId
                    : commandName is null
                        ? PowerShellCompilationFeatureIds.DynamicCommand
                        : PowerShellCompilationFeatureIds.ForCommand(commandName);
                diagnostics.Add(new PowerShellSemanticDiagnostic(
                    featureId,
                    commandResolution.Origin == PowerShellCommandSemanticOrigin.Ambiguous
                        ? $"Command invocation '{commandName}' is ambiguous across registered semantic providers: {string.Join(", ", commandResolution.Candidates.Select(static contract => contract.ProviderId))}."
                        : commandResolution.Origin == PowerShellCommandSemanticOrigin.Dynamic
                        ? "Dynamic command invocation requires PowerShell runtime command discovery."
                        : commandResolution.Origin == PowerShellCommandSemanticOrigin.PowerShellRuntime
                            ? $"Command invocation '{commandName}' must preserve PowerShell runtime command resolution because the source does not identify one canonical module-qualified provider command."
                        : commandResolution.IsProvider
                            ? $"Command invocation '{commandName}' is owned by semantic provider '{commandResolution.Contract!.ProviderId}' and requires its {commandResolution.Contract.Family} binding context."
                            : $"Command invocation '{commandName}' requires a registered semantic provider or a hosted PowerShell command region.",
                    span));
                return null;
            default:
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2101", $"Expression '{syntax.GetType().Name}' is not yet represented by the bound pipeline.", span));
                return null;
        }
    }

    private PowerShellCommandInvocationResolution ResolveCommand(
        CommandAst command,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        PowerShellCompilationCapability capabilities)
        => _commandResolver.Resolve(
            command,
            functions.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            capabilities);
}
