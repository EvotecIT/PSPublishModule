using System.Management.Automation.Language;
using System.Reflection;

namespace PowerForge;

/// <summary>Binds the deliberately narrow capture-free Boolean delegate contract.</summary>
internal static class PowerShellConstantBooleanDelegateSemanticBinder
{
    internal static PowerShellBoundExpression? Bind(
        ParsedSourceDocument document,
        ScriptBlockExpressionAst syntax,
        Type? contextualType,
        string? targetFramework,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var span = PowerShellSourceParser.GetSpan(document, syntax.Extent);
        if (contextualType is null || !typeof(MulticastDelegate).IsAssignableFrom(contextualType))
            return Reject(diagnostics, "Nested script blocks require a typed delegate or explicit hosted PowerShell boundary.", span);
        if (!PowerShellGeneratedTypePolicy.IsSupportedDelegateSignature(contextualType, targetFramework))
            return Reject(diagnostics, $"Delegate type '{contextualType.FullName}' does not have a closed target-compatible CLR signature.", span);

        var invoke = contextualType.GetMethod("Invoke", BindingFlags.Public | BindingFlags.Instance)!;
        var parameters = invoke.GetParameters();
        if (invoke.ReturnType != typeof(bool))
            return Reject(diagnostics, $"Typed script-block conversion currently requires a Boolean-returning delegate; '{contextualType.FullName}' returns '{invoke.ReturnType.FullName}'.", span);

        var block = syntax.ScriptBlock;
        if (block.DynamicParamBlock is not null ||
            block.BeginBlock is not null ||
            block.ProcessBlock is not null ||
            GetCleanBlock(block) is not null ||
            block.EndBlock is null ||
            block.EndBlock.Traps?.Count > 0 ||
            block.Attributes?.Count > 0 ||
            block.UsingStatements?.Count > 0 ||
            block.ScriptRequirements is not null)
            return Reject(diagnostics, "Typed Boolean delegates accept only a plain end block with no lifecycle blocks, traps, attributes, requirements, or using statements.", span);

        IReadOnlyList<ParameterAst> authoredParameters = block.ParamBlock?.Parameters ?? (IReadOnlyList<ParameterAst>)Array.Empty<ParameterAst>();
        if (authoredParameters.Count != 0 && authoredParameters.Count != parameters.Length)
            return Reject(diagnostics, $"Typed Boolean delegate parameters must be omitted or match the target delegate arity of {parameters.Length}.", span);
        if (authoredParameters.Any(static parameter =>
                parameter.Attributes.Count != 0 ||
                parameter.DefaultValue is not null ||
                !parameter.Name.VariablePath.IsUnqualified ||
                !parameter.Name.VariablePath.IsVariable ||
                PowerShellAssignmentTargetPolicy.IsReadOnlyAutomaticVariable(parameter.Name.VariablePath.UserPath)))
            return Reject(diagnostics, "Typed Boolean delegate parameters must be simple untyped, unqualified, writable names without attributes or default values.", span);
        if (block.EndBlock.Statements.Count != 1 || !TryGetBoolean(block.EndBlock.Statements[0], out var value))
            return Reject(diagnostics, "Typed Boolean delegates currently accept exactly '$true', '$false', 'return $true', or 'return $false'.", span);

        return new PowerShellBoundConstantBooleanDelegateExpression(
            span,
            contextualType,
            parameters.Select(static parameter => parameter.ParameterType).ToArray(),
            value);
    }

    private static bool TryGetBoolean(StatementAst statement, out bool value)
    {
        var pipeline = statement switch
        {
            ReturnStatementAst returned => returned.Pipeline,
            PipelineAst direct => direct,
            _ => null
        };
        if (pipeline is PipelineAst
            {
                PipelineElements.Count: 1,
                PipelineElements: var elements
            } &&
            !IsBackground(pipeline) &&
            elements[0] is CommandExpressionAst
            {
                Redirections.Count: 0,
                Expression: VariableExpressionAst variable
            })
        {
            if (variable.VariablePath.UserPath.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                value = true;
                return true;
            }
            if (variable.VariablePath.UserPath.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                value = false;
                return true;
            }
        }
        value = false;
        return false;
    }

    private static NamedBlockAst? GetCleanBlock(ScriptBlockAst scriptBlock)
        => scriptBlock.GetType().GetProperty("CleanBlock")?.GetValue(scriptBlock) as NamedBlockAst;

    private static bool IsBackground(Ast pipeline)
        => pipeline.GetType().GetProperty("Background")?.GetValue(pipeline) is true;

    private static PowerShellBoundExpression? Reject(
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        string message,
        SourceSpan span)
    {
        diagnostics.Add(new PowerShellSemanticDiagnostic(PowerShellCompilationFeatureIds.ScriptBlock, message, span));
        return null;
    }
}
