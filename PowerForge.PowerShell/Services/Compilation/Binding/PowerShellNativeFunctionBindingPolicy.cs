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
        PowerShellCompilationCapability capabilities)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding)) return result;
        var declarations = functions.GroupBy(static function => function.Name, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() == 1)
            .ToDictionary(static group => group.Key, static group => group.Single(), StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<FunctionDefinitionAst>(declarations.Values.Where(RequiresNativeBinding));
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
        bool requiresNativeInvocation = false)
    {
        if (!capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding) ||
            function.Body.DynamicParamBlock is not null)
            return null;
        var parameters = PowerShellParameterSyntax.GetParameters(function.Body).ToArray();
        if (!requiresNativeInvocation && !RequiresNativeBinding(function)) return null;
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

    private static bool RequiresNativeBinding(FunctionDefinitionAst function)
        => function.Body.BeginBlock is not null || function.Body.ProcessBlock is not null ||
           function.Body.Find(static node => node is ScriptBlockExpressionAst block && PowerShellSemanticBinder.IsAssignedScriptBlock(block), searchNestedScriptBlocks: true) is not null ||
           function.Body.GetType().GetProperty("CleanBlock")?.GetValue(function.Body) is not null ||
           FindNativePipelineOperator(function) is not null ||
           function.Body.Find(static node =>
               node is ConvertExpressionAst conversion && conversion.Parent is CommandExpressionAst { Parent: PipelineAst discardPipeline } &&
                   PowerShellCompilationConversionPolicy.IsStatementDiscard(conversion) && IsCapturedPipeline(discardPipeline) ||
               node is SubExpressionAst or BinaryExpressionAst { Operator: TokenKind.Join or TokenKind.Ireplace or TokenKind.Creplace } or
               UnaryExpressionAst { TokenKind: TokenKind.Join }, searchNestedScriptBlocks: false) is not null ||
           function.Body.Find(static node => node is PipelineAst pipeline &&
               PowerShellCommandRegionSemanticBinder.RequiresPipelineSyntax(pipeline) && IsCapturedPipeline(pipeline),
               searchNestedScriptBlocks: false) is not null ||
           PowerShellParameterSyntax.GetParameters(function.Body).Any(parameter => parameter.DefaultValue is not null and not ConstantExpressionAst and not StringConstantExpressionAst ||
            parameter.Attributes.OfType<AttributeAst>().Any(attribute =>
                typeof(System.Management.Automation.ValidateArgumentsAttribute).IsAssignableFrom(attribute.TypeName.GetReflectionType() ?? typeof(object)) ||
                parameter.StaticType == typeof(string) && attribute.TypeName.Name.Equals("Parameter", StringComparison.OrdinalIgnoreCase) &&
                attribute.NamedArguments.Any(argument => argument.ArgumentName.Equals("ValueFromPipeline", StringComparison.OrdinalIgnoreCase) ||
                    argument.ArgumentName.Equals("ValueFromPipelineByPropertyName", StringComparison.OrdinalIgnoreCase))));

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
