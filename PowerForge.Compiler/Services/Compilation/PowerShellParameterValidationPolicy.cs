namespace PowerForge;

/// <summary>Owns when parameter validation observes bound input rather than an unbound invocation default.</summary>
internal static class PowerShellParameterValidationPolicy
{
    internal static bool RequiresBoundParameterSet(PowerShellCompilationParameter parameter, PowerShellCompilationCapability capabilities)
        => parameter.DefaultValue is not null ||
           capabilities.HasFlag(PowerShellCompilationCapability.BoundParameters) &&
           (!parameter.IsMandatory && parameter.Validations.Length > 0 ||
            parameter.IsMandatory && IsPipelineBinding(parameter, capabilities));

    internal static bool ValidateOnlyWhenBound(PowerShellCompilationParameter parameter, PowerShellCompilationCapability capabilities)
        => (parameter.DefaultValue is not null || capabilities.HasFlag(PowerShellCompilationCapability.BoundParameters)) &&
           (!parameter.IsMandatory || IsPipelineBinding(parameter, capabilities));

    private static bool IsPipelineBinding(PowerShellCompilationParameter parameter, PowerShellCompilationCapability capabilities)
        => parameter.AcceptsPipelineInput && capabilities.HasFlag(PowerShellCompilationCapability.PipelineParameterBinding);
}
