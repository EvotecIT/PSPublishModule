using System.Management.Automation.Language;

namespace PowerForge;

internal static class PowerShellStrictParameterDefaultPolicy
{
    internal static bool UsesFileSystemCurrentDirectory(ParameterAst parameter)
    {
        if (parameter.StaticType != typeof(string)) return false;
        var variable = parameter.DefaultValue switch
        {
            VariableExpressionAst direct => direct,
            ExpandableStringExpressionAst { NestedExpressions.Count: 1 } text
                when text.NestedExpressions[0] is VariableExpressionAst nested &&
                     text.Value.Equals(nested.Extent.Text, StringComparison.Ordinal) => nested,
            _ => null
        };
        return variable is { VariablePath.IsUnqualified: true } &&
            variable.VariablePath.UserPath.Equals("PWD", StringComparison.OrdinalIgnoreCase);
    }
}
