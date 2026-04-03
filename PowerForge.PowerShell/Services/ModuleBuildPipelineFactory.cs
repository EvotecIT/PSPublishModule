namespace PowerForge;

/// <summary>
/// Creates PowerShell-backed <see cref="ModuleBuildPipeline"/> instances using the default AST manifest mutator and
/// script export detector implementations.
/// </summary>
public static class ModuleBuildPipelineFactory
{
    /// <summary>
    /// Creates a pipeline prewired with the PowerShell-specific collaborators required for manifest mutation and
    /// script export detection.
    /// </summary>
    /// <param name="logger">Logger used by the pipeline.</param>
    /// <returns>A ready-to-use module build pipeline.</returns>
    public static ModuleBuildPipeline Create(ILogger logger)
        => new(
            logger,
            new AstModuleManifestMutator(),
            new PowerShellScriptFunctionExportDetector());
}
