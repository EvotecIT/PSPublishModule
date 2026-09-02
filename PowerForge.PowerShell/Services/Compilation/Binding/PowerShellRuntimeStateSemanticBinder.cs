using System.Management.Automation.Language;

namespace PowerForge;

internal static class PowerShellRuntimeStateSemanticBinder
{
    internal static bool TryBind(
        ParsedSourceDocument document,
        Ast syntax,
        ScriptBlockAst body,
        string? targetFramework,
        PowerShellCompilationSemanticOracleProfile semanticProfile,
        PowerShellCompilationCapability capabilities,
        Func<Ast, Type?, PowerShellBoundExpression?> bindExpression,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        out PowerShellBoundExpression? bound)
    {
        bound = null;
        if (!PowerShellRuntimeStateIntrinsicPolicy.TryClassify(syntax, body, targetFramework, semanticProfile, capabilities, out var kind))
            return false;

        var span = PowerShellSourceParser.GetSpan(document, syntax.Extent);
        PowerShellBoundExpression?[] arguments;
        if (syntax is InvokeMemberExpressionAst invocation)
            arguments = invocation.Arguments.Select(argument => bindExpression(argument, typeof(string))).ToArray();
        else if (kind == PowerShellRuntimeStateIntrinsicKind.LanguageMode ||
                 syntax is VariableExpressionAst && kind is
                     (PowerShellRuntimeStateIntrinsicKind.EnvironmentVariable or
                      PowerShellRuntimeStateIntrinsicKind.ActionPreference or
                      PowerShellRuntimeStateIntrinsicKind.ConfirmPreference or
                      PowerShellRuntimeStateIntrinsicKind.ErrorCollection))
        {
            var name = kind == PowerShellRuntimeStateIntrinsicKind.LanguageMode
                ? "LanguageMode"
                : ((VariableExpressionAst)syntax).VariablePath.UserPath;
            if (kind == PowerShellRuntimeStateIntrinsicKind.EnvironmentVariable) name = name.Substring(4);
            arguments = new PowerShellBoundExpression?[]
            {
                new PowerShellBoundLiteralExpression(
                    span,
                    name,
                    new PowerShellTypeFact(typeof(string), PowerShellTypeFactProvenance.Literal, "The runtime-state slot name is syntax-owned and immutable."),
                    PowerShellValueState.Known)
            };
        }
        else arguments = Array.Empty<PowerShellBoundExpression?>();
        if (arguments.Any(static argument => argument is null) ||
            arguments.Any(static argument => argument!.Type.ClrType != typeof(string)))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2601",
                "$PSCmdlet.ShouldProcess requires one or two scalar String arguments.",
                span));
            return true;
        }

        bound = new PowerShellBoundRuntimeStateExpression(
            span,
            kind,
            targetFramework ?? string.Empty,
            semanticProfile.ProfileId,
            arguments.Select(static argument => argument!).ToArray());
        return true;
    }
}
