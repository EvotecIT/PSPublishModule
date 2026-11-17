namespace PowerForge;

/// <summary>
/// Hosted operations that can pass child-process output to the running pipeline step while it runs.
/// The pipeline uses it when available and falls back to <see cref="IModulePipelineHostedOperations"/> otherwise.
/// </summary>
internal interface IModulePipelineStreamingHostedOperations
{
    /// <summary>Runs the test suite and passes each Pester output line to <paramref name="outputLineReceived"/>.</summary>
    ModuleTestSuiteResult RunModuleTestSuite(ModuleTestSuiteSpec spec, Action<string>? outputLineReceived);
}

internal sealed partial class PowerShellModulePipelineHostedOperations : IModulePipelineStreamingHostedOperations
{
    public ModuleTestSuiteResult RunModuleTestSuite(ModuleTestSuiteSpec spec, Action<string>? outputLineReceived)
        => new ModuleTestSuiteService(_runner, _logger).Run(spec, outputLineReceived);
}
