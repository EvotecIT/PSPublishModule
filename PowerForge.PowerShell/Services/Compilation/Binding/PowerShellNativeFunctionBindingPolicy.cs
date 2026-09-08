using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Selects native invocation storage when parameter callbacks can invalidate CLR storage facts.</summary>
internal static class PowerShellNativeFunctionBindingPolicy
{
    internal static bool IsInsideExpandableString(Ast syntax)
    {
        for (var parent = syntax.Parent; parent is not null; parent = parent.Parent)
            if (parent is ExpandableStringExpressionAst) return true;
        return false;
    }

    internal static PowerShellNativeFunctionBinding? Select(FunctionDefinitionAst function, PowerShellCompilationCapability capabilities)
    {
        if (!capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding) ||
            function.Body.BeginBlock is not null || function.Body.ProcessBlock is not null ||
            function.Body.DynamicParamBlock is not null || function.Body.GetType().GetProperty("CleanBlock")?.GetValue(function.Body) is not null)
            return null;
        var parameters = PowerShellParameterSyntax.GetParameters(function.Body).ToArray();
        var requiresNative = parameters.Any(parameter => parameter.DefaultValue is not null and not ConstantExpressionAst and not StringConstantExpressionAst ||
            parameter.Attributes.OfType<AttributeAst>().Any(attribute =>
                typeof(System.Management.Automation.ValidateArgumentsAttribute).IsAssignableFrom(attribute.TypeName.GetReflectionType() ?? typeof(object)) ||
                parameter.StaticType == typeof(string) && attribute.TypeName.Name.Equals("Parameter", StringComparison.OrdinalIgnoreCase) &&
                attribute.NamedArguments.Any(argument => argument.ArgumentName.Equals("ValueFromPipeline", StringComparison.OrdinalIgnoreCase) ||
                    argument.ArgumentName.Equals("ValueFromPipelineByPropertyName", StringComparison.OrdinalIgnoreCase))));
        if (!requiresNative) return null;
        var declaration = function.Body.ParamBlock is { } block
            ? string.Join("\n", block.Attributes.Select(static attribute => attribute.Extent.Text).Concat(new[] { block.Extent.Text }))
            : "param(" + string.Join(",", parameters.Select(static parameter => parameter.Extent.Text)) + ")";
        return new PowerShellNativeFunctionBinding(declaration, PowerShellNativeVariableAnalysis.Analyze(function));
    }
}
