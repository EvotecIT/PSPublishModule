namespace PowerForge;

public sealed partial class PowerShellCompilationArtifactBuilder
{
    private static CopiedArtifact CopyArtifact(
        PowerShellCompilationBuildSpec spec,
        string artifactName,
        string generatedAssemblyName,
        string publishDirectory,
        PowerShellTypedCompilationResult? typed,
        bool usesPowerShellRuntimeFallback,
        string outputDirectory)
    {
        if (spec.Kind is PowerShellCompilationArtifactKind.Library or PowerShellCompilationArtifactKind.BinaryModule)
        {
            var source = Path.Combine(publishDirectory, generatedAssemblyName + ".dll");
            if (!File.Exists(source)) throw new FileNotFoundException("Generated library was not found.", source);
            if (spec.Kind == PowerShellCompilationArtifactKind.BinaryModule && usesPowerShellRuntimeFallback)
                return CopyHybridModule(spec, artifactName, source, typed ?? throw new InvalidOperationException("Typed module metadata was not available."), outputDirectory);
            if (spec.Kind == PowerShellCompilationArtifactKind.BinaryModule &&
                (!string.IsNullOrWhiteSpace(spec.ModuleManifestPath) || HasSiblingModuleManifest(spec.SourcePath)))
                return CopyStrictModuleWithManifest(spec, artifactName, source, typed ?? throw new InvalidOperationException("Typed module metadata was not available."), outputDirectory);
            var target = Path.Combine(outputDirectory, artifactName + ".dll");
            File.Copy(source, target, overwrite: true);
            return CreateCopiedArtifactWithSymbols(source, target, "Primary");
        }

        var executableFileName = GetExecutableFileName(artifactName, spec.RuntimeIdentifier);
        var executable = Path.Combine(publishDirectory, executableFileName);
        if (!File.Exists(executable)) throw new FileNotFoundException("Generated executable was not found.", executable);
        if (spec.SingleFile)
        {
            var target = Path.Combine(outputDirectory, executableFileName);
            File.Copy(executable, target, overwrite: true);
            return CreateCopiedArtifactWithSymbols(executable, target, "Primary");
        }

        var targetDirectory = Path.Combine(outputDirectory, artifactName);
        Directory.CreateDirectory(targetDirectory);
        foreach (var directory in Directory.EnumerateDirectories(publishDirectory, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(targetDirectory, FrameworkCompatibility.GetRelativePath(publishDirectory, directory)));
        foreach (var file in Directory.EnumerateFiles(publishDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = FrameworkCompatibility.GetRelativePath(publishDirectory, file);
            var target = Path.Combine(targetDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target) ?? targetDirectory);
            File.Copy(file, target, overwrite: true);
        }
        var primaryPath = Path.Combine(targetDirectory, executableFileName);
        var generatedAssemblyPath = Path.Combine(targetDirectory, artifactName + ".dll");
        var files = Directory.EnumerateFiles(targetDirectory, "*", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => CreateArtifactFile(
                path,
                PowerShellCompilationPathSafety.PathEquals(path, primaryPath)
                    ? "Primary"
                    : PowerShellCompilationPathSafety.PathEquals(path, generatedAssemblyPath)
                        ? "GeneratedAssembly"
                        : "RuntimeDependency"))
            .ToArray();
        return new CopiedArtifact(primaryPath, files);
    }

    private static string ResolveGeneratedAssemblyName(
        PowerShellCompilationBuildSpec spec,
        string artifactName,
        PowerShellCompilationDependencyGraph dependencyGraph)
    {
        if (spec.Kind != PowerShellCompilationArtifactKind.BinaryModule || spec.Mode != PowerShellCompilationMode.Hybrid)
            return artifactName;
        return dependencyGraph.Nodes.Any(node =>
                (node.Kind is PowerShellCompilationDependencyNodeKind.ManagedLibrary or
                    PowerShellCompilationDependencyNodeKind.BinaryModule or
                    PowerShellCompilationDependencyNodeKind.MixedModule) &&
                node.Identity.Name.Equals(artifactName, StringComparison.OrdinalIgnoreCase))
            ? artifactName + ".PowerForge.Compiled"
            : artifactName;
    }

    private static bool HasSiblingModuleManifest(string sourcePath)
        => Path.GetExtension(sourcePath).Equals(".psm1", StringComparison.OrdinalIgnoreCase) &&
           File.Exists(Path.ChangeExtension(sourcePath, ".psd1"));

    private static CopiedArtifact CreateCopiedArtifactWithSymbols(string sourcePath, string targetPath, string role)
    {
        var files = new List<PowerShellCompilationArtifactFile> { CreateArtifactFile(targetPath, role) };
        CopyDebugSymbolsIfPresent(sourcePath, targetPath, files);
        return new CopiedArtifact(targetPath, files.ToArray());
    }

    private static void CopyDebugSymbolsIfPresent(
        string sourceArtifact,
        string targetArtifact,
        ICollection<PowerShellCompilationArtifactFile> files)
    {
        var sourcePdb = Path.ChangeExtension(sourceArtifact, ".pdb");
        if (!File.Exists(sourcePdb)) return;
        var targetPdb = Path.ChangeExtension(targetArtifact, ".pdb");
        File.Copy(sourcePdb, targetPdb, overwrite: true);
        files.Add(CreateArtifactFile(targetPdb, "DebugSymbols"));
    }

    private static PowerShellCompilationArtifactFile CreateArtifactFile(string path, string role)
        => new() { Path = path, Role = role, Sha256 = ComputeSha256(path), SizeBytes = new FileInfo(path).Length };
}
