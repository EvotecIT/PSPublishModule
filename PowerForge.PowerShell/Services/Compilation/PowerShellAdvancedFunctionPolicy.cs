using System.Management.Automation.Language;

namespace PowerForge;

internal static class PowerShellAdvancedFunctionPolicy
{
    internal static bool IsAdvanced(FunctionDefinitionAst function)
    {
        var parameterBlock = function.Body.ParamBlock;
        return parameterBlock is not null &&
               (parameterBlock.Attributes.OfType<AttributeAst>().Any(static attribute => IsAttributeNamed(attribute, "CmdletBinding")) ||
                parameterBlock.Parameters.Any(static parameter =>
                    parameter.Attributes.OfType<AttributeAst>().Any(static attribute => IsAttributeNamed(attribute, "Parameter"))));
    }

    private static bool IsAttributeNamed(AttributeAst attribute, string name)
    {
        var fullName = attribute.TypeName.FullName;
        return fullName.Equals(name, StringComparison.OrdinalIgnoreCase) ||
               fullName.Equals(name + "Attribute", StringComparison.OrdinalIgnoreCase) ||
               fullName.EndsWith("." + name + "Attribute", StringComparison.OrdinalIgnoreCase);
    }
}
