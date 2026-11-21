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

    private string? FindDonorContextVariable(ScriptBlock body, ApprovedModuleSource? source)
    {
        HashSet<string>? moduleVariableNames = null;
        foreach (var variable in body.Ast.FindAll(static node => node is VariableExpressionAst, searchNestedScriptBlocks: true)
                     .Cast<VariableExpressionAst>())
        {
            var path = variable.VariablePath;
            string name = path.UserPath;
            if (path.IsScript ||
                name.Equals("PSScriptRoot", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("PSCommandPath", StringComparison.OrdinalIgnoreCase))
                return name;
            if (name.Equals("MyInvocation", StringComparison.OrdinalIgnoreCase))
            {
                if (!ReadsCurrentFunctionName(variable))
                    return name;
                continue;
            }
            if (source is null || !path.IsUnqualified || !IsPotentialModuleVariable(name))
                continue;

            moduleVariableNames ??= GetModuleScriptVariableNames(source);
            if (moduleVariableNames.Contains(name) && !HasLocalBinding(variable))
                return name;
        }
        return null;
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

    private static bool HasLocalBinding(VariableExpressionAst variable)
    {
        string name = variable.VariablePath.UserPath;
        var comparer = StringComparer.OrdinalIgnoreCase;

        if (variable.Parent is ParameterAst parameter && ReferenceEquals(parameter.Name, variable) ||
            variable.Parent is ForEachStatementAst declaration && ReferenceEquals(declaration.Variable, variable))
            return true;

        Ast target = variable.Parent is ConvertExpressionAst conversion ? conversion : variable;
        if (target.Parent is AssignmentStatementAst { Operator: TokenKind.Equals } assignment &&
            ReferenceEquals(assignment.Left, target))
            return true;

        // A direct earlier assignment in the same executing statement block initializes this local.
        // Assignments in sibling branches or nested functions do not prove the binding.
        bool inParameterDefault = false;
        for (Ast? node = variable; node is not null; node = node.Parent)
        {
            if (node is ParameterAst)
                inParameterDefault = true;
            if (node is ScriptBlockAst scope)
            {
                if (inParameterDefault)
                    return false;
                var parameters = scope.ParamBlock?.Parameters ?? (scope.Parent as FunctionDefinitionAst)?.Parameters;
                if (parameters?.Any(item => comparer.Equals(item.Name.VariablePath.UserPath, name)) == true)
                    return true;
                // begin runs in the same function scope before process and end.
                return scope.BeginBlock is { } begin && begin.Extent.EndOffset <= variable.Extent.StartOffset &&
                       HasPriorAssignment(begin.Statements, variable, name);
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
