using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;

namespace PowerForge;

public sealed partial class MissingFunctionsAnalyzer
{
    private static bool MatchesObservedModuleIdentity(PSModuleInfo? observed, PSModuleInfo selected)
        => observed is not null &&
           string.Equals(observed.Name, selected.Name, StringComparison.OrdinalIgnoreCase) &&
           observed.Version == selected.Version &&
           string.Equals(GetModulePrerelease(observed), GetModulePrerelease(selected), StringComparison.OrdinalIgnoreCase) &&
           observed.Guid == selected.Guid &&
           FrameworkCompatibility.PathComparer.Equals(observed.ModuleBase, selected.ModuleBase);

    private string? FindDonorContextVariable(
        ScriptBlockAst root,
        ApprovedModuleSource? source,
        HashSet<string> consumerFunctions,
        AuthoredAliases consumerAliases)
    {
        HashSet<string>? moduleVariableNames = null;
        NestedFunctionVisibilityAnalyzer? visibility = null;
        bool? canRemoveVariables = null;
        foreach (var variable in root.FindAll(static node => node is VariableExpressionAst, searchNestedScriptBlocks: true)
                     .Cast<VariableExpressionAst>())
        {
            var path = variable.VariablePath;
            string name = PowerShellAssignmentTargetPolicy.GetUnscopedVariableName(path.UserPath);
            if (path.IsScript ||
                name.Equals("PSScriptRoot", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("PSCommandPath", StringComparison.OrdinalIgnoreCase))
                return path.UserPath;
            if (name.Equals("MyInvocation", StringComparison.OrdinalIgnoreCase))
            {
                if (!ReadsCurrentFunctionName(variable))
                    return path.UserPath;
                continue;
            }
            if (source is null || !path.IsUnqualified || !IsPotentialModuleVariable(name))
                continue;

            moduleVariableNames ??= GetModuleScriptVariableNames(source);
            if (!moduleVariableNames.Contains(name) || IsLocalDeclaration(variable))
                continue;
            canRemoveVariables ??= root.Find(static node => node is CommandAst invocation &&
                invocation.GetCommandName() is { } commandName && IsVariableRemovalCommand(commandName),
                searchNestedScriptBlocks: true) is not null;
            if (canRemoveVariables.Value || !HasLocalBinding(variable, root, CanReadEnclosingScope))
                return path.UserPath;
        }
        return null;

        bool CanReadEnclosingScope(ScriptBlockExpressionAst callback)
        {
            visibility ??= NestedFunctionVisibilityAnalyzer.Create(root);
            if (!visibility.IsSynchronousScriptBlock(callback))
                return false;
            if (callback.Parent is not CommandAst invocation)
                return false;
            if (invocation.InvocationOperator is TokenKind.Ampersand or TokenKind.Dot &&
                ReferenceEquals(invocation.CommandElements[0], callback))
                return true;
            if (source is null || invocation.GetCommandName() is not { } commandName)
                return false;

            string name = GetUnqualifiedCommandName(commandName);
            if (GetModuleQualifier(commandName) is null &&
                (consumerFunctions.Contains(name) || consumerAliases.Names.Contains(name)))
                return false;

            var command = GetCommandFromModuleScopeCached(source, commandName, includePrivate: true, requireOwnership: false);
            if (command is AliasInfo alias)
                command = alias.ResolvedCommand;
            return command is CmdletInfo cmdlet &&
                   cmdlet.Source.StartsWith("Microsoft.PowerShell.", StringComparison.OrdinalIgnoreCase);
        }
    }

    private HashSet<string> GetModuleScriptVariableNames(ApprovedModuleSource source)
    {
        var session = GetOrCreateModuleScopeSession(source);
        if (session.ScriptVariableNames is { } cached)
            return cached;

        using var ps = PowerShell.Create();
        ps.Runspace = session.Runspace;
        ps.AddScript(@"
param($module)
& $module {
    Microsoft.PowerShell.Utility\Get-Variable -Scope Script -ErrorAction Stop |
        Microsoft.PowerShell.Utility\Select-Object -ExpandProperty Name
}
").AddArgument(session.Module);
        var names = ps.Invoke<string>();
        if (ps.HadErrors)
            throw new InvalidOperationException($"Cannot inspect variable names in approved module '{source.Name}'.");
        return session.ScriptVariableNames = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
    }

    private static bool ReadsCurrentFunctionName(VariableExpressionAst variable)
        => variable.Parent is MemberExpressionAst { Static: false, Member: StringConstantExpressionAst command } member &&
           member is not InvokeMemberExpressionAst &&
           ReferenceEquals(member.Expression, variable) &&
           command.Value.Equals("MyCommand", StringComparison.OrdinalIgnoreCase) &&
           member.Parent is MemberExpressionAst { Static: false, Member: StringConstantExpressionAst name } access &&
           access is not InvokeMemberExpressionAst &&
           ReferenceEquals(access.Expression, member) &&
           name.Value.Equals("Name", StringComparison.OrdinalIgnoreCase);

    private static bool IsPotentialModuleVariable(string name)
        => !name.Equals("args", StringComparison.OrdinalIgnoreCase) &&
           !name.Equals("Error", StringComparison.OrdinalIgnoreCase) &&
           !name.Equals("null", StringComparison.OrdinalIgnoreCase) &&
           !name.Equals("true", StringComparison.OrdinalIgnoreCase) &&
           !name.Equals("false", StringComparison.OrdinalIgnoreCase);

    private static bool IsVariableRemovalCommand(string commandName)
        => GetUnqualifiedCommandName(commandName) is { } leaf &&
           (leaf.Equals("Remove-Variable", StringComparison.OrdinalIgnoreCase) ||
            leaf.Equals("rv", StringComparison.OrdinalIgnoreCase));

    private static bool IsLocalDeclaration(VariableExpressionAst variable)
    {
        if (variable.Parent is ParameterAst parameter && ReferenceEquals(parameter.Name, variable) ||
            variable.Parent is ForEachStatementAst loop && ReferenceEquals(loop.Variable, variable))
            return true;

        Ast target = variable.Parent is ConvertExpressionAst conversion ? conversion : variable;
        return target.Parent is AssignmentStatementAst { Operator: TokenKind.Equals } assignment &&
               ReferenceEquals(assignment.Left, target);
    }

    private static bool HasLocalBinding(
        VariableExpressionAst variable,
        ScriptBlockAst root,
        Func<ScriptBlockExpressionAst, bool> canReadEnclosingScope)
    {
        string name = variable.VariablePath.UserPath;
        var comparer = StringComparer.OrdinalIgnoreCase;
        // A direct earlier assignment in the same executing statement block initializes this local.
        // Assignments in sibling branches or nested functions do not prove the binding.
        ParameterAst? defaultParameter = null;
        for (Ast? node = variable; node is not null; node = node.Parent)
        {
            if (node.Parent is ParameterAst parameterAst && ReferenceEquals(parameterAst.DefaultValue, node))
                defaultParameter = parameterAst;
            if (node is ScriptBlockAst scope)
            {
                var parameters = (scope.ParamBlock?.Parameters ?? (scope.Parent as FunctionDefinitionAst)?.Parameters)
                    ?.TakeWhile(item => !ReferenceEquals(item, defaultParameter));
                if (parameters?.Any(item => comparer.Equals(item.Name.VariablePath.UserPath, name)) == true)
                    return true;
                // begin runs in the same function scope before process and end.
                if (defaultParameter is null && scope.BeginBlock is { } begin &&
                    begin.Extent.EndOffset <= variable.Extent.StartOffset && HasPriorAssignment(begin.Statements, variable, name))
                    return true;
                if (ReferenceEquals(scope, root) || scope.Parent is not ScriptBlockExpressionAst callback ||
                    !canReadEnclosingScope(callback))
                    return false;
            }
            if (node.Parent is ForEachStatementAst loop && ReferenceEquals(loop.Body, node) &&
                comparer.Equals(loop.Variable.VariablePath.UserPath, name))
                return true;
            if (node is StatementAst && node.Parent is StatementBlockAst statements &&
                HasPriorAssignment(statements.Statements, node, name))
                return true;
            if (node is StatementAst && node.Parent is NamedBlockAst named &&
                HasPriorAssignment(named.Statements, node, name))
                return true;
        }
        return false;
    }

    private static bool HasPriorAssignment(IEnumerable<StatementAst> statements, Ast before, string name)
        => statements.OfType<AssignmentStatementAst>().Any(statement =>
            statement.Operator == TokenKind.Equals &&
            statement.Extent.EndOffset <= before.Extent.StartOffset &&
            PowerShellAssignmentTargetPolicy.FindDirectVariable(statement.Left) is { } assigned &&
            (assigned.VariablePath.IsUnqualified || assigned.VariablePath.IsLocal) &&
            string.Equals(PowerShellAssignmentTargetPolicy.GetUnscopedVariableName(assigned.VariablePath.UserPath),
                name, StringComparison.OrdinalIgnoreCase));
}
