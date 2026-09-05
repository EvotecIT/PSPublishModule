using System.Management.Automation.Language;

namespace PowerForge;

internal static class PowerShellAdvancedFunctionPolicy
{
    internal static string[] GetAliases(FunctionDefinitionAst function)
        => function.Body.ParamBlock?.Attributes
            .OfType<AttributeAst>()
            .Where(static attribute =>
                IsAttributeNamed(attribute, "Alias") ||
                IsAttributeNamed(attribute, "AliasAttribute"))
            .SelectMany(static attribute => attribute.PositionalArguments.OfType<StringConstantExpressionAst>())
            .Select(static alias => alias.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();

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

    internal static PowerShellCompilationCommandBinding GetBinding(ParamBlockAst? parameterBlock)
    {
        var advanced = IsAdvanced(parameterBlock);
        var cmdletBinding = parameterBlock?.Attributes
            .OfType<AttributeAst>()
            .FirstOrDefault(static attribute => IsAttributeNamed(attribute, "CmdletBinding"));
        if (cmdletBinding is null)
            return new PowerShellCompilationCommandBinding(advanced);

        var positional = GetBoolean(cmdletBinding, "PositionalBinding", defaultValue: true);
        var supportsShouldProcess = GetBoolean(cmdletBinding, "SupportsShouldProcess", defaultValue: false);
        return new PowerShellCompilationCommandBinding(
            advanced,
            positional,
            GetString(cmdletBinding, "DefaultParameterSetName"),
            supportsShouldProcess,
            GetString(cmdletBinding, "ConfirmImpact"));
    }

    private static bool IsTrue(ExpressionAst expression)
        => expression is ConstantExpressionAst { Value: true } ||
           expression is VariableExpressionAst variable &&
           variable.VariablePath.UserPath.Equals("true", StringComparison.OrdinalIgnoreCase);

    private static bool GetBoolean(AttributeAst attribute, string name, bool defaultValue)
    {
        var argument = attribute.NamedArguments.FirstOrDefault(candidate =>
            candidate.ArgumentName.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (argument is null)
            return defaultValue;
        try
        {
            return argument.Argument.SafeGetValue() is bool value ? value : defaultValue;
        }
        catch (InvalidOperationException)
        {
            return defaultValue;
        }
    }

    private static string GetString(AttributeAst attribute, string name)
    {
        var argument = attribute.NamedArguments.FirstOrDefault(candidate =>
            candidate.ArgumentName.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (argument is null)
            return string.Empty;
        try
        {
            return argument.Argument.SafeGetValue() as string ?? string.Empty;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private static bool IsAttributeNamed(AttributeAst attribute, string name)
    {
        var fullName = attribute.TypeName.FullName;
        return fullName.Equals(name, StringComparison.OrdinalIgnoreCase) ||
               fullName.Equals(name + "Attribute", StringComparison.OrdinalIgnoreCase) ||
               fullName.EndsWith("." + name + "Attribute", StringComparison.OrdinalIgnoreCase);
    }
}
