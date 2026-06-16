using System;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private void EmbedModuleDependencies(ModulePipelinePlan plan, ModuleBuildResult buildResult)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (buildResult is null) throw new ArgumentNullException(nameof(buildResult));

        if (plan.BuildSpec.RefreshManifestOnly)
            return;

        var service = new EmbeddedModuleDependencyService(_logger);
        service.Embed(
            moduleRoot: buildResult.StagingPath,
            modules: plan.EmbeddedModules ?? Array.Empty<RequiredModuleReference>(),
            metadataProvider: _moduleDependencyMetadataProvider,
            delivery: plan.Delivery);
    }
}
