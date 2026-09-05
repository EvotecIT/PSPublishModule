namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private static string EmitRuntimeState(PowerShellLoweredRuntimeStateExpression expression)
    {
        if (expression.Kind == PowerShellRuntimeStateIntrinsicKind.ShouldProcessTarget)
            return $"__shouldProcessTarget({EmitExpression(expression.Arguments[0])})";
        if (expression.Kind == PowerShellRuntimeStateIntrinsicKind.ShouldProcessAction)
            return $"__shouldProcessAction({EmitExpression(expression.Arguments[0])}, {EmitExpression(expression.Arguments[1])})";
        if (expression.Kind == PowerShellRuntimeStateIntrinsicKind.EnvironmentVariable)
            return $"global::System.Environment.GetEnvironmentVariable({EmitExpression(expression.Arguments[0])})";
        if (expression.Kind == PowerShellRuntimeStateIntrinsicKind.ModuleVariable)
            return $"__readPowerShellModuleVariable({EmitExpression(expression.Arguments[0])})";
        if (expression.Kind == PowerShellRuntimeStateIntrinsicKind.ActionPreference)
            return $"(global::System.Management.Automation.ActionPreference)global::System.Management.Automation.LanguagePrimitives.ConvertTo(__runtimeState[{EmitExpression(expression.Arguments[0])}], typeof(global::System.Management.Automation.ActionPreference), global::System.Globalization.CultureInfo.InvariantCulture)!";
        if (expression.Kind == PowerShellRuntimeStateIntrinsicKind.ConfirmPreference)
            return $"(global::System.Management.Automation.ConfirmImpact)global::System.Management.Automation.LanguagePrimitives.ConvertTo(__runtimeState[{EmitExpression(expression.Arguments[0])}], typeof(global::System.Management.Automation.ConfirmImpact), global::System.Globalization.CultureInfo.InvariantCulture)!";
        if (expression.Kind == PowerShellRuntimeStateIntrinsicKind.ErrorCollection)
            return $"(global::System.Collections.ArrayList)__runtimeState[{EmitExpression(expression.Arguments[0])}]";
        if (expression.Kind == PowerShellRuntimeStateIntrinsicKind.LanguageMode)
            return $"(global::System.Management.Automation.PSLanguageMode)__runtimeState[{EmitExpression(expression.Arguments[0])}]!";
        return PowerShellRuntimeStateIntrinsicPolicy.EmitStatic(expression.Kind, expression.TargetFramework, expression.SemanticProfileId);
    }
}
