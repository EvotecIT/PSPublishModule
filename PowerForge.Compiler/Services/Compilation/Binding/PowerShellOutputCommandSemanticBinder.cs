using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Qualifies explicit non-enumerating output without changing ordinary provider binding.</summary>
internal static class PowerShellOutputCommandSemanticBinder
{
    internal static bool TryBindNoEnumerate(
        CommandElementAst[] arguments,
        out ExpressionAst message,
        out PowerShellOutputBindingKind binding)
    {
        message = null!;
        binding = PowerShellOutputBindingKind.Default;
        var noEnumerate = false;
        var named = false;
        for (var index = 0; index < arguments.Length; index++)
        {
            if (arguments[index] is CommandParameterAst parameter)
            {
                if (parameter.ParameterName.Equals("NoEnumerate", StringComparison.OrdinalIgnoreCase))
                {
                    if (noEnumerate || parameter.Argument is not null &&
                        parameter.Argument is not VariableExpressionAst { VariablePath.UserPath: "true" })
                        return false;
                    noEnumerate = true;
                    continue;
                }
                if (!parameter.ParameterName.Equals("InputObject", StringComparison.OrdinalIgnoreCase) || message is not null)
                    return false;
                named = true;
                if (parameter.Argument is ExpressionAst inline)
                    message = inline;
                else if (++index < arguments.Length && arguments[index] is ExpressionAst value)
                    message = value;
                else
                    return false;
            }
            else if (arguments[index] is ExpressionAst positional && message is null)
                message = positional;
            else
                return false;
        }
        if (!noEnumerate || message is null || message is VariableExpressionAst { Splatted: true })
            return false;
        binding = named ? PowerShellOutputBindingKind.NoEnumerate : PowerShellOutputBindingKind.PositionalNoEnumerate;
        return true;
    }
}
