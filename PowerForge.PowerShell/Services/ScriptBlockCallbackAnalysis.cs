using System;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Separates caller-supplied callbacks from computed module command names.</summary>
internal static class ScriptBlockCallbackAnalysis
{
    internal static bool IsParameterCallback(CommandAst command)
    {
        // Dot-sourcing can alter the module scope; only the call operator is a callback boundary.
        if (command.InvocationOperator != TokenKind.Ampersand ||
            command.CommandElements.FirstOrDefault() is not VariableExpressionAst variable ||
            !variable.VariablePath.IsUnqualified)
            return false;

        var name = variable.VariablePath.UserPath;
        ScriptBlockAst? scope = null;
        for (var parent = command.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is ScriptBlockAst scriptBlock)
            {
                scope = scriptBlock;
                break;
            }
        }
        if (scope?.ParamBlock is null)
            return false;

        var parameter = scope.ParamBlock.Parameters.FirstOrDefault(candidate =>
            string.Equals(candidate.Name.VariablePath.UserPath, name, StringComparison.OrdinalIgnoreCase));
        if (parameter is null || parameter.StaticType != typeof(ScriptBlock) || parameter.DefaultValue is not null)
            return false;

        // A typed parameter may subsequently be replaced with generated code. Keep the
        // conservative dependency rule when the callback's binding is changed locally.
        return !scope.FindAll(node =>
        {
            if (node is AssignmentStatementAst assignment)
                return assignment.Left.FindAll(child => IsVariable(child, name), true).Any();
            if (node is ForEachStatementAst loop)
                return IsVariable(loop.Variable, name);
            if (node is ConvertExpressionAst conversion && conversion.Type.TypeName.FullName.Equals("ref", StringComparison.OrdinalIgnoreCase))
                return conversion.Child.FindAll(child => IsVariable(child, name), true).Any();
            if (node is CommandAst invocation)
            {
                var commandName = invocation.GetCommandName()?.Split('\\').Last();
                return string.Equals(commandName, "Set-Variable", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(commandName, "New-Variable", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(commandName, "sv", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(commandName, "nv", StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }, searchNestedScriptBlocks: true).Any();
    }

    private static bool IsVariable(Ast node, string name) =>
        node is VariableExpressionAst variable &&
        string.Equals(variable.VariablePath.UserPath.Split(':').Last(), name, StringComparison.OrdinalIgnoreCase);
}
