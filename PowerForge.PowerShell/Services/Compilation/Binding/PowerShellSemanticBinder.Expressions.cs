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
        var authoredSyntax = syntax;
        syntax = UnwrapExpression(syntax, preservePipeline: capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding));
        var span = PowerShellSourceParser.GetSpan(document, syntax.Extent);
        if (_runtimeFreeModule is not null && syntax is VariableExpressionAst ownedVariable &&
            (ownedVariable.VariablePath.IsScript || ownedVariable.VariablePath.IsUnqualified &&
                !symbols.ContainsKey(ownedVariable.VariablePath.UserPath)) &&
            symbols.TryGetValue(ownedVariable.VariablePath.IsScript ? ownedVariable.VariablePath.UserPath :
                "script:" + ownedVariable.VariablePath.UserPath, out var ownedField) &&
            ownedField.Symbol.Kind == PowerShellSymbolKind.ModuleState)
            return new PowerShellBoundVariableExpression(span, ownedField.Symbol, ownedField.Type);
        var functionBody = FindOwningFunctionBody(syntax);
        if (capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding) &&
            capabilities.HasFlag(PowerShellCompilationCapability.PowerShellHostTypes) &&
            (syntax is CommandAst or PipelineAst or CommandExpressionAst { Redirections.Count: > 0 }))
            return PowerShellCommandRegionSemanticBinder.BindNativeCapture(document, syntax, authoredSyntax,
                _commandResolver, functions.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase), capabilities);
        if (capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding) && syntax is IndexExpressionAst nativeIndex)
            return PowerShellNativeAccessSemanticBinder.BindIndex(document, nativeIndex,
                (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities), diagnostics);
        if (capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding) && syntax is InvokeMemberExpressionAst nativeInvocation &&
            HasObservedCatchAncestor(nativeInvocation) && InvocationConsumesAuthoredTypeLiteral(nativeInvocation) &&
            !HasClosedDirectTypeArgumentCatchAll(nativeInvocation))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                PowerShellCompilationFeatureIds.ForSyntax(nameof(InvokeMemberExpressionAst)),
                "Native method invocation inside this observed catch boundary remains hosted until its argument and caught-error identity are closed.",
                span));
            return null;
        }
        if (capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding) && syntax is InvokeMemberExpressionAst acceptedNativeInvocation)
            return PowerShellNativeAccessSemanticBinder.BindInvocation(document, acceptedNativeInvocation,
                (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                targetFramework, capabilities, diagnostics);
        if (capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding) &&
            syntax is MemberExpressionAst nativeMember && syntax is not InvokeMemberExpressionAst &&
            (!nativeMember.Static || nativeMember.Member is not StringConstantExpressionAst))
            return PowerShellNativeAccessSemanticBinder.BindMember(document, nativeMember,
                (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                targetFramework, capabilities, diagnostics);
        if (functionBody is not null &&
            !(syntax is VariableExpressionAst && capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding)) &&
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
            case IfStatementAst conditional when capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding):
                return BindNativeConditionalValue(document, conditional, symbols, functions, diagnostics,
                    targetFramework, capabilities, contextualType == typeof(object));
            case SubExpressionAst subexpression when capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding):
                return PowerShellArraySemanticBinder.BindNativeSubexpression(document, subexpression,
                    (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                    _semanticProfile, diagnostics);
            case ExpandableStringExpressionAst expandable:
                return PowerShellStringSemanticBinder.BindInterpolated(
                    document,
                    expandable,
                    (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                    diagnostics,
                    capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding));
            case StringConstantExpressionAst text:
                return new PowerShellBoundLiteralExpression(span, text.Value, LiteralType(typeof(string), "String literal syntax determines the CLR representation."), PowerShellValueState.Known);
            case ConstantExpressionAst constant:
                return new PowerShellBoundLiteralExpression(span, constant.Value, LiteralType(constant.Value?.GetType() ?? typeof(object), "Literal syntax determines the CLR representation."), constant.Value is null ? PowerShellValueState.Null : PowerShellValueState.Known);
            case TypeExpressionAst typeExpression:
                var typeValue = typeExpression.TypeName.GetReflectionType();
                if (typeValue is null ||
                    !PowerShellCompilationParameterTypePolicy.CanUseInMethod(typeValue, targetFramework, capabilities))
                {
                    diagnostics.Add(new PowerShellSemanticDiagnostic(
                        PowerShellCompilationFeatureIds.ForSyntax(nameof(TypeExpressionAst)),
                        $"CLR type literal '{typeExpression.TypeName.FullName}' is not statically available in the generated project reference set for the requested target.",
                        span));
                    return null;
                }
                return new PowerShellBoundLiteralExpression(
                    span,
                    typeValue,
                    new PowerShellTypeFact(typeof(Type), PowerShellTypeFactProvenance.Literal,
                        "A statically resolved type literal produces one System.Type value."),
                    PowerShellValueState.Known);
            case ArrayLiteralAst array:
                return PowerShellArraySemanticBinder.Bind(
                    document,
                    array,
                    array.Elements,
                    PowerShellBoundArrayKind.Literal,
                    contextualType,
                    (item, elementType) => BindExpression(document, item, symbols, functions, diagnostics, elementType, targetFramework, capabilities),
                    _semanticProfile,
                    diagnostics);
            case ArrayExpressionAst array:
            {
                if (array.SubExpression.Traps is not null)
                {
                    diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2501", "Collected arrays with traps retain their native statement handlers.", span));
                    return null;
                }
                var closedTypedVector = contextualType is { IsArray: true } vectorType &&
                                        vectorType.GetArrayRank() == 1 &&
                                        vectorType.GetElementType() is { } vectorElement &&
                                        vectorType == vectorElement.MakeArrayType() &&
                                        PowerShellStableScalarTypePolicy.IsSupported(vectorElement);
                if (capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding) &&
                    array.SubExpression.Statements.Count > 0 && !closedTypedVector)
                    return PowerShellArraySemanticBinder.BindNativeCollection(document, array, contextualType,
                        (item, elementType) => BindExpression(document, item, symbols, functions, diagnostics, elementType, targetFramework, capabilities),
                        _semanticProfile, diagnostics);
                var elements = new List<ExpressionAst>();
                foreach (var statement in array.SubExpression.Statements)
                {
                    if (statement is not PipelineAst { PipelineElements.Count: 1 } pipeline ||
                        pipeline.PipelineElements[0] is not CommandExpressionAst { Redirections.Count: 0 } command)
                    {
                        diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2501", "Typed @() expressions accept expression statements without redirection.", PowerShellSourceParser.GetSpan(document, statement.Extent)));
                        return null;
                    }
                    elements.Add(command.Expression);
                }
                return PowerShellArraySemanticBinder.Bind(
                    document,
                    array,
                    elements,
                    PowerShellBoundArrayKind.CollectedExpression,
                    contextualType,
                    (item, elementType) => BindExpression(document, item, symbols, functions, diagnostics, elementType, targetFramework, capabilities),
                    _semanticProfile,
                    diagnostics);
            }
            case HashtableAst hashtable:
                return PowerShellDictionarySemanticBinder.BindLiteral(
                    document,
                    hashtable,
                    ordered: false,
                    contextualType,
                    (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                    capabilities,
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
            case VariableExpressionAst variable when capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding):
                return PowerShellNativeFunctionBindingPolicy.BindVariable(document, variable);
            case VariableExpressionAst variable when symbols.TryGetValue(variable.VariablePath.UserPath, out var symbol):
                return new PowerShellBoundVariableExpression(
                    span,
                    symbol.Symbol,
                    symbol.Type,
                    symbol.ValueState,
                    symbol.IsModuleStateDerived,
                    symbol.IsBraceFreeString);
            case VariableExpressionAst variable:
                diagnostics.Add(new PowerShellSemanticDiagnostic(
                    PowerShellCompilationFeatureIds.RuntimeScope,
                    $"Variable '${variable.VariablePath.UserPath}' requires dynamic PowerShell scope or runtime-owned automatic state.",
                    span));
                return null;
            case ScriptBlockExpressionAst scriptBlock:
                if (capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding) &&
                    _nativeScriptBlocks.ContainsKey(ScriptBlockKey(document.DocumentId, scriptBlock.Extent.StartOffset)))
                    return BindNativeScriptBlock(document, scriptBlock, functions, diagnostics);
                return PowerShellConstantBooleanDelegateSemanticBinder.Bind(
                    document,
                    scriptBlock,
                    contextualType,
                    targetFramework,
                    diagnostics);
            case ConvertExpressionAst conversion when PowerShellDictionarySemanticBinder.IsOrderedHashtableConversion(conversion):
                return PowerShellDictionarySemanticBinder.BindLiteral(
                    document,
                    (HashtableAst)conversion.Child,
                    ordered: true,
                    contextualType,
                    (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                    capabilities,
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
                    _semanticProfile.ProfileId,
                    capabilities,
                    diagnostics);
            case BinaryExpressionAst binary:
                var rightOperandSymbols = RefineShortCircuitRightOperandSymbols(binary, symbols);
                return PowerShellOperatorSemanticBinder.BindBinary(
                    document,
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
                if (PowerShellMutationSemanticBinder.TryBindIncrement(document, unary, symbols, out var mutation, diagnostics, capabilities)) return mutation;
                return PowerShellOperatorSemanticBinder.BindUnary(
                    document,
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
                    diagnostics, capabilities);
            case IndexExpressionAst index:
                return PowerShellDictionarySemanticBinder.BindIndex(
                    document,
                    index,
                    (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                    capabilities,
                    diagnostics);
            case InvokeMemberExpressionAst invocation when PowerShellBoundParametersPolicy.TryGetContainsKey(invocation, out var parameterName):
                if (PowerShellParameterSyntax.GetParameters(functionBody).Any(parameter =>
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

    private static bool HasObservedCatchAncestor(Ast expression)
    {
        for (Ast? ancestor = expression.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor is TryStatementAst { CatchClauses.Count: > 0 }) return true;
            if (ancestor is FunctionDefinitionAst) return false;
        }
        return false;
    }

    private static bool HasClosedDirectTypeArgumentCatchAll(InvokeMemberExpressionAst invocation)
    {
        // Hoisted type values and nested casts have different storage/evaluation contracts.
        // The admitted shape is one authored type literal consumed by catch-all handling.
        if (invocation.Arguments is not { Count: 1 } arguments || arguments[0] is not TypeExpressionAst)
            return false;
        var observed = false;
        for (Ast? ancestor = invocation.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor is FunctionDefinitionAst) break;
            if (ancestor is not TryStatementAst { CatchClauses.Count: > 0 } attempted) continue;
            observed = true;
            if (attempted.CatchClauses.Any(static clause => clause.CatchTypes.Count != 0)) return false;
        }
        return observed;
    }

    private static bool InvocationConsumesAuthoredTypeLiteral(InvokeMemberExpressionAst invocation)
    {
        if (invocation.Arguments is not { } arguments) return false;
        if (arguments.Any(static argument =>
                argument.Find(static node => node is TypeExpressionAst, searchNestedScriptBlocks: false) is not null))
            return true;

        var referencedVariables = arguments
            .SelectMany(static argument => argument.FindAll(static node => node is VariableExpressionAst,
                searchNestedScriptBlocks: false).OfType<VariableExpressionAst>())
            .Select(static variable => variable.VariablePath.UserPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (referencedVariables.Count == 0) return false;

        var body = FindOwningFunctionBody(invocation);
        if (body is null) return false;
        var assignments = body.FindAll(node => node is AssignmentStatementAst assignment &&
                assignment.Extent.EndOffset <= invocation.Extent.StartOffset,
                searchNestedScriptBlocks: false)
            .OfType<AssignmentStatementAst>()
            .OrderByDescending(static assignment => assignment.Extent.EndOffset)
            .ToArray();
        var resolvedVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var assignment in assignments)
        {
            var target = PowerShellAssignmentTargetPolicy.FindDirectVariable(assignment.Left, true);
            if (target is null || !referencedVariables.Contains(target.VariablePath.UserPath) ||
                !resolvedVariables.Add(target.VariablePath.UserPath))
                continue;
            if (assignment.Right.Find(static child => child is TypeExpressionAst,
                    searchNestedScriptBlocks: false) is not null)
                return true;
            foreach (var source in assignment.Right.FindAll(static child => child is VariableExpressionAst,
                         searchNestedScriptBlocks: false).OfType<VariableExpressionAst>())
                if (!resolvedVariables.Contains(source.VariablePath.UserPath))
                    referencedVariables.Add(source.VariablePath.UserPath);
        }
        return false;
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
