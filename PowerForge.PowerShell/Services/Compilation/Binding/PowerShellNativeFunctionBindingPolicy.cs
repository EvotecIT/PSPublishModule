using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Selects native invocation storage for observable binding and pipeline contracts.</summary>
internal static class PowerShellNativeFunctionBindingPolicy
{
    internal static PowerShellBoundNativeVariableExpression BindVariable(ParsedSourceDocument document, VariableExpressionAst variable)
    {
        var span = PowerShellSourceParser.GetSpan(document, variable.Extent);
        return new PowerShellBoundNativeVariableExpression(span, variable.VariablePath.UserPath,
            IsInsideExpandableString(variable), document.Path,
            SourceLines(document, span),
            PowerShellNativeVariableAnalysis.IsDirectLocal(variable));
    }

    internal static string SourceLines(ParsedSourceDocument document, SourceSpan span)
        => PowerShellSourceParser.GetSourceLines(document, span);

    internal static bool IsInsideExpandableString(Ast syntax)
    {
        for (var parent = syntax.Parent; parent is not null; parent = parent.Parent)
            if (parent is ExpandableStringExpressionAst) return true;
        return false;
    }

    internal static HashSet<string> FindInvocationClosure(IEnumerable<FunctionDefinitionAst> functions,
        PowerShellCompilationCapability capabilities,
        string? targetFramework)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding)) return result;
        var declarations = functions.GroupBy(static function => function.Name, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() == 1)
            .ToDictionary(static group => group.Key, static group => group.Single(), StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<FunctionDefinitionAst>(declarations.Values.Where(function =>
            RequiresNativeBinding(function, targetFramework, capabilities)));
        while (pending.Count > 0)
        {
            var function = pending.Dequeue();
            if (!result.Add(function.Name)) continue;
            foreach (var command in function.Body.FindAll(static node => node is CommandAst, searchNestedScriptBlocks: true).OfType<CommandAst>())
                if (command.GetCommandName() is { } name && declarations.TryGetValue(name, out var target) && !result.Contains(name))
                    pending.Enqueue(target);
        }
        return result;
    }

    internal static PowerShellNativeFunctionBinding? Select(FunctionDefinitionAst function, PowerShellCompilationCapability capabilities,
        string? targetFramework,
        bool requiresNativeInvocation = false)
    {
        if (!capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding) ||
            function.Body.DynamicParamBlock is not null)
            return null;
        var parameters = PowerShellParameterSyntax.GetParameters(function.Body).ToArray();
        if (!requiresNativeInvocation && !RequiresNativeBinding(function, targetFramework, capabilities)) return null;
        var declaration = function.Body.ParamBlock is { } block
            ? string.Join("\n", block.Attributes.Select(static attribute => attribute.Extent.Text).Concat(new[] { block.Extent.Text }))
            : "param(" + string.Join(",", parameters.Select(static parameter => parameter.Extent.Text)) + ")";
        var locals = PowerShellNativeVariableAnalysis.Analyze(function);
        return new PowerShellNativeFunctionBinding(declaration, locals,
            PowerShellNativeVariableAnalysis.FindLocalTypeDeclarations(function, locals),
            function.Body.BeginBlock is not null, function.Body.ProcessBlock is not null,
            function.Body.EndBlock is not null, function.Body.GetType().GetProperty("CleanBlock")?.GetValue(function.Body) is not null);
    }

    internal static Ast? FindNativePipelineOperator(FunctionDefinitionAst function, bool includeCommandRedirections = true)
        => function.Body.Find(node =>
            node is PipelineAst pipeline && PowerShellCommandRegionSemanticBinder.IsBackground(pipeline) ||
            node is CommandBaseAst { Redirections.Count: > 0 } command &&
                (includeCommandRedirections || command is CommandExpressionAst), searchNestedScriptBlocks: true);

    private static bool RequiresNativeBinding(FunctionDefinitionAst function, string? targetFramework,
        PowerShellCompilationCapability capabilities)
        => function.Body.BeginBlock is not null || function.Body.ProcessBlock is not null ||
           capabilities.HasFlag(PowerShellCompilationCapability.PowerShellHostTypes) &&
           function.Body.Find(static node => node is TypeExpressionAst expression &&
               expression.TypeName.GetReflectionType() is { } expressionType &&
               PowerShellCompilationParameterTypePolicy.IsQualifiedHostDataType(expressionType) ||
               node is TypeConstraintAst constraint && constraint.TypeName.GetReflectionType() is { } constraintType &&
               PowerShellCompilationParameterTypePolicy.IsQualifiedHostDataType(constraintType),
               searchNestedScriptBlocks: false) is not null ||
           function.Body.Find(node => node is ScriptBlockExpressionAst block && PowerShellSemanticBinder.IsCompiledValueScriptBlock(block, targetFramework) ||
               node is FunctionDefinitionAst nested && PowerShellSemanticBinder.IsCompiledNestedFunction(nested), searchNestedScriptBlocks: true) is not null ||
           function.Body.GetType().GetProperty("CleanBlock")?.GetValue(function.Body) is not null ||
           FindNativePipelineOperator(function) is not null ||
           function.Body.Find(static node => node is BinaryExpressionAst { Operator: TokenKind.Format } format &&
               format.Left.StaticType == typeof(object), searchNestedScriptBlocks: false) is not null ||
           function.Body.Find(static node =>
               node is ConvertExpressionAst conversion && conversion.Parent is CommandExpressionAst { Parent: PipelineAst discardPipeline } &&
                   PowerShellCompilationConversionPolicy.IsStatementDiscard(conversion) && IsCapturedPipeline(discardPipeline) ||
               node is SubExpressionAst or BinaryExpressionAst { Operator: TokenKind.Join or TokenKind.Isplit or TokenKind.Csplit or TokenKind.Ireplace or TokenKind.Creplace or
                   TokenKind.Imatch or TokenKind.Cmatch or TokenKind.Inotmatch or TokenKind.Cnotmatch or TokenKind.DotDot or TokenKind.As or
                   TokenKind.Icontains or TokenKind.Ccontains or TokenKind.Inotcontains or TokenKind.Cnotcontains or
                   TokenKind.Iin or TokenKind.Cin or TokenKind.Inotin or TokenKind.Cnotin } or
               UnaryExpressionAst { TokenKind: TokenKind.Join }, searchNestedScriptBlocks: false) is not null ||
           RequiresNativeObjectParameterForEach(function) ||
           RequiresNativeDictionaryKeyForEach(function) ||
           RequiresNativeStatementValue(function) ||
           RequiresNativeLiteralCommandValue(function, capabilities) ||
           RequiresNativeCommandSwitch(function, capabilities) ||
           RequiresNativeObjectParameterSwitch(function, capabilities) ||
           RequiresNativeConditionalAccessCapture(function) ||
           function.Body.Find(static node => node is UnaryExpressionAst
               { TokenKind: TokenKind.PlusPlus or TokenKind.MinusMinus or TokenKind.PostfixPlusPlus or TokenKind.PostfixMinusMinus } increment &&
               PowerShellSemanticBinder.NativeAccessMutationReceiver(increment.Child) is not null,
               searchNestedScriptBlocks: false) is not null ||
           RequiresNativeVariableIndex(function) ||
           RequiresNativeModuleStateStringConversion(function) ||
           RequiresNativeStaticNumericArgumentConversion(function) ||
           PowerShellAutomaticVariableObservationPolicy.ObservesCatchState(function) ||
           function.Body.Find(static node => node is ConvertExpressionAst conversion &&
               PowerShellObjectConstructionPolicy.HasTypeNameMetadata(conversion),
               searchNestedScriptBlocks: false) is not null ||
           function.Body.Find(static node => node is ArrayExpressionAst array &&
               array.SubExpression.Statements.Any(static statement => statement is AssignmentStatementAst),
               searchNestedScriptBlocks: false) is not null ||
           RequiresNativeStatementArrayCapture(function) ||
           RequiresNativeDirectForEachCapture(function) ||
           function.Body.Find(node => node is ConvertExpressionAst conversion &&
               conversion.Type.TypeName.GetReflectionType() is { } targetType &&
               !PowerShellCompilationParameterTypePolicy.CanUseInMethod(targetType, targetFramework, capabilities),
               searchNestedScriptBlocks: false) is not null ||
           function.Body.Find(static node => node is PipelineAst pipeline &&
               PowerShellCommandRegionSemanticBinder.RequiresPipelineSyntax(pipeline) && IsCapturedPipeline(pipeline),
               searchNestedScriptBlocks: false) is not null ||
           function.Body.Find(static node => node is PipelineAst pipeline &&
               PowerShellCommandRegionSemanticBinder.RequiresNativePipelineBinding(pipeline),
               searchNestedScriptBlocks: false) is not null ||
           function.Body.Find(static node => node is MemberExpressionAst member && node is not InvokeMemberExpressionAst &&
               member.Member is not StringConstantExpressionAst, searchNestedScriptBlocks: false) is not null ||
           PowerShellParameterSyntax.GetParameters(function.Body).Any(parameter => parameter.DefaultValue is not null and not ConstantExpressionAst and not StringConstantExpressionAst ||
            parameter.Attributes.OfType<AttributeAst>().Any(attribute =>
                typeof(System.Management.Automation.ValidateArgumentsAttribute).IsAssignableFrom(attribute.TypeName.GetReflectionType() ?? typeof(object)) ||
                parameter.StaticType == typeof(string) && attribute.TypeName.Name.Equals("Parameter", StringComparison.OrdinalIgnoreCase) &&
                attribute.NamedArguments.Any(argument => argument.ArgumentName.Equals("ValueFromPipeline", StringComparison.OrdinalIgnoreCase) ||
                    argument.ArgumentName.Equals("ValueFromPipelineByPropertyName", StringComparison.OrdinalIgnoreCase))));

    /// <summary>Selects invocation-owned cardinality only when an if is itself consumed as a literal or collection value.</summary>
    private static bool RequiresNativeStatementValue(FunctionDefinitionAst function)
        => function.Body.Find(static node => node is IfStatementAst conditional &&
            (conditional.Parent is HashtableAst ||
             conditional.Parent is StatementBlockAst { Parent: ArrayExpressionAst or SubExpressionAst }),
            searchNestedScriptBlocks: false) is not null;

    private static bool RequiresNativeLiteralCommandValue(FunctionDefinitionAst function,
        PowerShellCompilationCapability capabilities)
        => function.Body.Find(node => node is HashtableAst literal &&
            literal.KeyValuePairs.Any(pair =>
                PowerShellCommandRegionSemanticBinder.IsNativeLiteralCommandValue(pair.Item2, capabilities)),
            searchNestedScriptBlocks: false) is not null;

    private static bool RequiresNativeCommandSwitch(FunctionDefinitionAst function,
        PowerShellCompilationCapability capabilities)
        => PowerShellLoopInterruptContract.IsAvailable(capabilities) &&
           function.Body.Find(node => node is SwitchStatementAst { Condition: PipelineAst pipeline } &&
               PowerShellCommandRegionSemanticBinder.IsNativeLiteralCommandValue(pipeline, capabilities),
               searchNestedScriptBlocks: false) is not null;

    private static bool RequiresNativeObjectParameterSwitch(FunctionDefinitionAst function,
        PowerShellCompilationCapability capabilities)
    {
        if (!PowerShellLoopInterruptContract.IsAvailable(capabilities)) return false;
        var parameters = PowerShellParameterSyntax.GetParameters(function.Body)
            .Where(parameter => parameter.StaticType == typeof(object))
            .Select(parameter => parameter.Name.VariablePath.UserPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return function.Body.Find(node => node is SwitchStatementAst selection &&
            string.IsNullOrEmpty(selection.Label) &&
            (selection.Flags & (SwitchFlags.Regex | SwitchFlags.Wildcard | SwitchFlags.File | SwitchFlags.Parallel)) == 0 &&
            selection.Clauses.All(clause => clause.Item1 is StringConstantExpressionAst) &&
            selection.Condition is PipelineAst pipeline && pipeline.GetPureExpression() is VariableExpressionAst variable &&
            variable.VariablePath.IsUnqualified && parameters.Contains(variable.VariablePath.UserPath),
            searchNestedScriptBlocks: false) is not null;
    }

    private static bool RequiresNativeConditionalAccessCapture(FunctionDefinitionAst function)
        => function.Body.Find(static node => node is AssignmentStatementAst
            {
                Operator: TokenKind.Equals,
                Right: IfStatementAst
            } assignment && PowerShellSemanticBinder.IsNativeConditionalAccessCaptureTarget(assignment.Left),
            searchNestedScriptBlocks: false) is not null;

    /// <summary>Selects invocation-owned lookup for a locally constructed map used outside the typed index contract.</summary>
    private static bool RequiresNativeVariableIndex(FunctionDefinitionAst function)
    {
        var maps = function.Body.FindAll(static node => node is AssignmentStatementAst
            {
                Left: VariableExpressionAst
            } assignment && IsDictionaryLiteral(assignment.Right), searchNestedScriptBlocks: false)
            .OfType<AssignmentStatementAst>()
            .Select(static assignment => ((VariableExpressionAst)assignment.Left).VariablePath.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return maps.Count > 0 && function.Body.Find(node => node is IndexExpressionAst
            {
                Target: VariableExpressionAst target
            } index && maps.Contains(target.VariablePath.ToString()) && RequiresInvocationOwnedMapRead(index),
            searchNestedScriptBlocks: false) is not null;
    }

    private static bool IsDictionaryLiteral(Ast syntax)
    {
        var value = PowerShellSemanticBinder.UnwrapExpression(syntax);
        return value is HashtableAst || value is ConvertExpressionAst conversion &&
            PowerShellDictionarySemanticBinder.IsOrderedHashtableConversion(conversion);
    }

    // A direct typed module-state read needs invocation-owned conversion, but
    // derived values that escape into locals, calls, or loops stay guarded.
    private static bool RequiresNativeModuleStateStringConversion(FunctionDefinitionAst function)
        => function.Body.Find(static node => node is ConvertExpressionAst conversion &&
            conversion.Child is VariableExpressionAst variable &&
            variable.VariablePath.IsScript &&
            conversion.Type.TypeName.GetReflectionType() is { } targetType &&
            PowerShellStringificationScopePolicy.HasStringElement(targetType) && IsDirectReturnedModuleStateRead(conversion),
            searchNestedScriptBlocks: false) is not null;

    private static bool IsDirectReturnedModuleStateRead(ConvertExpressionAst conversion)
    {
        if (conversion.Parent is not CommandExpressionAst { Parent: PipelineAst { Parent: ParenExpressionAst parenthesized } } ||
            parenthesized.Parent is not Ast read || read is InvokeMemberExpressionAst)
            return false;
        if (read is MemberExpressionAst member &&
            (member.Member is not StringConstantExpressionAst name ||
             !name.Value.Equals("Length", StringComparison.OrdinalIgnoreCase)))
            return false;
        if (read is IndexExpressionAst index && !IsSimpleIndex(PowerShellSemanticBinder.UnwrapExpression(index.Index)))
            return false;
        if (read is not MemberExpressionAst and not IndexExpressionAst) return false;
        return read.Parent is CommandExpressionAst { Parent: PipelineAst { Parent: ReturnStatementAst } };
    }

    private static bool IsSimpleIndex(Ast index)
        => index is VariableExpressionAst or ConstantExpressionAst or StringConstantExpressionAst ||
           index is UnaryExpressionAst { TokenKind: TokenKind.Plus or TokenKind.Minus, Child: ConstantExpressionAst };

    // PowerShell converts a string parameter at the CLR call boundary. The
    // typed overload binder cannot substitute a C# string-to-number cast.
    // Select the invocation bridge only when the target and arity are closed;
    // the native binder still validates target availability and call effects.
    private static bool RequiresNativeStaticNumericArgumentConversion(FunctionDefinitionAst function)
    {
        var stringParameters = PowerShellParameterSyntax.GetParameters(function.Body)
            .Where(static parameter => parameter.StaticType == typeof(string))
            .Select(static parameter => parameter.Name.VariablePath.UserPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (stringParameters.Count == 0) return false;
        return function.Body.Find(node => node is InvokeMemberExpressionAst
            {
                Static: true,
                Expression: TypeExpressionAst target,
                Member: StringConstantExpressionAst method,
                Arguments: { Count: 1 } arguments
            } &&
            arguments[0] is VariableExpressionAst argument &&
            stringParameters.Contains(argument.VariablePath.UserPath) &&
            target.TypeName.GetReflectionType() is { } type &&
            HasSingleNumericOverload(type, method.Value), searchNestedScriptBlocks: false) is not null;
    }

    private static bool HasSingleNumericOverload(Type type, string methodName)
    {
        var overloads = type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static |
                                        System.Reflection.BindingFlags.FlattenHierarchy)
            .Where(candidate => candidate.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase) &&
                                !candidate.IsSpecialName && candidate.GetParameters().Length == 1)
            .ToArray();
        if (overloads.Length != 1 || overloads[0].ContainsGenericParameters) return false;
        var parameter = overloads[0].GetParameters()[0];
        var target = parameter.ParameterType;
        return !parameter.IsOptional && !parameter.IsOut && !target.IsByRef &&
               (target == typeof(byte) || target == typeof(short) || target == typeof(int) ||
                target == typeof(long) || target == typeof(ushort) || target == typeof(uint) ||
                target == typeof(ulong));
    }

    // Preserve already-qualified typed direct returns, assignments, and
    // indexed mutation. A map read consumed by an if condition or emitted as
    // an ordinary statement instead needs the active PowerShell invocation.
    private static bool RequiresInvocationOwnedMapRead(IndexExpressionAst index)
    {
        for (Ast current = index; current.Parent is { } parent; current = parent)
        {
            if (parent is ReturnStatementAst) return false;
            if (parent is AssignmentStatementAst assignment)
                return assignment.Parent is IfStatementAst &&
                       assignment.Left.Find(node => ReferenceEquals(node, index), searchNestedScriptBlocks: false) is null;
            if (parent is FunctionDefinitionAst) break;
        }
        return true;
    }

    private static bool RequiresNativeStatementArrayCapture(FunctionDefinitionAst function)
        => function.Body.Find(static node => node is AssignmentStatementAst { Operator: TokenKind.Equals } assignment &&
            PowerShellSemanticBinder.UnwrapExpression(assignment.Right) is ArrayExpressionAst collection &&
            collection.SubExpression.FindAll(static nested => nested is ForEachStatementAst or TryStatementAst,
                searchNestedScriptBlocks: false).Any(),
            searchNestedScriptBlocks: false) is not null;

    // A direct foreach assigned to [Array] is statement-output capture, not
    // a CLR array expression. Select native binding so the existing collector
    // can preserve zero/one/many success records and the authored constraint.
    private static bool RequiresNativeDirectForEachCapture(FunctionDefinitionAst function)
        => function.Body.Find(static node => node is AssignmentStatementAst
            {
                Operator: TokenKind.Equals,
                Right: ForEachStatementAst,
                Left: AttributedExpressionAst
                {
                    Attribute: TypeConstraintAst constraint,
                    Child: VariableExpressionAst { VariablePath.IsUnqualified: true }
                }
            } && constraint.TypeName.GetReflectionType() == typeof(Array),
            searchNestedScriptBlocks: false) is not null;

    private static bool RequiresNativeObjectParameterForEach(FunctionDefinitionAst function)
    {
        var untypedParameters = PowerShellParameterSyntax.GetParameters(function.Body)
            .Where(static parameter => parameter.StaticType == typeof(object))
            .Select(static parameter => parameter.Name.VariablePath.UserPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return untypedParameters.Count > 0 && function.Body.Find(node =>
            node is ForEachStatementAst loop &&
            loop.Condition.GetPureExpression() is VariableExpressionAst source &&
            untypedParameters.Contains(source.VariablePath.UserPath), searchNestedScriptBlocks: false) is not null;
    }

    private static bool RequiresNativeDictionaryKeyForEach(FunctionDefinitionAst function)
        => function.Body.Find(static node =>
            node is ForEachStatementAst loop &&
            loop.Condition.GetPureExpression() is MemberExpressionAst
            {
                Expression: VariableExpressionAst,
                Member: StringConstantExpressionAst member
            } &&
            member.Value.Equals("Keys", StringComparison.OrdinalIgnoreCase),
            searchNestedScriptBlocks: false) is not null;

    /// <summary>Identifies command results consumed by an expression or assignment in this invocation.</summary>
    private static bool IsCapturedPipeline(PipelineAst pipeline)
    {
        for (var parent = pipeline.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is AssignmentStatementAst or ParenExpressionAst or ArrayExpressionAst or SubExpressionAst)
                return true;
            if (parent is ScriptBlockAst or FunctionDefinitionAst or NamedBlockAst)
                return false;
        }
        return false;
    }
}
