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
        string? targetFramework = "net8.0",
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
        string? nuGetPackageRoot)
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
            targetFramework: target?.TargetFramework ?? targetFramework ?? "net8.0").TargetFramework;
        var plan = AnalyzeFiles(
            target?.Mode ?? mode,
            input.CompilationSourceFiles,
            input.ModuleRoot,
            normalizedTargetFramework,
            PowerShellCompilationBuildSpec.GetCapabilities(input.Kind, capabilityMode));
        var dependencyPlanner = new PowerShellCompilationDependencyPlanner();
        var dependencies = dependencyPlanner.Analyze(
            input,
            capabilityMode,
            resourceMode,
            includeResource,
            excludeResource,
            outputDirectory,
            generatedOutputDirectories);
        var dependencyGraph = PowerShellCompilationDependencyGraphBuilder.Build(
            input.SourcePath,
            input.ModuleManifestPath,
            input.ModuleRoot,
            input.Kind,
            capabilityMode,
            input.CompilationSourceFiles,
            dependencies,
            normalizedTargetFramework);
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
                nuGetPackageRoot);
        }
        var combined = new PowerShellCompilationPlan(
            plan.Mode,
            plan.Files,
            plan.TargetFramework,
            dependencies,
            dependencyGraph,
            target);
        return capabilityMode == PowerShellCompilationMode.Package
            ? ApplyPackagedValidation(combined, input, normalizedTargetFramework ?? "net8.0")
            : combined;
    }

    private static PowerShellCompilationPlan ApplyPackagedValidation(
        PowerShellCompilationPlan plan,
        PowerShellCompilationResolvedInput input,
        string targetFramework)
    {
        try
        {
            var embeddedScripts = input.CompilationSourceFiles
                .Select(Path.GetFullPath)
                .Distinct(PowerShellCompilationPathSafety.PathComparer)
                .ToArray();
            PowerShellPackagedParameterBindingPolicy.Generate(input.SourcePath, targetFramework);
            _ = PowerShellPackagedScriptRewriter.Rewrite(
                input.SourcePath,
                embeddedScriptPaths: embeddedScripts);
            foreach (var dependency in embeddedScripts.Where(path =>
                         !PowerShellCompilationPathSafety.PathEquals(path, input.SourcePath)))
                PowerShellPackagedScriptRewriter.ValidateDependency(dependency, embeddedScripts);
            return plan;
        }
        catch (InvalidOperationException exception)
        {
            var sourcePath = Path.GetFullPath(input.SourcePath);
            var files = plan.Files.Select(file =>
            {
                if (!PowerShellCompilationPathSafety.PathEquals(file.FullPath, sourcePath))
                    return file;
                var diagnostic = new PowerShellCompilationDiagnostic(
                    PowerShellCompilationDiagnosticCode.InputError,
                    exception.Message,
                    sourcePath,
                    1,
                    1,
                    "powershell.package.validation");
                return new PowerShellCompilationFilePlan(
                    file.FullPath,
                    file.RelativePath,
                    file.Units,
                    file.Diagnostics.Concat(new[] { diagnostic }).ToArray());
            }).ToArray();
            return new PowerShellCompilationPlan(plan.Mode, files, plan.TargetFramework, plan.Dependencies, plan.DependencyGraph, plan.TargetContract);
        }
    }

    internal PowerShellCompilationPlan AnalyzeFiles(
        PowerShellCompilationMode mode,
        IEnumerable<string> sourcePaths,
        string basePath,
        string? targetFramework,
        PowerShellCompilationCapability capabilities)
    {
        var files = sourcePaths.Select(Path.GetFullPath).Distinct(PowerShellCompilationPathSafety.PathComparer).ToArray();
        var analysisTargetFramework = mode == PowerShellCompilationMode.Package ? null : targetFramework;
        if (analysisTargetFramework is not null)
        {
            PowerShellGeneratedTargetFrameworkPolicy.EnsureHostCanAnalyze(analysisTargetFramework);
            PowerShellGeneratedReferenceAssemblyResolver.EnsureAvailable(analysisTargetFramework);
        }
        var localFunctionNames = capabilities.HasFlag(PowerShellCompilationCapability.LocalFunctionCalls)
            ? PowerShellLocalFunctionDiscovery.DiscoverNames(files)
            : null;
        var structural = files.Select(file => AnalyzeFile(file, basePath, analysisTargetFramework, capabilities, localFunctionNames)).ToArray();
        var analyzed = mode == PowerShellCompilationMode.Package
            ? structural
            : ApplySemanticEvidence(structural, files, basePath, analysisTargetFramework, capabilities, _commandRegistry);
        return new PowerShellCompilationPlan(mode, analyzed, targetFramework);
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
