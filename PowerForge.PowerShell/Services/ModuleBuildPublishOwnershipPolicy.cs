namespace PowerForge;

/// <summary>
/// Applies module destination selection consistently before any input reaches the Publish gate.
/// </summary>
internal static class ModuleBuildPublishOwnershipPolicy
{
    /// <summary>
    /// Excludes module repository and GitHub publishers without changing project/package publication.
    /// </summary>
    internal static void Apply(ModulePipelineSpec spec, bool includeModulePublishing)
    {
        if (!includeModulePublishing)
        {
            spec.Segments = (spec.Segments ?? Array.Empty<IConfigurationSegment>())
                .Where(static segment => segment is not ConfigurationPublishSegment)
                .ToArray();
        }
    }
}
