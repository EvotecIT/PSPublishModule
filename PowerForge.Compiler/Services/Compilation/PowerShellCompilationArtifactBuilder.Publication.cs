namespace PowerForge;

public sealed partial class PowerShellCompilationArtifactBuilder
{
    private static CopiedArtifact CopyArtifact(
        PowerShellCompilationBuildSpec spec,
        string artifactName,
        string generatedAssemblyName,
        string moduleAssemblyFileName,
        string publishDirectory,
        PowerShellTypedCompilationResult? typed,
        bool usesPowerShellRuntimeFallback,
        string outputDirectory,
        IReadOnlyDictionary<string, string> inputHashes,
        PowerShellSourceSnapshot snapshot)
    {
        void VerifyCopiedInput(string sourcePath, string stagedPath)
            => VerifyStagedPayload(sourcePath, Path.GetFileName(sourcePath), ComputeSha256(stagedPath), inputHashes);
        if (spec.Kind is PowerShellCompilationArtifactKind.Library or PowerShellCompilationArtifactKind.BinaryModule)
        {
            var source = Path.Combine(publishDirectory, generatedAssemblyName + ".dll");
            if (!File.Exists(source)) throw new FileNotFoundException("Generated library was not found.", source);
            if (spec.Kind == PowerShellCompilationArtifactKind.BinaryModule &&
                (usesPowerShellRuntimeFallback || typed?.Methods.Any(static method => method.NativeFunctionBinding is not null) == true))
                return CopyHybridModule(spec, artifactName, moduleAssemblyFileName, source, typed ?? throw new InvalidOperationException("Typed module metadata was not available."), outputDirectory, VerifyCopiedInput, snapshot);
            if (spec.Kind == PowerShellCompilationArtifactKind.BinaryModule &&
                (!string.IsNullOrWhiteSpace(spec.ModuleManifestPath) || HasSiblingModuleManifest(spec.SourcePath)))
                return CopyStrictModuleWithManifest(spec, artifactName, moduleAssemblyFileName, source, typed ?? throw new InvalidOperationException("Typed module metadata was not available."), outputDirectory, VerifyCopiedInput, snapshot);
            var target = Path.Combine(outputDirectory, moduleAssemblyFileName);
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
        PowerShellCompilationDependencyGraph dependencyGraph,
        IReadOnlyCollection<PowerShellCompilationDependency> dependencies,
        out string moduleAssemblyFileName)
    {
        moduleAssemblyFileName = artifactName + ".dll";
        if (spec.Kind != PowerShellCompilationArtifactKind.BinaryModule || spec.Mode != PowerShellCompilationMode.Hybrid)
            return artifactName;
        var identities = dependencyGraph.Nodes.Where(static node =>
                node.Kind is PowerShellCompilationDependencyNodeKind.ManagedLibrary or
                    PowerShellCompilationDependencyNodeKind.BinaryModule or
                    PowerShellCompilationDependencyNodeKind.MixedModule)
            .Select(static node => node.Identity.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var authoredPaths = dependencies.Where(IsSelectedAdjacentPayload)
            .Select(static dependency => dependency.RelativePath.Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool HasAuthoredPath(string name) => authoredPaths.Contains(name + ".dll") || authoredPaths.Contains(name + ".pdb");
        var conventionalPathTaken = HasAuthoredPath(artifactName);
        if (!conventionalPathTaken && !identities.Contains(artifactName))
            return artifactName;

        var prefix = artifactName + ".PowerForge.Compiled";
        var generatedName = prefix;
        for (var suffix = 1; identities.Contains(generatedName) || HasAuthoredPath(generatedName); suffix++)
            generatedName = prefix + "." + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (conventionalPathTaken)
            moduleAssemblyFileName = generatedName + ".dll";
        return generatedName;
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
