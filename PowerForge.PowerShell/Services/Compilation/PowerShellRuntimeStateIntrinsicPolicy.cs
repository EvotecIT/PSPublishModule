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
    PSVersionMajor,
    WhatIfPreference,
    ActionPreference,
    ConfirmPreference,
    ErrorCollection,
    EnvironmentVariable,
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
        => TryClassify(
            ast,
            body,
            targetFramework,
            PowerShellCompilationSemanticOracleCatalog.Get(PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId),
            capabilities,
            out kind);

    internal static bool TryClassify(
        Ast ast,
        ScriptBlockAst body,
        string? targetFramework,
        PowerShellCompilationSemanticOracleProfile semanticProfile,
        PowerShellCompilationCapability capabilities,
        out PowerShellRuntimeStateIntrinsicKind kind)
    {
        kind = PowerShellRuntimeStateIntrinsicKind.None;
        if (!capabilities.HasFlag(PowerShellCompilationCapability.RuntimeStateIntrinsics))
            return false;

        if (ast is VariableExpressionAst variable)
        {
            var name = variable.VariablePath.UserPath;
            if (name.StartsWith("env:", StringComparison.OrdinalIgnoreCase) && name.Length > 4)
                kind = PowerShellRuntimeStateIntrinsicKind.EnvironmentVariable;
            else if (IsActionPreference(name) && !HasLocalDefinition(body, name) && capabilities.HasFlag(PowerShellCompilationCapability.PowerShellStreams))
                kind = PowerShellRuntimeStateIntrinsicKind.ActionPreference;
            else if (name.Equals("ConfirmPreference", StringComparison.OrdinalIgnoreCase) && !HasLocalDefinition(body, name) && capabilities.HasFlag(PowerShellCompilationCapability.PowerShellStreams))
                kind = PowerShellRuntimeStateIntrinsicKind.ConfirmPreference;
            else if (name.Equals("Error", StringComparison.OrdinalIgnoreCase) && !HasLocalDefinition(body, name) && capabilities.HasFlag(PowerShellCompilationCapability.PowerShellHostTypes))
                kind = PowerShellRuntimeStateIntrinsicKind.ErrorCollection;
            else if (name.Equals("PSEdition", StringComparison.OrdinalIgnoreCase) && IsKnownTarget(targetFramework))
                kind = PowerShellRuntimeStateIntrinsicKind.PSEdition;
            else if (IsCoreTarget(targetFramework) && IsCoreProfile(semanticProfile) && name.Equals("IsCoreCLR", StringComparison.OrdinalIgnoreCase))
                kind = PowerShellRuntimeStateIntrinsicKind.IsCoreClr;
            else if (IsCoreTarget(targetFramework) && IsCoreProfile(semanticProfile) && name.Equals("IsWindows", StringComparison.OrdinalIgnoreCase))
                kind = PowerShellRuntimeStateIntrinsicKind.IsWindows;
            else if (IsCoreTarget(targetFramework) && IsCoreProfile(semanticProfile) && name.Equals("IsLinux", StringComparison.OrdinalIgnoreCase))
                kind = PowerShellRuntimeStateIntrinsicKind.IsLinux;
            else if (IsCoreTarget(targetFramework) && IsCoreProfile(semanticProfile) && name.Equals("IsMacOS", StringComparison.OrdinalIgnoreCase))
                kind = PowerShellRuntimeStateIntrinsicKind.IsMacOS;
            else if (name.Equals("WhatIfPreference", StringComparison.OrdinalIgnoreCase) &&
                     !HasLocalDefinition(body, name) &&
                     SupportsShouldProcess(body, capabilities))
                kind = PowerShellRuntimeStateIntrinsicKind.WhatIfPreference;
            return kind != PowerShellRuntimeStateIntrinsicKind.None;
        }

        if (TryGetVersionTableVersionMember(ast, out var versionMember))
        {
            if (versionMember.Equals("Major", StringComparison.OrdinalIgnoreCase) && IsKnownTarget(targetFramework))
                kind = PowerShellRuntimeStateIntrinsicKind.PSVersionMajor;
            return kind != PowerShellRuntimeStateIntrinsicKind.None;
        }

        if (TryGetVersionTableMember(ast, out var member))
        {
            if (member.Equals("PSEdition", StringComparison.OrdinalIgnoreCase) && IsKnownTarget(targetFramework))
                kind = PowerShellRuntimeStateIntrinsicKind.PSEdition;
            else if (member.Equals("PSVersion", StringComparison.OrdinalIgnoreCase) &&
                     !IsConsumedByStaticVersionMember(ast, targetFramework) &&
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
        if (variable.Parent is MemberExpressionAst or IndexExpressionAst or InvokeMemberExpressionAst &&
            TryClassify(variable.Parent, body, targetFramework, capabilities, out _))
            return true;
        return variable.Parent?.Parent is MemberExpressionAst or IndexExpressionAst or InvokeMemberExpressionAst &&
               TryClassify(variable.Parent.Parent, body, targetFramework, capabilities, out _);
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
                             PowerShellRuntimeStateIntrinsicKind.ActionPreference or
                             PowerShellRuntimeStateIntrinsicKind.ConfirmPreference or
                             PowerShellRuntimeStateIntrinsicKind.ErrorCollection or
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
            PowerShellRuntimeStateIntrinsicKind.PSVersionMajor => typeof(int),
            PowerShellRuntimeStateIntrinsicKind.ActionPreference => typeof(System.Management.Automation.ActionPreference),
            PowerShellRuntimeStateIntrinsicKind.ConfirmPreference => typeof(System.Management.Automation.ConfirmImpact),
            PowerShellRuntimeStateIntrinsicKind.ErrorCollection => typeof(System.Collections.ArrayList),
            PowerShellRuntimeStateIntrinsicKind.EnvironmentVariable => typeof(string),
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
        => EmitStatic(kind, targetFramework, PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId);

    internal static string EmitStatic(PowerShellRuntimeStateIntrinsicKind kind, string targetFramework, string semanticProfileId)
        => kind switch
        {
            PowerShellRuntimeStateIntrinsicKind.PSEdition => PowerShellCompilationSemanticOracleCatalog.Get(semanticProfileId).PowerShellEdition.Equals("Desktop", StringComparison.Ordinal) ? "\"Desktop\"" : "\"Core\"",
            PowerShellRuntimeStateIntrinsicKind.IsCoreClr => "true",
            PowerShellRuntimeStateIntrinsicKind.IsWindows => "global::System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(global::System.Runtime.InteropServices.OSPlatform.Windows)",
            PowerShellRuntimeStateIntrinsicKind.IsLinux => "global::System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(global::System.Runtime.InteropServices.OSPlatform.Linux)",
            PowerShellRuntimeStateIntrinsicKind.IsMacOS => "global::System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(global::System.Runtime.InteropServices.OSPlatform.OSX)",
            PowerShellRuntimeStateIntrinsicKind.PSVersion => "__psVersion",
            PowerShellRuntimeStateIntrinsicKind.PSVersionMajor => EmitPowerShellMajorVersion(semanticProfileId),
            PowerShellRuntimeStateIntrinsicKind.WhatIfPreference => "__whatIfPreference",
            _ => throw new InvalidOperationException($"Runtime-state intrinsic '{kind}' requires expression-specific emission.")
        };

    private static bool TryGetVersionTableMember(Ast ast, out string member)
    {
        member = string.Empty;
        if (IsAssignmentTarget(ast))
            return false;
        if (ast is MemberExpressionAst
            {
                Static: false,
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

    private static string EmitPowerShellMajorVersion(string semanticProfileId)
    {
        var major = PowerShellCompilationSemanticOracleCatalog.Get(semanticProfileId).PowerShellMajorVersion;
        if (major <= 0)
            throw new InvalidOperationException($"Semantic profile '{semanticProfileId}' does not fix one PowerShell major version.");
        return major.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool IsConsumedByStaticVersionMember(Ast ast, string? targetFramework)
        => ast.Parent is { } parent &&
           TryGetVersionTableVersionMember(parent, out var member) &&
           member.Equals("Major", StringComparison.OrdinalIgnoreCase) &&
           IsKnownTarget(targetFramework);

    private static bool TryGetVersionTableVersionMember(Ast ast, out string member)
    {
        member = string.Empty;
        if (IsAssignmentTarget(ast) || ast is not MemberExpressionAst
            {
                Static: false,
                Expression: var versionExpression,
                Member: StringConstantExpressionAst { Value: var property }
            } ||
            !TryGetVersionTableMember(versionExpression, out var versionTableMember) ||
            !versionTableMember.Equals("PSVersion", StringComparison.OrdinalIgnoreCase))
            return false;
        member = property;
        return true;
    }

    private static bool HasLocalDefinition(ScriptBlockAst body, string name)
        => body.ParamBlock?.Parameters.Any(parameter =>
               parameter.Name.VariablePath.UserPath.Equals(name, StringComparison.OrdinalIgnoreCase)) == true ||
           body.FindAll(
                   node => (node is AssignmentStatementAst assignment &&
                               PowerShellAssignmentTargetPolicy.FindDirectVariable(assignment.Left) is { } variable &&
                               variable.VariablePath.UserPath.Equals(name, StringComparison.OrdinalIgnoreCase)) ||
                           (node is ForEachStatementAst loop &&
                               loop.Variable.VariablePath.UserPath.Equals(name, StringComparison.OrdinalIgnoreCase)),
                   searchNestedScriptBlocks: false)
               .Any();

    private static bool IsAssignmentTarget(Ast ast)
        => ast.Parent is AssignmentStatementAst assignment && ReferenceEquals(assignment.Left, ast);

    private static bool SupportsShouldProcess(ScriptBlockAst body, PowerShellCompilationCapability capabilities)
        => capabilities.HasFlag(PowerShellCompilationCapability.PowerShellStreams) &&
           PowerShellAdvancedFunctionPolicy.SupportsShouldProcess(body.ParamBlock);

    private static bool IsKnownTarget(string? targetFramework)
        => targetFramework?.Equals("net472", StringComparison.OrdinalIgnoreCase) == true || IsCoreTarget(targetFramework);

    private static bool IsCoreTarget(string? targetFramework)
        => targetFramework?.Equals("net8.0", StringComparison.OrdinalIgnoreCase) == true ||
           targetFramework?.Equals("net10.0", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsCoreProfile(PowerShellCompilationSemanticOracleProfile semanticProfile)
        => semanticProfile.Family == PowerShellCompilationSemanticHostFamily.PowerShell7;

    private static bool IsActionPreference(string name)
        => name.Equals("VerbosePreference", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("DebugPreference", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("WarningPreference", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("InformationPreference", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("ErrorActionPreference", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("ProgressPreference", StringComparison.OrdinalIgnoreCase);
}
