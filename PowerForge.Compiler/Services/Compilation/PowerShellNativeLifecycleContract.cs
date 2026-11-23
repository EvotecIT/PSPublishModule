namespace PowerForge;

/// <summary>Describes compiled clauses without classifying their bodies as retained PowerShell source.</summary>
internal static class PowerShellNativeLifecycleContract
{
    internal static PowerShellCompilationLifecycleContract Create(PowerShellNativeFunctionBinding native,
        PowerShellCompilationParameter[] parameters, PowerShellCompilationCommandBinding binding)
        => new()
        {
            Execution = PowerShellCompilationLifecycleExecution.CompiledNativeCallbacks,
            HasBegin = native.HasBegin,
            HasProcess = native.HasProcess,
            HasEnd = native.HasEnd,
            HasClean = native.HasClean,
            MinimumPowerShellVersion = native.HasClean ? "7.3" : "5.1",
            PreservesOriginalPipelineRecord = true,
            CleanupGuaranteed = true,
            ValueFromPipeline = parameters.Any(static parameter => parameter.Bindings.Any(static item => item.ValueFromPipeline)),
            ValueFromPipelineByPropertyName = parameters.Any(static parameter => parameter.Bindings.Any(static item => item.ValueFromPipelineByPropertyName)),
            ValueFromRemainingArguments = parameters.Any(static parameter => parameter.Bindings.Any(static item => item.ValueFromRemainingArguments)),
            CommonParameters = binding.IsAdvancedFunction,
            SupportsShouldProcess = binding.SupportsShouldProcess,
            ConfirmImpact = binding.ConfirmImpact,
            PipelineParameterNames = parameters.Where(static parameter => parameter.AcceptsPipelineInput).Select(static parameter => parameter.Name).ToArray(),
            HostingReason = "Lifecycle bodies compile to CLR callbacks; the native PowerShell invocation owns binding, variables, streams, and clause scheduling."
        };
}
