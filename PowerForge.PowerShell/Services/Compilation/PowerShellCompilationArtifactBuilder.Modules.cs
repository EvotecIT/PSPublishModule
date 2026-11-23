using System.Text;

namespace PowerForge;

public sealed partial class PowerShellCompilationArtifactBuilder
{
    private static CopiedArtifact CopyHybridModule(
        PowerShellCompilationBuildSpec spec,
        string artifactName,
        string moduleAssemblyFileName,
        string compiledAssembly,
        PowerShellTypedCompilationResult typed,
        string outputDirectory,
        Action<string, string> verifyCopiedInput,
        PowerShellSourceSnapshot snapshot)
    {
        var moduleDirectory = Path.Combine(outputDirectory, artifactName);
        Directory.CreateDirectory(moduleDirectory);
        var assemblyPath = Path.Combine(moduleDirectory, moduleAssemblyFileName);
        var modulePath = Path.Combine(moduleDirectory, artifactName + ".psm1");
        File.Copy(compiledAssembly, assemblyPath, overwrite: true);
        var files = new List<PowerShellCompilationArtifactFile>();
        var sourceRoot = Path.GetDirectoryName(Path.GetFullPath(spec.SourcePath)) ?? Directory.GetCurrentDirectory();
        var conventionalDiscovery = PowerShellConventionalModuleSourceDiscovery.Analyze(spec.SourcePath,
            rejectChangingFileLists: spec.ResourceMode != PowerShellCompilationResourceMode.CompleteModule, snapshot: snapshot);
        var runtimeHooks = PowerShellCompiledModuleManifest.GetContainedRuntimeScriptFiles(spec.SourcePath, spec.ModuleManifestPath)
            .Select(reference => Path.GetFullPath(Path.Combine(
                sourceRoot,
                PowerShellCompiledModuleManifest.NormalizeManifestRelativePath(reference))))
            .ToArray();
        var retainedSourcePaths = PowerShellHybridDependencyResolver.DiscoverModuleScopeDependencies(
            spec.SourcePath, runtimeHooks.Concat(typed.SourcePaths), conventionalDiscovery.Loaders,
            PowerShellModuleRuntimeAlternatives.Stage, snapshot);
        var runtimeSourcePaths = (spec.RuntimeSourcePaths ?? Array.Empty<string>())
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => Path.GetFullPath(path.Trim().Trim('"')))
            .ToArray();
        File.WriteAllText(
            modulePath,
            PowerShellHybridModuleComposer.ComposeRoot(
                spec.SourcePath,
                Path.GetFileName(assemblyPath),
                typed,
                manifestControlsExports: !string.IsNullOrWhiteSpace(spec.ModuleManifestPath) || HasSiblingModuleManifest(spec.SourcePath),
                retainedSourcePaths: retainedSourcePaths, snapshot: snapshot),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        files.Add(CreateArtifactFile(modulePath, "PrimaryModule"));
        files.Add(CreateArtifactFile(assemblyPath, "TypedAssembly"));
        CopyDebugSymbolsIfPresent(compiledAssembly, assemblyPath, files);
        var manifestFiles = PowerShellCompiledModuleManifest.Create(
            spec.SourcePath,
            spec.ModuleManifestPath,
            moduleDirectory,
            artifactName,
            Path.GetFileName(modulePath),
            moduleAssemblyFileName,
            typed,
            spec.TargetFramework,
            verifyCopiedInput,
            snapshot,
            retainedSourcePaths.Concat(runtimeSourcePaths));
        if (manifestFiles is not null)
        {
            var primaryManifest = manifestFiles[0].Path;
            files.AddRange(manifestFiles.Select(file => CreateManifestArtifactFile(file, primaryManifest)));
        }
        foreach (var sourceDirectory in conventionalDiscovery.SourceDirectories)
        {
            if (PowerShellCompilationPathSafety.PathEquals(sourceRoot, sourceDirectory))
                continue;
            var relativeDirectory = FrameworkCompatibility.GetRelativePath(sourceRoot, sourceDirectory);
            var targetDirectory = Path.GetFullPath(Path.Combine(moduleDirectory, relativeDirectory));
            PowerShellCompilationPathSafety.EnsureContained(
                moduleDirectory,
                targetDirectory,
                $"Conventional module source directory '{sourceDirectory}' escapes the generated module root.");
            Directory.CreateDirectory(targetDirectory);
        }
        var wrappedCompiledMethods = PowerShellHybridModuleComposer.GetWrappedCompiledMethodKeys(spec.SourcePath, typed, snapshot.Get(spec.SourcePath));
        foreach (var dependency in PowerShellHybridDependencyResolver.CopyDependencies(
                     spec.SourcePath,
                     moduleDirectory,
                     runtimeHooks,
                     path => PowerShellHybridModuleComposer.ComposeDependency(path, typed, wrappedCompiledMethods, document: snapshot.Get(path)),
                     typed.SourcePaths.Where(path => !PowerShellCompilationPathSafety.PathEquals(path, spec.SourcePath)),
                     conventionalLoaders: conventionalDiscovery.Loaders, verifyCopiedInput: verifyCopiedInput, snapshot: snapshot,
                     deliveryProfile: PowerShellCompilationTargetContractService.GetDefaultSemanticProfileId(spec.TargetFramework),
                     runtimeSourcePaths: runtimeSourcePaths))
            files.Add(CreateArtifactFile(
                dependency.Path,
                dependency.IsGenerated ? "GeneratedModuleDependency" : "ModuleDependency"));
        var primaryPath = manifestFiles?[0].Path ?? modulePath;
        return new CopiedArtifact(primaryPath, files.ToArray());
    }

    private static CopiedArtifact CopyStrictModuleWithManifest(
        PowerShellCompilationBuildSpec spec,
        string artifactName,
        string moduleAssemblyFileName,
        string compiledAssembly,
        PowerShellTypedCompilationResult typed,
        string outputDirectory,
        Action<string, string> verifyCopiedInput,
        PowerShellSourceSnapshot snapshot)
    {
        var moduleDirectory = Path.Combine(outputDirectory, artifactName);
        Directory.CreateDirectory(moduleDirectory);
        var assemblyPath = Path.Combine(moduleDirectory, moduleAssemblyFileName);
        File.Copy(compiledAssembly, assemblyPath, overwrite: true);
        var files = new List<PowerShellCompilationArtifactFile> { CreateArtifactFile(assemblyPath, "TypedAssembly") };
        CopyDebugSymbolsIfPresent(compiledAssembly, assemblyPath, files);
        var manifestFiles = PowerShellCompiledModuleManifest.Create(
            spec.SourcePath,
            spec.ModuleManifestPath,
            moduleDirectory,
            artifactName,
            Path.GetFileName(assemblyPath),
            moduleAssemblyFileName,
            typed,
            spec.TargetFramework,
            verifyCopiedInput,
            snapshot) ?? throw new InvalidOperationException("The sibling module manifest was not available during artifact publication.");
        var primaryManifest = manifestFiles[0].Path;
        files.AddRange(manifestFiles.Select(file => CreateManifestArtifactFile(file, primaryManifest)));
        return new CopiedArtifact(primaryManifest, files.ToArray());
    }

    private static PowerShellCompilationArtifactFile CreateManifestArtifactFile(PowerShellStagedModuleFile file, string primaryManifest)
    {
        var role = PowerShellCompilationPathSafety.PathEquals(file.Path, primaryManifest) ? "PrimaryModuleManifest"
            : file.IsGenerated ? "GeneratedModuleDependency" : "ModuleDependency";
        return CreateArtifactFile(file.Path, role);
    }
}
