namespace PowerForge;

public sealed partial class PowerShellCompilationArtifactBuilder
{
    /// <summary>Describes the actual additional delivery without changing the reviewed dependency graph or adjacent payload.</summary>
    private static PowerShellCompilationDependency[] DescribeNativeSourceResourceDelivery(
        PowerShellCompilationDependency[] dependencies,
        PowerShellCompilationDependency[] nativeMirroredResources)
    {
        if (nativeMirroredResources.Length == 0) return dependencies;
        // These are the original immutable descriptors actually staged by PreparePackagedSources.
        // Keep the graph's copied-adjacent policy; annotate only the delivered manifest copies.
        var mirrored = new HashSet<PowerShellCompilationDependency>(nativeMirroredResources);
        return dependencies.Select(dependency => !mirrored.Contains(dependency) ? dependency :
            new PowerShellCompilationDependency(dependency.Name, dependency.SourcePath, dependency.RelativePath,
                dependency.Kind, dependency.Discovery, dependency.Disposition, dependency.Exists, dependency.SizeBytes,
                dependency.Note + " Native script-root reads use an immutable embedded/extracted source-layout snapshot of this resource. " +
                "The adjacent resource remains delivered; editing it does not update that bundled snapshot.", dependency.Selection))
            .ToArray();
    }
}
