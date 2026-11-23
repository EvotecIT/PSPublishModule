using System.Management.Automation.Language;

namespace PowerForge;

public sealed partial class PowerShellCompilationAnalyzer
{
    /// <summary>Analyzes a PowerShell file or directory.</summary>
    public PowerShellCompilationPlan Analyze(PowerShellCompilationSpec spec)
    {
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));

        var files = DiscoverFiles(spec);
        var basePath = Directory.Exists(spec.Path) ? spec.Path : Path.GetDirectoryName(spec.Path) ?? Directory.GetCurrentDirectory();
        return AnalyzeFiles(spec.Mode, files, basePath, spec.TargetFramework, spec.Capabilities);
    }

    /// <summary>Analyzes the exact compilation source graph selected by the shared input resolver.</summary>
    /// <param name="input">Resolved script or module input.</param>
    /// <param name="mode">Requested analysis and fallback policy.</param>
    /// <param name="targetFramework">Generated-project target framework used for CLR eligibility.</param>
    /// <param name="resourceMode">Optional payload selection policy.</param>
    /// <param name="includeResource">Contained resource paths or patterns to include.</param>
    /// <param name="excludeResource">Contained optional resource paths or patterns to exclude.</param>
    /// <param name="outputDirectory">Optional durable output root used to reject resource overlap.</param>
    public PowerShellCompilationPlan Analyze(
        PowerShellCompilationResolvedInput input,
        PowerShellCompilationMode mode = PowerShellCompilationMode.Analyze,
        string? targetFramework = PowerShellCompilationTargetFrameworkPolicy.Default,
        PowerShellCompilationResourceMode resourceMode = PowerShellCompilationResourceMode.Declared,
        IEnumerable<string>? includeResource = null,
        IEnumerable<string>? excludeResource = null,
        string? outputDirectory = null)
        => Analyze(input, mode, targetFramework, resourceMode, includeResource, excludeResource, outputDirectory, null, null, null);

    /// <summary>Analyzes the exact compilation graph and dependency lock for an explicit target contract.</summary>
    public PowerShellCompilationPlan Analyze(
        PowerShellCompilationResolvedInput input,
        PowerShellCompilationMode mode,
        string? targetFramework,
        PowerShellCompilationResourceMode resourceMode,
        IEnumerable<string>? includeResource,
        IEnumerable<string>? excludeResource,
        string? outputDirectory,
        PowerShellCompilationTargetContract? targetContract)
        => Analyze(input, mode, targetFramework, resourceMode, includeResource, excludeResource, outputDirectory, targetContract, null, null);

    internal PowerShellCompilationPlan Analyze(
        PowerShellCompilationResolvedInput input,
        PowerShellCompilationMode mode,
        string? targetFramework,
        PowerShellCompilationResourceMode resourceMode,
        IEnumerable<string>? includeResource,
        IEnumerable<string>? excludeResource,
        string? outputDirectory,
        PowerShellCompilationTargetContract? targetContract,
        IEnumerable<string>? generatedOutputDirectories,
        string? nuGetPackageRoot,
        Action? beforeDependencyAnalysis = null,
        CancellationToken cancellationToken = default)
    {
        if (input is null)
            throw new ArgumentNullException(nameof(input));
        if (!Enum.IsDefined(typeof(PowerShellCompilationMode), mode))
            throw new ArgumentOutOfRangeException(nameof(mode));

        var target = targetContract is null ? null : PowerShellCompilationTargetContractService.Normalize(targetContract);
        if (target is not null && target.ArtifactKind != input.Kind)
            throw new ArgumentException("The explicit PowerShell compilation target kind conflicts with the resolved input.", nameof(targetContract));
        var capabilityMode = target?.Mode ?? (mode == PowerShellCompilationMode.Analyze ? input.Mode : mode);
        if (target is not null && mode != PowerShellCompilationMode.Analyze && mode != target.Mode)
            throw new ArgumentException("The explicit PowerShell compilation target mode conflicts with the requested analysis mode.", nameof(targetContract));
        var normalizedTargetFramework = new PowerShellCompilationSpec(
            input.SourcePath,
            target?.Mode ?? mode,
            targetFramework: target?.TargetFramework ?? targetFramework ?? PowerShellCompilationTargetFrameworkPolicy.Default).TargetFramework;
        cancellationToken.ThrowIfCancellationRequested();
        beforeDependencyAnalysis?.Invoke();
        var profile = target?.SemanticProfileId ?? _semanticProfileId;
        input = input.ResolveForProfile(profile);
        var snapshot = input.SourceSnapshot;
        var dependencyPlanner = new PowerShellCompilationDependencyPlanner();
        var dependencies = dependencyPlanner.Analyze(
            input,
            capabilityMode,
            resourceMode,
            includeResource,
            excludeResource,
            outputDirectory,
            generatedOutputDirectories,
            snapshot);
        var dependencyGraph = PowerShellCompilationDependencyGraphBuilder.Build(
            input.SourcePath,
            input.ModuleManifestPath,
            input.ModuleRoot,
            input.Kind,
            capabilityMode,
            input.CompilationSourceFiles,
            dependencies,
            normalizedTargetFramework,
            snapshot: snapshot);
        if (target is not null)
        {
            dependencyGraph = PowerShellCompilationDependencyGraphBuilder.Build(
                input.SourcePath,
                input.ModuleManifestPath,
                input.ModuleRoot,
                input.Kind,
                capabilityMode,
                input.CompilationSourceFiles,
                dependencies,
                normalizedTargetFramework,
                target.RuntimeIdentifier,
                includeRuntimePack: target.ArtifactKind == PowerShellCompilationArtifactKind.Executable &&
                                    target.Deployment != PowerShellCompilationDeploymentModel.FrameworkDependent,
                nuGetPackageRoot,
                snapshot);
        }
        var nativeTypes = PowerShellNativeDependencyTypes.Create(input.ModuleManifestPath, dependencies, dependencyGraph, input.ModuleRoot, snapshot);
        var documents = input.CompilationSourceFiles.Select(snapshot.Get).ToArray();
        var plan = new PowerShellCompilationAnalyzer(_commandRegistry, profile, nativeTypes).AnalyzeDocuments(
            target?.Mode ?? mode, documents, input.ModuleRoot, normalizedTargetFramework,
            PowerShellCompilationBuildSpec.GetCapabilities(input.Kind, capabilityMode), cancellationToken);
        var combined = new PowerShellCompilationPlan(
            plan.Mode,
            plan.Files,
            plan.TargetFramework,
            dependencies,
            dependencyGraph,
            target,
            profile);
        return input.Kind == PowerShellCompilationArtifactKind.Executable &&
               capabilityMode is PowerShellCompilationMode.Package or PowerShellCompilationMode.Hybrid
            ? ApplyPackagedValidation(combined, input, normalizedTargetFramework ?? PowerShellCompilationTargetFrameworkPolicy.Default, capabilityMode, snapshot)
            : combined;
    }

    private static PowerShellCompilationPlan ApplyPackagedValidation(
        PowerShellCompilationPlan plan,
        PowerShellCompilationResolvedInput input,
        string targetFramework,
        PowerShellCompilationMode mode,
        PowerShellSourceSnapshot snapshot)
    {
        try
        {
            var embeddedScripts = input.SourceFiles
                .Select(Path.GetFullPath)
                .Distinct(PowerShellCompilationPathSafety.PathComparer)
                .ToArray();
            var retainedScripts = embeddedScripts.Except(input.CompilationSourceFiles, PowerShellCompilationPathSafety.PathComparer).ToArray();
            var entry = snapshot.Get(input.SourcePath);
            if (mode == PowerShellCompilationMode.Hybrid && retainedScripts.Length == 0)
                PowerShellPackagedScriptRewriter.ValidateRetainedEntryRequirements(input.SourcePath, embeddedScripts, entry);
            else
            {
                PowerShellPackagedParameterBindingPolicy.Generate(input.SourcePath, targetFramework, document: entry);
                _ = PowerShellPackagedScriptRewriter.Rewrite(input.SourcePath, embeddedScriptPaths: embeddedScripts, document: entry,
                    guardDotSources: retainedScripts.Length > 0, retainedScriptPaths: retainedScripts);
            }
            foreach (var dependency in embeddedScripts.Where(path =>
                         !PowerShellCompilationPathSafety.PathEquals(path, input.SourcePath)))
                PowerShellPackagedScriptRewriter.ValidateDependency(
                    dependency, embeddedScripts, allowDerivedPaths: mode == PowerShellCompilationMode.Hybrid || retainedScripts.Length > 0,
                    document: snapshot.Get(dependency), retainedScriptPaths: retainedScripts);
            return plan;
        }
        catch (InvalidOperationException exception)
        {
            var location = PowerShellCompilationSourceFailure.GetDiagnostic(exception);
            if (location is { FilePath: var reportedPath } &&
                !input.SourceFiles.Contains(reportedPath, PowerShellCompilationPathSafety.PathComparer))
                location = null;
            var sourcePath = location?.FilePath ?? Path.GetFullPath(input.SourcePath);
            IEnumerable<PowerShellCompilationFilePlan> sourceFiles = plan.Files;
            if (!plan.Files.Any(file => PowerShellCompilationPathSafety.PathEquals(file.FullPath, sourcePath)))
                sourceFiles = sourceFiles.Append(new PowerShellCompilationFilePlan(sourcePath,
                    FrameworkCompatibility.GetRelativePath(input.ModuleRoot, sourcePath).Replace('\\', '/'),
                    Array.Empty<PowerShellCompilationUnitPlan>(), Array.Empty<PowerShellCompilationDiagnostic>()));
            var files = sourceFiles.Select(file =>
            {
                if (!PowerShellCompilationPathSafety.PathEquals(file.FullPath, sourcePath))
                    return file;
                var diagnostic = new PowerShellCompilationDiagnostic(
                    PowerShellCompilationDiagnosticCode.InputError,
                    exception.Message,
                    sourcePath,
                    location?.Line ?? 1,
                    location?.Column ?? 1,
                    "powershell.package.validation");
                return new PowerShellCompilationFilePlan(
                    file.FullPath,
                    file.RelativePath,
                    file.Units,
                    file.Diagnostics.Concat(new[] { diagnostic }).ToArray());
            }).ToArray();
            return new PowerShellCompilationPlan(plan.Mode, files, plan.TargetFramework, plan.Dependencies, plan.DependencyGraph,
                plan.TargetContract, plan.AnalysisSemanticProfileId);
        }
    }

    internal PowerShellCompilationPlan AnalyzeFiles(
        PowerShellCompilationMode mode,
        IEnumerable<string> sourcePaths,
        string basePath,
        string? targetFramework,
        PowerShellCompilationCapability capabilities,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var files = sourcePaths.Select(Path.GetFullPath).Distinct(PowerShellCompilationPathSafety.PathComparer).ToArray();
        var documents = files.Select(file =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return PowerShellSourceParser.ParseFile(file, basePath, _semanticProfileId);
        }).ToArray();
        return AnalyzeDocuments(mode, documents, basePath, targetFramework, capabilities, cancellationToken);
    }

    internal PowerShellCompilationPlan AnalyzeDocuments(
        PowerShellCompilationMode mode,
        IReadOnlyList<ParsedSourceDocument> documents,
        string basePath,
        string? targetFramework,
        PowerShellCompilationCapability capabilities,
        CancellationToken cancellationToken = default)
    {
        var analysisTargetFramework = mode == PowerShellCompilationMode.Package ? null : targetFramework;
        if (analysisTargetFramework is not null)
        {
            PowerShellGeneratedTargetFrameworkPolicy.EnsureHostCanAnalyze(analysisTargetFramework);
            PowerShellGeneratedReferenceAssemblyResolver.EnsureAvailable(analysisTargetFramework);
        }
        var localFunctionNames = capabilities.HasFlag(PowerShellCompilationCapability.LocalFunctionCalls)
            ? PowerShellLocalFunctionDiscovery.DiscoverNames(documents)
            : null;
        var structural = documents.Select(document =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return AnalyzeFile(document, basePath, analysisTargetFramework, capabilities, localFunctionNames);
        }).ToArray();
        var analyzed = mode == PowerShellCompilationMode.Package
            ? structural
            : ApplySemanticEvidence(structural, documents, basePath, analysisTargetFramework, capabilities, _commandRegistry, _semanticProfileId,
                _nativeDependencyTypes, cancellationToken);
        return new PowerShellCompilationPlan(mode, analyzed, targetFramework, null, null, null, _semanticProfileId);
    }

    private static string[] DiscoverFiles(PowerShellCompilationSpec spec)
    {
        if (File.Exists(spec.Path))
        {
            var extension = Path.GetExtension(spec.Path);
            if (!extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".psm1", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("PowerShell compilation accepts .ps1 and .psm1 files.", nameof(spec));
            return new[] { spec.Path };
        }

        if (!Directory.Exists(spec.Path))
            throw new DirectoryNotFoundException($"PowerShell compilation input was not found: {spec.Path}");

        return EnumerateSourceFiles(spec.Path, spec.Recurse, spec.ExcludeDirectories)
            .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> EnumerateSourceFiles(string root, bool recurse, string[] exclusions)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var file in Directory.GetFiles(current, "*", SearchOption.TopDirectoryOnly))
            {
                var extension = Path.GetExtension(file);
                if (extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".psm1", StringComparison.OrdinalIgnoreCase))
                    yield return file;
            }

            if (!recurse) continue;
            foreach (var directory in Directory.GetDirectories(current, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (IsExcludedDirectory(name, exclusions)) continue;
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                pending.Push(directory);
            }
        }
    }

    private static bool IsExcludedDirectory(string directory, string[] exclusions)
        => exclusions.Any(exclusion =>
            directory.Equals(exclusion, StringComparison.OrdinalIgnoreCase) ||
            ((exclusion.Equals("bin", StringComparison.OrdinalIgnoreCase) || exclusion.Equals("obj", StringComparison.OrdinalIgnoreCase)) &&
             directory.StartsWith(exclusion + "-", StringComparison.OrdinalIgnoreCase)));

}
