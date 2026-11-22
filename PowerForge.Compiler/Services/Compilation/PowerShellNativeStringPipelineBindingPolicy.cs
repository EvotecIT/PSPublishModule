namespace PowerForge;

/// <summary>Identifies pipeline conversions whose ordering must remain owned by the native script binder.</summary>
internal static class PowerShellNativeStringPipelineBindingPolicy
{
    internal const string DiagnosticCode = "PSB2241";

    internal static bool RequiresScriptBinding(PowerShellCompilationParameter parameter)
        => parameter.TypeName == typeof(string).FullName && parameter.AcceptsPipelineInput;

    internal static bool RequiresScriptBinding(IEnumerable<PowerShellCompilationParameter> parameters)
        => parameters.Any(RequiresScriptBinding);
}
