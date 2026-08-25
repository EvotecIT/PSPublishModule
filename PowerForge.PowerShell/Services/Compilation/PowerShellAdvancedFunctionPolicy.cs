using System.Management.Automation.Language;

namespace PowerForge;

internal static class PowerShellAdvancedFunctionPolicy
{
    internal static bool IsAdvanced(FunctionDefinitionAst function)
        => IsAdvanced(function.Body.ParamBlock);

    internal static bool IsAdvanced(ParamBlockAst? parameterBlock)
    {
        return parameterBlock is not null &&
               (parameterBlock.Attributes.OfType<AttributeAst>().Any(static attribute => IsAttributeNamed(attribute, "CmdletBinding")) ||
                 parameterBlock.Parameters.Any(static parameter =>
                     parameter.Attributes.OfType<AttributeAst>().Any(static attribute => IsAttributeNamed(attribute, "Parameter"))));
    }

    internal static bool SupportsShouldProcess(ParamBlockAst? parameterBlock)
    {
        var cmdletBinding = parameterBlock?.Attributes
            .OfType<AttributeAst>()
            .FirstOrDefault(static attribute => IsAttributeNamed(attribute, "CmdletBinding"));
        if (cmdletBinding is null)
            return false;
        var argument = cmdletBinding.NamedArguments.FirstOrDefault(static named =>
            named.ArgumentName.Equals("SupportsShouldProcess", StringComparison.OrdinalIgnoreCase));
        return argument is not null && IsTrue(argument.Argument);
    }

    private static bool IsTrue(ExpressionAst expression)
        => expression is ConstantExpressionAst { Value: true } ||
           expression is VariableExpressionAst variable &&
           variable.VariablePath.UserPath.Equals("true", StringComparison.OrdinalIgnoreCase);

    private static bool IsAttributeNamed(AttributeAst attribute, string name)
    {
        var fullName = attribute.TypeName.FullName;
        return fullName.Equals(name, StringComparison.OrdinalIgnoreCase) ||
               fullName.Equals(name + "Attribute", StringComparison.OrdinalIgnoreCase) ||
               fullName.EndsWith("." + name + "Attribute", StringComparison.OrdinalIgnoreCase);
    }
}
