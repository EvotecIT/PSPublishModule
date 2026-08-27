using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Defines the conservative assignment targets that can preserve PowerShell semantics in typed CLR code.
/// </summary>
internal static class PowerShellAssignmentTargetPolicy
{
    private static readonly HashSet<string> ReadOnlyAutomaticVariables = new(StringComparer.OrdinalIgnoreCase)
    {
        "?",
        "ConsoleFileName",
        "EnabledExperimentalFeatures",
        "ExecutionContext",
        "false",
        "HOME",
        "Host",
        "IsCoreCLR",
        "IsLinux",
        "IsMacOS",
        "IsWindows",
        "PID",
        "PSCulture",
        "PSEdition",
        "PSHOME",
        "PSUICulture",
        "PSVersionTable",
        "ShellId",
        "true"
    };

    /// <summary>Returns the directly assigned local variable, including an explicit typed declaration.</summary>
    internal static VariableExpressionAst? FindDirectVariable(ExpressionAst left)
        => left switch
        {
            VariableExpressionAst variable => variable,
            ConvertExpressionAst { Child: VariableExpressionAst variable } => variable,
            _ => null
        };

    /// <summary>Returns whether the name is a non-shadowable PowerShell read-only or constant automatic variable.</summary>
    internal static bool IsReadOnlyAutomaticVariable(string name)
        => ReadOnlyAutomaticVariables.Contains(name);

    /// <summary>Returns whether a parameter name collides with an automatic variable that is read-only on the selected runtime.</summary>
    internal static bool IsReadOnlyAutomaticParameter(string name, string? targetFramework)
    {
        if (!ReadOnlyAutomaticVariables.Contains(name)) return false;
        if (name.Equals("IsCoreCLR", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("IsLinux", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("IsMacOS", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("IsWindows", StringComparison.OrdinalIgnoreCase))
            return !string.Equals(targetFramework, "net472", StringComparison.OrdinalIgnoreCase);
        return true;
    }

    /// <summary>Returns whether the variable is the direct target of an assignment.</summary>
    internal static bool IsDirectAssignmentTarget(VariableExpressionAst variable)
    {
        Ast target = variable;
        if (variable.Parent is ConvertExpressionAst conversion && ReferenceEquals(conversion.Child, variable))
            target = conversion;
        return target.Parent is AssignmentStatementAst assignment && ReferenceEquals(assignment.Left, target);
    }
}
