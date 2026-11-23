using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Finds packaged-script runtime and host access that cannot be validated for an embedded runspace.
/// </summary>
internal static class PowerShellPackagedHostAccessPolicy
{
    internal static VariableExpressionAst? FindExecutionContextHostAccess(ScriptBlockAst ast)
        => ast.FindAll(
                static node => node is VariableExpressionAst variable &&
                               variable.VariablePath.UserPath.Equals("ExecutionContext", StringComparison.OrdinalIgnoreCase),
                searchNestedScriptBlocks: true)
            .Cast<VariableExpressionAst>()
            .FirstOrDefault(static variable => IsUnsafeExecutionContextAccess(variable));

    internal static VariableExpressionAst? FindPSCmdletHostAccess(ScriptBlockAst ast)
        => ast.FindAll(
                static node => node is VariableExpressionAst variable &&
                               variable.VariablePath.UserPath.Equals("PSCmdlet", StringComparison.OrdinalIgnoreCase),
                searchNestedScriptBlocks: true)
            .Cast<VariableExpressionAst>()
            .FirstOrDefault(static variable =>
            {
                if (variable.Parent is not MemberExpressionAst member || !ReferenceEquals(member.Expression, variable))
                    return true;
                return member.Member is not StringConstantExpressionAst name ||
                       name.Value.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                       name.Value.Equals("InvokeCommand", StringComparison.OrdinalIgnoreCase);
            });

    internal static Ast? FindDynamicScriptEvaluation(ScriptBlockAst ast)
    {
        var command = ast.FindAll(static node => node is CommandAst, searchNestedScriptBlocks: true)
            .Cast<CommandAst>()
            .FirstOrDefault(static candidate =>
            {
                var name = candidate.GetCommandName()?.Split('\\').Last();
                return name?.Equals("Invoke-Expression", StringComparison.OrdinalIgnoreCase) == true ||
                       name?.Equals("iex", StringComparison.OrdinalIgnoreCase) == true;
            });
        if (command is not null)
            return command;

        var scriptBlockFactory = ast.FindAll(static node => node is InvokeMemberExpressionAst, searchNestedScriptBlocks: true)
            .Cast<InvokeMemberExpressionAst>()
            .FirstOrDefault(static invocation =>
                invocation.Expression is TypeExpressionAst type &&
                IsScriptBlockType(type.TypeName.FullName) &&
                invocation.Member is StringConstantExpressionAst member &&
                member.Value.Equals("Create", StringComparison.OrdinalIgnoreCase));
        if (scriptBlockFactory is not null)
            return scriptBlockFactory;

        return ast.FindAll(static node => node is ConvertExpressionAst, searchNestedScriptBlocks: true)
            .Cast<ConvertExpressionAst>()
            .FirstOrDefault(static conversion => IsScriptBlockType(conversion.Type.TypeName.FullName));
    }

    private static bool IsUnsafeExecutionContextAccess(VariableExpressionAst variable)
    {
        if (variable.Parent is not MemberExpressionAst member || !ReferenceEquals(member.Expression, variable))
            return true;
        if (member.Member is not StringConstantExpressionAst name)
            return true;
        if (name.Value.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
            name.Value.Equals("InvokeCommand", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!name.Value.Equals("SessionState", StringComparison.OrdinalIgnoreCase))
            return false;

        if (member.Parent is not MemberExpressionAst next || !ReferenceEquals(next.Expression, member))
            return true;
        return next.Member is not StringConstantExpressionAst nextName ||
               nextName.Value.Equals("InvokeCommand", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsScriptBlockType(string typeName)
        => typeName.Equals("scriptblock", StringComparison.OrdinalIgnoreCase) ||
           typeName.Equals("System.Management.Automation.ScriptBlock", StringComparison.OrdinalIgnoreCase);
}
