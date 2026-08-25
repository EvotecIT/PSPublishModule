using System.Management.Automation.Language;

namespace PowerForge;

internal enum PowerShellRuntimeStateIntrinsicKind
{
    None,
    PSEdition,
    IsCoreClr,
    IsWindows,
    IsLinux,
    IsMacOS,
    PSVersion,
    WhatIfPreference,
    ShouldProcessTarget,
    ShouldProcessAction
}

internal static class PowerShellRuntimeStateIntrinsicPolicy
{
    internal static bool TryClassify(
        Ast ast,
        ScriptBlockAst body,
        string? targetFramework,
        PowerShellCompilationCapability capabilities,
        out PowerShellRuntimeStateIntrinsicKind kind)
    {
        kind = PowerShellRuntimeStateIntrinsicKind.None;
        if (!capabilities.HasFlag(PowerShellCompilationCapability.RuntimeStateIntrinsics))
            return false;

        if (ast is VariableExpressionAst variable)
        {
            var name = variable.VariablePath.UserPath;
            if (name.Equals("PSEdition", StringComparison.OrdinalIgnoreCase) && IsKnownTarget(targetFramework))
                kind = PowerShellRuntimeStateIntrinsicKind.PSEdition;
            else if (IsCoreTarget(targetFramework) && name.Equals("IsCoreCLR", StringComparison.OrdinalIgnoreCase))
                kind = PowerShellRuntimeStateIntrinsicKind.IsCoreClr;
            else if (IsCoreTarget(targetFramework) && name.Equals("IsWindows", StringComparison.OrdinalIgnoreCase))
                kind = PowerShellRuntimeStateIntrinsicKind.IsWindows;
            else if (IsCoreTarget(targetFramework) && name.Equals("IsLinux", StringComparison.OrdinalIgnoreCase))
                kind = PowerShellRuntimeStateIntrinsicKind.IsLinux;
            else if (IsCoreTarget(targetFramework) && name.Equals("IsMacOS", StringComparison.OrdinalIgnoreCase))
                kind = PowerShellRuntimeStateIntrinsicKind.IsMacOS;
            else if (name.Equals("WhatIfPreference", StringComparison.OrdinalIgnoreCase) && SupportsShouldProcess(body, capabilities))
                kind = PowerShellRuntimeStateIntrinsicKind.WhatIfPreference;
            return kind != PowerShellRuntimeStateIntrinsicKind.None;
        }

        if (TryGetVersionTableMember(ast, out var member))
        {
            if (member.Equals("PSEdition", StringComparison.OrdinalIgnoreCase) && IsKnownTarget(targetFramework))
                kind = PowerShellRuntimeStateIntrinsicKind.PSEdition;
            else if (member.Equals("PSVersion", StringComparison.OrdinalIgnoreCase) &&
                     capabilities.HasFlag(PowerShellCompilationCapability.PowerShellHostTypes))
                kind = PowerShellRuntimeStateIntrinsicKind.PSVersion;
            return kind != PowerShellRuntimeStateIntrinsicKind.None;
        }

        if (ast is InvokeMemberExpressionAst invocation &&
            invocation.Expression is VariableExpressionAst receiver &&
            receiver.VariablePath.UserPath.Equals("PSCmdlet", StringComparison.OrdinalIgnoreCase) &&
            invocation.Member is StringConstantExpressionAst { Value: var method } &&
            method.Equals("ShouldProcess", StringComparison.OrdinalIgnoreCase) &&
            SupportsShouldProcess(body, capabilities) &&
            invocation.Arguments.Count is 1 or 2)
        {
            kind = invocation.Arguments.Count == 1
                ? PowerShellRuntimeStateIntrinsicKind.ShouldProcessTarget
                : PowerShellRuntimeStateIntrinsicKind.ShouldProcessAction;
            return true;
        }

        return false;
    }

    internal static bool IsSupportedReference(
        VariableExpressionAst variable,
        ScriptBlockAst body,
        string? targetFramework,
        PowerShellCompilationCapability capabilities)
    {
        if (TryClassify(variable, body, targetFramework, capabilities, out _)) return true;
        return (variable.Parent is MemberExpressionAst or IndexExpressionAst or InvokeMemberExpressionAst) &&
               TryClassify(variable.Parent, body, targetFramework, capabilities, out _);
    }

    internal static bool RequiresHostBinding(
        IEnumerable<StatementAst> statements,
        ScriptBlockAst body,
        string? targetFramework,
        PowerShellCompilationCapability capabilities)
        => statements.SelectMany(static statement => statement.FindAll(
                static node => node is VariableExpressionAst or MemberExpressionAst or IndexExpressionAst or InvokeMemberExpressionAst,
                searchNestedScriptBlocks: false))
            .Any(node => TryClassify(node, body, targetFramework, capabilities, out var kind) &&
                         kind is PowerShellRuntimeStateIntrinsicKind.PSVersion or
                             PowerShellRuntimeStateIntrinsicKind.WhatIfPreference or
                             PowerShellRuntimeStateIntrinsicKind.ShouldProcessTarget or
                             PowerShellRuntimeStateIntrinsicKind.ShouldProcessAction);

    internal static bool RequiresShouldProcessHostBinding(
        ScriptBlockAst body,
        string? targetFramework,
        PowerShellCompilationCapability capabilities)
        => body.FindAll(
                static node => node is InvokeMemberExpressionAst,
                searchNestedScriptBlocks: false)
            .Any(node => TryClassify(node, body, targetFramework, capabilities, out var kind) &&
                         kind is PowerShellRuntimeStateIntrinsicKind.ShouldProcessTarget or
                             PowerShellRuntimeStateIntrinsicKind.ShouldProcessAction);

    internal static Type GetType(PowerShellRuntimeStateIntrinsicKind kind)
        => kind switch
        {
            PowerShellRuntimeStateIntrinsicKind.PSEdition => typeof(string),
            PowerShellRuntimeStateIntrinsicKind.PSVersion => typeof(object),
            PowerShellRuntimeStateIntrinsicKind.IsCoreClr or
            PowerShellRuntimeStateIntrinsicKind.IsWindows or
            PowerShellRuntimeStateIntrinsicKind.IsLinux or
            PowerShellRuntimeStateIntrinsicKind.IsMacOS or
            PowerShellRuntimeStateIntrinsicKind.WhatIfPreference or
            PowerShellRuntimeStateIntrinsicKind.ShouldProcessTarget or
            PowerShellRuntimeStateIntrinsicKind.ShouldProcessAction => typeof(bool),
            _ => typeof(object)
        };

    internal static string EmitStatic(PowerShellRuntimeStateIntrinsicKind kind, string targetFramework)
        => kind switch
        {
            PowerShellRuntimeStateIntrinsicKind.PSEdition => targetFramework.Equals("net472", StringComparison.OrdinalIgnoreCase) ? "\"Desktop\"" : "\"Core\"",
            PowerShellRuntimeStateIntrinsicKind.IsCoreClr => "true",
            PowerShellRuntimeStateIntrinsicKind.IsWindows => "global::System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(global::System.Runtime.InteropServices.OSPlatform.Windows)",
            PowerShellRuntimeStateIntrinsicKind.IsLinux => "global::System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(global::System.Runtime.InteropServices.OSPlatform.Linux)",
            PowerShellRuntimeStateIntrinsicKind.IsMacOS => "global::System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(global::System.Runtime.InteropServices.OSPlatform.OSX)",
            PowerShellRuntimeStateIntrinsicKind.PSVersion => "__psVersion",
            PowerShellRuntimeStateIntrinsicKind.WhatIfPreference => "__whatIfPreference",
            _ => throw new InvalidOperationException($"Runtime-state intrinsic '{kind}' requires expression-specific emission.")
        };

    private static bool TryGetVersionTableMember(Ast ast, out string member)
    {
        member = string.Empty;
        if (ast is MemberExpressionAst
            {
                Expression: VariableExpressionAst variable,
                Member: StringConstantExpressionAst { Value: var property }
            } && variable.VariablePath.UserPath.Equals("PSVersionTable", StringComparison.OrdinalIgnoreCase))
        {
            member = property;
            return true;
        }
        if (ast is IndexExpressionAst
        {
                Target: VariableExpressionAst indexedVariable,
                Index: StringConstantExpressionAst { Value: var key }
            } && indexedVariable.VariablePath.UserPath.Equals("PSVersionTable", StringComparison.OrdinalIgnoreCase))
        {
            member = key;
            return true;
        }
        return false;
    }

    private static bool SupportsShouldProcess(ScriptBlockAst body, PowerShellCompilationCapability capabilities)
        => capabilities.HasFlag(PowerShellCompilationCapability.PowerShellStreams) &&
           PowerShellAdvancedFunctionPolicy.SupportsShouldProcess(body.ParamBlock);

    private static bool IsKnownTarget(string? targetFramework)
        => targetFramework?.Equals("net472", StringComparison.OrdinalIgnoreCase) == true || IsCoreTarget(targetFramework);

    private static bool IsCoreTarget(string? targetFramework)
        => targetFramework?.Equals("net8.0", StringComparison.OrdinalIgnoreCase) == true ||
           targetFramework?.Equals("net10.0", StringComparison.OrdinalIgnoreCase) == true;
}
