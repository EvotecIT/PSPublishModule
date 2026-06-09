namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private ModulePipelineManifestBaseline? TryReadProjectManifestBaseline(string projectRoot, string moduleName)
        => ModulePipelineManifestBaselineReader.TryRead(projectRoot, moduleName, _logger);
}
