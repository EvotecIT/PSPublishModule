namespace PowerForge;

public sealed partial class PowerShellCompilationArtifactBuilder
{
    private static PowerShellCompilationArtifactFile[] CopyPlannedModulePayload(
        PowerShellCompilationBuildSpec spec,
        string artifactName,
        string artifactStagingDirectory,
        IEnumerable<PowerShellCompilationDependency> dependencies,
        IEnumerable<PowerShellCompilationArtifactFile> existingFiles)
    {
        if (spec.Kind != PowerShellCompilationArtifactKind.BinaryModule)
            return Array.Empty<PowerShellCompilationArtifactFile>();

        var moduleDirectory = Path.Combine(artifactStagingDirectory, artifactName);
        var existing = existingFiles
            .Select(static file => Path.GetFullPath(file.Path))
            .ToHashSet(PowerShellCompilationPathSafety.PathComparer);
        var copied = new List<PowerShellCompilationArtifactFile>();
        foreach (var dependency in dependencies.Where(static dependency =>
                     dependency.Exists &&
                     dependency.SourcePath is not null &&
                     dependency.Disposition == PowerShellCompilationDependencyDisposition.CopiedAdjacent &&
                     dependency.Discovery is PowerShellCompilationDependencyDiscovery.ConventionalResourceDirectory or
                         PowerShellCompilationDependencyDiscovery.ConventionalLibraryDirectory or
                         PowerShellCompilationDependencyDiscovery.ConventionalRuntimeDirectory))
        {
            var relativePath = PowerShellCompiledModuleManifest.NormalizeManifestRelativePath(dependency.RelativePath);
            var targetPath = Path.GetFullPath(Path.Combine(moduleDirectory, relativePath));
            PowerShellCompilationPathSafety.EnsureContained(
                moduleDirectory,
                targetPath,
                $"Planned module payload '{dependency.RelativePath}' escapes the generated module root.");
            if (existing.Contains(targetPath))
                continue;
            if (File.Exists(targetPath))
                throw new InvalidOperationException($"Planned module payload '{dependency.RelativePath}' collides with a generated artifact.");
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? moduleDirectory);
            File.Copy(dependency.SourcePath!, targetPath, overwrite: false);
            existing.Add(targetPath);
            copied.Add(CreateArtifactFile(targetPath, GetPayloadRole(dependency.Kind)));
        }
        return copied.ToArray();
    }

    private static string GetPayloadRole(PowerShellCompilationDependencyKind kind)
        => kind switch
        {
            PowerShellCompilationDependencyKind.ManagedAssembly => "ManagedDependency",
            PowerShellCompilationDependencyKind.NativeLibrary => "NativeDependency",
            PowerShellCompilationDependencyKind.JavaScript => "ModuleJavaScript",
            PowerShellCompilationDependencyKind.StyleSheet => "ModuleStyleSheet",
            PowerShellCompilationDependencyKind.TypeData => "ModuleTypeData",
            PowerShellCompilationDependencyKind.FormatData => "ModuleFormatData",
            _ => "ModuleResource"
        };
}
