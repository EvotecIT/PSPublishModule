using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>
/// Produces runtime-packaged executables and genuinely typed CLR libraries from PowerShell source.
/// </summary>
public sealed partial class PowerShellCompilationArtifactBuilder
{
    private const string TypedProjectTemplate = "PowerForge.PowerShell.Compilation.TypedLibrary.csproj.template";
    private const string PackagedProjectTemplate = "PowerForge.PowerShell.Compilation.PackagedExecutable.csproj.template";
    private const string PackagedProgramTemplate = "PowerForge.PowerShell.Compilation.PackagedProgram.cs.template";
    private const string TypedExecutableProjectTemplate = "PowerForge.PowerShell.Compilation.TypedExecutable.csproj.template";
    private const string BinaryModuleProjectTemplate = "PowerForge.PowerShell.Compilation.BinaryModule.csproj.template";
    private const int MaximumBuildOutputLength = 64 * 1024;
    private readonly Func<PowerShellStrictDependencyClosureRequest, PowerShellCompilationDependencyClosure> _verifyStrictDependencyClosure;

    /// <summary>Creates an artifact builder that uses the built-in delivered-file verifier.</summary>
    public PowerShellCompilationArtifactBuilder()
        : this(PowerShellStrictDependencyClosureVerifier.Verify)
    {
    }

    internal PowerShellCompilationArtifactBuilder(
        Func<PowerShellStrictDependencyClosureRequest, PowerShellCompilationDependencyClosure> verifyStrictDependencyClosure)
    {
        _verifyStrictDependencyClosure = verifyStrictDependencyClosure ?? throw new ArgumentNullException(nameof(verifyStrictDependencyClosure));
    }

    /// <summary>Builds the requested PowerShell artifact.</summary>
    public PowerShellCompilationBuildResult Build(PowerShellCompilationBuildSpec spec)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        ApplyExplicitTargetContract(spec);
        ValidateSpec(spec);
        var runtimeIdentifier = ResolveRuntimeIdentifier(spec);
        if (!string.IsNullOrWhiteSpace(runtimeIdentifier)) spec.RuntimeIdentifier = runtimeIdentifier;
        PowerShellCompilationOutputPolicy.EnsureDoesNotOverlapRecursiveLoaderRoot(spec.SourcePath, spec.OutputDirectory);

        Directory.CreateDirectory(spec.OutputDirectory);
        PowerShellCompilationPathSafety.EnsureNoLinksFromFileSystemRoot(
            spec.OutputDirectory,
            $"PowerShell compilation output directory '{spec.OutputDirectory}' must not be a symbolic link or junction.");
        var artifactName = SanitizeArtifactName(spec.ArtifactName);
        // Microsoft.PowerShell.SDK carries deeply nested content files. Keeping the disposable
        // generated project below the durable output directory can exceed MAX_PATH on Windows
        // even when the user's final artifact path is otherwise reasonable.
        using var workspaceLease = PowerShellCompilationWorkspace.Create(spec.KeepBuildWorkspace);
        var workspace = workspaceLease.Path;
        var result = new PowerShellCompilationBuildResult { BuildWorkspace = spec.KeepBuildWorkspace ? workspace : null };

        try
        {
            var compilationSourcePaths = ResolveCompilationSourcePaths(spec);
            foreach (var sourcePath in compilationSourcePaths)
            {
                PowerShellCompilationPathSafety.EnsureNoLinksFromFileSystemRoot(
                    sourcePath,
                    $"PowerShell compilation source '{sourcePath}' must not traverse a symbolic link or junction.");
            }
            ValidateRuntimeHookSourceOwnership(spec, compilationSourcePaths);
            ValidateRuntimeSourcePaths(spec, compilationSourcePaths);
            var dependencyPlan = PowerShellCompilationDependencyPlanner.Analyze(spec, compilationSourcePaths);
            var dependencyGraph = PowerShellCompilationDependencyPlanner.AnalyzeGraph(spec, compilationSourcePaths, dependencyPlan);
            ValidateExpectedDependencyLock(spec, dependencyGraph);
            if (dependencyGraph.Conflicts.Length > 0)
                throw new InvalidOperationException("PowerShell compilation dependency graph contains incompatible identities: " + string.Join(" ", dependencyGraph.Conflicts));
            if (dependencyGraph.Cycles.Length > 0)
                throw new InvalidOperationException("PowerShell compilation dependency graph contains a static dependency cycle: " + string.Join(" -> ", dependencyGraph.Cycles[0]));
            var targetContract = ResolveTargetContract(spec, runtimeIdentifier);
            var toolchain = CaptureToolchain(workspace, targetContract, dependencyGraph);
            WriteTargetContract(workspace, targetContract);
            var missingDependencies = dependencyPlan
                .Where(static dependency => dependency.Disposition == PowerShellCompilationDependencyDisposition.Missing)
                .ToArray();
            if (missingDependencies.Length > 0)
            {
                var missingManifestReference = missingDependencies.FirstOrDefault(static dependency =>
                    dependency.Discovery is PowerShellCompilationDependencyDiscovery.RequiredAssemblies or
                        PowerShellCompilationDependencyDiscovery.NestedModules or
                        PowerShellCompilationDependencyDiscovery.ScriptsToProcess or
                        PowerShellCompilationDependencyDiscovery.TypesToProcess or
                        PowerShellCompilationDependencyDiscovery.FormatsToProcess or
                        PowerShellCompilationDependencyDiscovery.FileList);
                if (missingManifestReference is not null)
                    throw new FileNotFoundException($"Required module manifest file reference '{missingManifestReference.RelativePath}' was not found.", missingManifestReference.SourcePath);
                throw new FileNotFoundException($"Required PowerShell compilation dependency was not found: {string.Join(", ", missingDependencies.Select(static dependency => dependency.RelativePath))}.");
            }
            var capabilities = PowerShellCompilationBuildSpec.GetCapabilities(spec.Kind, spec.Mode);
            var plan = AnalyzeCompilationSources(compilationSourcePaths, spec.Mode, spec.TargetFramework, capabilities, spec.CommandProviders);
            if (plan.ParseErrorFiles > 0)
                throw new InvalidOperationException("PowerShell source contains parser errors; no artifact was produced.");

            var publishDirectory = Path.Combine(workspace, "publish");
            Directory.CreateDirectory(publishDirectory);
            PowerShellTypedCompilationResult? typed = null;
            string projectPath;
            bool requiresPowerShellRuntime;
            bool usesPowerShellRuntimeFallback;
            int compiledUnits;
            int compiledMethods;
            int runtimeRoutedUnits;
            IReadOnlyCollection<PowerShellCompiledMethod> compiledMethodDetails = Array.Empty<PowerShellCompiledMethod>();
            var optimizationEvidence = new PowerShellCompilationOptimizationEvidence();
            PowerShellRuntimeFreeArtifactContract? runtimeFreeContract = null;
            PowerShellCompilationDependencyClosure? dependencyClosure = null;
            var runtimeManifestHooks = spec.Kind == PowerShellCompilationArtifactKind.BinaryModule
                ? PowerShellCompiledModuleManifest.GetRuntimeScriptHooks(spec.SourcePath, spec.ModuleManifestPath)
                : Array.Empty<string>();
            if (spec.Mode == PowerShellCompilationMode.Strict && runtimeManifestHooks.Length > 0)
                throw new InvalidOperationException(
                    $"Strict binary-module compilation rejected manifest runtime script hook(s): {string.Join(", ", runtimeManifestHooks)}.");
            if (spec.Kind == PowerShellCompilationArtifactKind.Executable && spec.Mode == PowerShellCompilationMode.Strict)
            {
                var executable = PowerShellTypedExecutableEmitter.Emit(spec.SourcePath, compilationSourcePaths, plan, spec.TargetFramework);
                File.WriteAllText(Path.Combine(workspace, "CompiledPowerShellScript.cs"), executable.CompiledSource, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                File.WriteAllText(Path.Combine(workspace, "Program.cs"), executable.ProgramSource, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                projectPath = Path.Combine(workspace, artifactName + ".csproj");
                var publishSingleFile = ShouldEnablePublishSingleFile(spec);
                File.WriteAllText(
                    projectPath,
                    ReadTemplate(TypedExecutableProjectTemplate)
                        .Replace("{{TARGET_FRAMEWORK}}", EscapeXml(spec.TargetFramework))
                        .Replace("{{ARTIFACT_NAME}}", EscapeXml(artifactName))
                        .Replace("{{SINGLE_FILE}}", publishSingleFile ? "true" : "false")
                        .Replace("{{SELF_CONTAINED}}", spec.SelfContained ? "true" : "false")
                        .Replace("{{PUBLISH_TRIMMED}}", spec.Optimization != PowerShellCompilationExecutableOptimization.None ? "true" : "false")
                        .Replace("{{PUBLISH_AOT}}", spec.Optimization == PowerShellCompilationExecutableOptimization.NativeAot ? "true" : "false"),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                requiresPowerShellRuntime = false;
                usesPowerShellRuntimeFallback = false;
                compiledUnits = plan.TotalUnits;
                compiledMethods = executable.Methods.Length;
                compiledMethodDetails = executable.Methods;
                optimizationEvidence = executable.Optimization;
                runtimeRoutedUnits = plan.TotalUnits;
            }
            else if (spec.Kind == PowerShellCompilationArtifactKind.Executable && spec.Mode == PowerShellCompilationMode.Hybrid)
            {
                var hybrid = PrepareHybridExecutable(
                    workspace,
                    artifactName,
                    spec,
                    compilationSourcePaths,
                    plan,
                    dependencyPlan);
                typed = hybrid.Typed;
                projectPath = hybrid.ProjectPath;
                requiresPowerShellRuntime = true;
                compiledMethodDetails = hybrid.CompiledMethods;
                compiledMethods = hybrid.CompiledMethods.Length;
                compiledUnits = compiledMethods;
                runtimeRoutedUnits = compiledUnits;
                usesPowerShellRuntimeFallback = plan.TotalUnits > compiledUnits;
                optimizationEvidence = typed.Optimization;
            }
            else if (spec.Kind is PowerShellCompilationArtifactKind.Library or PowerShellCompilationArtifactKind.BinaryModule)
            {
                if (spec.Mode == PowerShellCompilationMode.Package)
                    throw new InvalidOperationException("DLL artifacts require Hybrid or Strict mode because they contain genuinely typed methods.");
                var transpiler = new PowerShellTypedCompilationTranspiler(spec.CommandProviders);
                typed = spec.Kind == PowerShellCompilationArtifactKind.BinaryModule
                    ? transpiler.TranspileForBinaryModule(
                        compilationSourcePaths,
                        "PowerForge.Compiled",
                        PowerShellCSharpSymbolRenderer.Identifier(artifactName) + "Methods",
                        spec.TargetFramework)
                    : transpiler.Transpile(
                        compilationSourcePaths,
                        "PowerForge.Compiled",
                        PowerShellCSharpSymbolRenderer.Identifier(artifactName) + "Methods",
                        spec.TargetFramework);
                string[]? exportedFunctions = null;
                if (spec.Kind == PowerShellCompilationArtifactKind.BinaryModule)
                {
                    var exportContract = PowerShellModuleExportContract.TryRead(spec.SourcePath);
                    if (spec.Mode == PowerShellCompilationMode.Strict && exportContract?.HasRuntimeControlledExports == true)
                    {
                        throw new InvalidOperationException(
                            "Strict binary-module compilation rejects runtime-controlled Export-ModuleMember declarations because their export surface requires PowerShell execution; use Hybrid mode or unconditional literal exports.");
                    }
                    if (spec.Mode == PowerShellCompilationMode.Hybrid)
                    {
                        typed = PowerShellHybridFunctionCollisionResolver.RouteNameCollisionsToFallback(typed, spec.TargetFramework);
                        typed = PowerShellAdvancedFunctionLifecyclePlanner.AddHostedLifecycleMethods(typed, spec.TargetFramework);
                    }
                    exportedFunctions = exportContract?.SelectFunctions(typed.Methods.Select(static method => method.SourceName));
                    typed = PowerShellBinaryCmdletSourceGenerator.PrepareForBinaryModule(typed, exportedFunctions, spec.TargetFramework);
                }
                if (typed.Methods.Length == 0 &&
                    !(spec.Kind == PowerShellCompilationArtifactKind.BinaryModule && spec.Mode == PowerShellCompilationMode.Hybrid))
                {
                    if (spec.Kind == PowerShellCompilationArtifactKind.BinaryModule &&
                        spec.Mode == PowerShellCompilationMode.Strict &&
                        PowerShellAdvancedFunctionLifecyclePlanner.HasNamedLifecycle(compilationSourcePaths))
                    {
                        throw new InvalidOperationException(
                            "Strict binary-module compilation rejects hosted advanced-function begin/process/end/clean lifecycle blocks; use Hybrid mode until these blocks have a runtime-free typed lifecycle owner.");
                    }
                    var blockerSummary = DescribeBlockers(typed.Diagnostics);
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(blockerSummary)
                        ? "No PowerShell functions were eligible for typed CLR compilation."
                        : $"No PowerShell functions were eligible for typed CLR compilation. Blockers: {blockerSummary}");
                }
                if (spec.Mode == PowerShellCompilationMode.Strict && typed.Diagnostics.Length > 0)
                    throw new InvalidOperationException($"Strict mode rejected {typed.Diagnostics.Length} compilation blocker(s). {DescribeBlockers(typed.Diagnostics)}");
                if (spec.Mode == PowerShellCompilationMode.Strict &&
                    plan.Files.SelectMany(static file => file.Units).Any(static unit => unit.Kind != PowerShellCompilationUnitKind.Function))
                    throw new InvalidOperationException("Strict DLL compilation rejected a top-level script unit because DLL emitters currently produce typed functions only.");
                File.WriteAllText(Path.Combine(workspace, "CompiledPowerShell.cs"), typed.SourceCode, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                if (spec.Kind == PowerShellCompilationArtifactKind.BinaryModule)
                {
                    runtimeRoutedUnits = exportedFunctions is null
                        ? typed.Methods.Count(static method => method.Lifecycle is null)
                        : exportedFunctions.Count(name => typed.Methods.Any(method =>
                            method.Lifecycle is null &&
                            method.SourceName.Equals(name, StringComparison.OrdinalIgnoreCase)));
                    File.WriteAllText(
                        Path.Combine(workspace, "CompiledCmdlets.cs"),
                        PowerShellBinaryCmdletSourceGenerator.Generate(typed, exportedFunctions, spec.TargetFramework),
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                }
                else
                {
                    runtimeRoutedUnits = typed.Methods.Length;
                }
                projectPath = Path.Combine(workspace, artifactName + ".csproj");
                var projectTemplate = spec.Kind == PowerShellCompilationArtifactKind.BinaryModule
                    ? ReadTemplate(BinaryModuleProjectTemplate).Replace("{{POWERSHELL_REFERENCE}}", GetPowerShellReference(spec.TargetFramework))
                    : ReadTemplate(TypedProjectTemplate);
                File.WriteAllText(
                    projectPath,
                    projectTemplate
                        .Replace("{{TARGET_FRAMEWORK}}", EscapeXml(spec.TargetFramework))
                        .Replace("{{TARGET_REFERENCE}}", PowerShellGeneratedReferenceAssemblyResolver.GetGeneratedProjectReference(spec.TargetFramework))
                        .Replace("{{ARTIFACT_NAME}}", EscapeXml(artifactName))
                        .Replace("{{ASSEMBLY_VERSION}}", EscapeXml(GetBinaryModuleAssemblyVersion(spec))),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                requiresPowerShellRuntime = spec.Kind == PowerShellCompilationArtifactKind.BinaryModule;
                usesPowerShellRuntimeFallback = spec.Kind == PowerShellCompilationArtifactKind.BinaryModule &&
                    spec.Mode == PowerShellCompilationMode.Hybrid &&
                    (runtimeRoutedUnits != plan.TotalUnits || runtimeManifestHooks.Length > 0);
                compiledUnits = typed.Methods.Count(static method => method.Lifecycle is null);
                compiledMethods = compiledUnits;
                compiledMethodDetails = spec.Kind == PowerShellCompilationArtifactKind.BinaryModule && exportedFunctions is not null
                    ? typed.Methods.Where(method => method.Lifecycle is null || exportedFunctions.Contains(method.SourceName, StringComparer.OrdinalIgnoreCase)).ToArray()
                    : typed.Methods;
                optimizationEvidence = typed.Optimization;
            }
            else
            {
                var packagedSources = PreparePackagedSources(workspace, spec.SourcePath, compilationSourcePaths, dependencyPlan);
                var parameterInitializers = PowerShellPackagedParameterBindingPolicy.Generate(spec.SourcePath, spec.TargetFramework);
                var packagedScript = GeneratePackagedScript(spec.SourcePath, packagedSources);
                var packagedScriptPath = Path.Combine(workspace, "Source.ps1");
                File.WriteAllText(
                    packagedScriptPath,
                    packagedScript,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                File.WriteAllText(
                    Path.Combine(workspace, "Program.cs"),
                    ReadTemplate(PackagedProgramTemplate)
                        .Replace("{{PARAMETERS}}", parameterInitializers.Parameters)
                        .Replace("{{SWITCH_PARAMETERS}}", parameterInitializers.SwitchParameters)
                        .Replace("{{BOOLEAN_PARAMETERS}}", parameterInitializers.BooleanParameters)
                        .Replace("{{PARAMETER_ALIASES}}", parameterInitializers.ParameterAliases)
                        .Replace("{{ENTRY_RELATIVE_PATH}}", PowerShellCSharpLiteral.QuoteString(packagedSources.EntryRelativePath))
                        .Replace("{{ENTRY_SHA256}}", PowerShellCSharpLiteral.QuoteString(ComputeSha256(packagedScriptPath)))
                        .Replace("{{DEPENDENCY_SPECS}}", packagedSources.DependencySpecs)
                        .Replace("{{TARGET_FRAMEWORK}}", PowerShellCSharpLiteral.QuoteString(spec.TargetFramework)),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                projectPath = Path.Combine(workspace, artifactName + ".csproj");
                File.WriteAllText(
                    projectPath,
                    ReadTemplate(PackagedProjectTemplate)
                        .Replace("{{TARGET_FRAMEWORK}}", EscapeXml(spec.TargetFramework))
                        .Replace("{{ARTIFACT_NAME}}", EscapeXml(artifactName))
                        .Replace("{{SINGLE_FILE}}", spec.SingleFile ? "true" : "false")
                        .Replace("{{SELF_CONTAINED}}", spec.SelfContained ? "true" : "false")
                        .Replace("{{POWERSHELL_SDK_VERSION}}", GetPowerShellSdkVersion(spec.TargetFramework))
                        .Replace("{{SECURITY_XML_VERSION}}", GetSecurityXmlVersion(spec.TargetFramework))
                        .Replace("{{DEPENDENCY_RESOURCES}}", packagedSources.ProjectResources),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                requiresPowerShellRuntime = true;
                usesPowerShellRuntimeFallback = true;
                compiledUnits = 0;
                compiledMethods = 0;
                runtimeRoutedUnits = 0;
            }

            if (spec.Mode == PowerShellCompilationMode.Strict && !requiresPowerShellRuntime && compiledMethodDetails.Count > 0)
            {
                runtimeFreeContract = PowerShellRuntimeFreeArtifactContract.Create(
                    workspace,
                    "PowerForge.Compiled",
                    typed?.TypeName ?? "CompiledPowerShellScript",
                    compiledMethodDetails);
            }

            GeneratedBuildProcessResult? restore = null;
            if (spec.UseBuildCache)
            {
                restore = RunDotNetRestore(spec, projectPath, runtimeIdentifier);
                if (restore.TimedOut)
                    throw new TimeoutException($"Generated .NET restore exceeded {spec.TimeoutSeconds} seconds.");
                if (restore.ExitCode != 0)
                    throw new InvalidOperationException($"Generated .NET restore failed with exit code {restore.ExitCode}.{Environment.NewLine}{BoundOutput(restore.Output)}");
            }
            var buildCache = PowerShellCompilationArtifactBuildCache.CreateEvidence(
                spec,
                workspace,
                targetContract,
                dependencyGraph,
                toolchain);
            var process = PowerShellCompilationArtifactBuildCache.TryRestore(spec, buildCache, publishDirectory)
                ? new GeneratedBuildProcessResult(0, "PowerForge compilation build cache: verified content-addressed hit.", timedOut: false)
                : RunDotNetBuild(spec, projectPath, publishDirectory, runtimeIdentifier, restoreCompleted: restore is not null);
            result.BuildOutput = BoundOutput(string.Join(Environment.NewLine,
                new[] { restore?.Output, process.Output }.Where(static output => !string.IsNullOrWhiteSpace(output))));
            if (process.TimedOut)
                throw new TimeoutException($"Generated .NET build exceeded {spec.TimeoutSeconds} seconds.");
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Generated .NET build failed with exit code {process.ExitCode}.");
            PowerShellCompilationArtifactBuildCache.Store(spec, buildCache, publishDirectory);
            VerifyDependencyInputsHaveNotDrifted(spec, dependencyGraph);

            var artifactStagingDirectory = PowerShellArtifactSetPublisher.CreateStagingDirectory(spec.OutputDirectory, artifactName);
            try
            {
                var stagedArtifact = CopyArtifact(spec, artifactName, publishDirectory, typed, usesPowerShellRuntimeFallback, artifactStagingDirectory);
                stagedArtifact = stagedArtifact.WithAdditionalFiles(CopyPlannedPayload(
                    stagedArtifact.PrimaryPath,
                    artifactName,
                    dependencyPlan,
                    stagedArtifact.Files));
                stagedArtifact = stagedArtifact.WithAdditionalFiles(WriteBuildEvidence(
                    workspace,
                    artifactStagingDirectory,
                    artifactName,
                    spec,
                    targetContract,
                    toolchain,
                    dependencyGraph,
                    runtimeFreeContract?.GeneratedSourceSha256 ?? string.Empty));
                if (spec.Kind == PowerShellCompilationArtifactKind.BinaryModule && typed is not null)
                {
                    var externalHelpPath = PowerShellCompiledHelpWriter.WriteExternalHelp(
                        artifactName,
                        Path.GetDirectoryName(stagedArtifact.PrimaryPath)!,
                        typed.Methods);
                    if (externalHelpPath is not null)
                    {
                        stagedArtifact = stagedArtifact.WithAdditionalFiles(new[]
                        {
                            CreateArtifactFile(externalHelpPath, "ExternalHelp")
                        });
                    }
                }
                string? stagedGeneratedSourcePath = null;
                if (spec.EmitSource)
                {
                    stagedGeneratedSourcePath = PowerShellGeneratedSourcePublisher.CopyProject(
                        workspace,
                        projectPath,
                        artifactName,
                        artifactStagingDirectory,
                        spec,
                        compiledMethodDetails);
                    stagedArtifact = stagedArtifact.WithAdditionalFiles(
                        Directory.EnumerateFiles(stagedGeneratedSourcePath, "*", SearchOption.AllDirectories)
                            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                            .Select(path => CreateArtifactFile(
                                path,
                                Path.GetExtension(path).Equals(".csproj", StringComparison.OrdinalIgnoreCase)
                                    ? "GeneratedProject"
                                    : Path.GetFileName(path).Equals("source-map.json", StringComparison.OrdinalIgnoreCase)
                                        ? "GeneratedSourceMap"
                                    : Path.GetFileName(path).Equals("global.json", StringComparison.OrdinalIgnoreCase) ||
                                      Path.GetFileName(path).StartsWith("Directory.", StringComparison.OrdinalIgnoreCase)
                                        ? "GeneratedBuildIsolation"
                                    : Path.GetExtension(path).Equals(".ps1", StringComparison.OrdinalIgnoreCase)
                                        ? "GeneratedPackagedSource"
                                        : "GeneratedSource")));
                }
                var signing = PowerShellCompilationArtifactSigner.Sign(spec, stagedArtifact.Files);
                if (signing is not null)
                {
                    foreach (var file in stagedArtifact.Files)
                    {
                        file.Sha256 = ComputeSha256(file.Path);
                        file.SizeBytes = new FileInfo(file.Path).Length;
                    }
                }
                if (spec.Mode == PowerShellCompilationMode.Strict && !requiresPowerShellRuntime)
                {
                    dependencyClosure = _verifyStrictDependencyClosure(new PowerShellStrictDependencyClosureRequest(
                        stagedArtifact.Files,
                        spec.TargetFramework,
                        runtimeIdentifier,
                        dependencyGraph,
                        spec.Optimization));
                    EnsureStrictDependencyClosureCertified(dependencyClosure);
                }
                var artifactPath = PowerShellArtifactSetPublisher.RebasePath(stagedArtifact.PrimaryPath, artifactStagingDirectory, spec.OutputDirectory);
                var generatedSourcePath = stagedGeneratedSourcePath is null
                    ? null
                    : PowerShellArtifactSetPublisher.RebasePath(stagedGeneratedSourcePath, artifactStagingDirectory, spec.OutputDirectory);
                var analyzedUnits = plan.TotalUnits;
                var emittedUnits = compiledUnits;
                var fallbackUnits = plan.RuntimeFallbackUnits;
                var shapedFallbackUnits = spec.Mode == PowerShellCompilationMode.Hybrid && usesPowerShellRuntimeFallback
                    ? Math.Max(0, analyzedUnits - emittedUnits - fallbackUnits)
                    : 0;
                var runtimeRoutedFallbackUnits = usesPowerShellRuntimeFallback
                    ? fallbackUnits + shapedFallbackUnits
                    : 0;
                var omittedUnits = spec.Kind == PowerShellCompilationArtifactKind.Library
                    ? Math.Max(0, analyzedUnits - emittedUnits)
                    : 0;
                var boundaryEvidence = CreateBoundaryEvidence(compiledMethodDetails, runtimeRoutedFallbackUnits, spec.BoundaryRuntimeProfile);
                var diagnostics = typed?.Diagnostics ?? plan.Files
                    .SelectMany(static file => file.Diagnostics.Concat(file.Units.SelectMany(static unit => unit.Diagnostics)))
                    .ToArray();
                var commandProviders = compiledMethodDetails.SelectMany(static method => method.CommandProviders)
                    .GroupBy(static provider => provider.ProviderId + "\0" + provider.ProviderVersion, StringComparer.Ordinal)
                    .Select(static group => group.First())
                    .OrderBy(static provider => provider.ProviderId, StringComparer.Ordinal)
                    .ThenBy(static provider => provider.ProviderVersion, StringComparer.Ordinal)
                    .ToArray();
                var decisionTrace = PowerShellCompilationExplanationService.CreateFinal(plan, spec.Kind, typed);
                var reproduction = PowerShellCompilationReproductionEvidenceBuilder.Create(
                    plan,
                    spec.Kind,
                    decisionTrace,
                    toolchain,
                    runtimeFreeContract?.SemanticProfile,
                    runtimeFreeContract?.PublicAbi,
                    runtimeFreeContract?.GeneratedSourceSha256 ?? string.Empty,
                    stagedArtifact.Files,
                    diagnostics,
                    commandProviders);
                var manifest = new PowerShellCompilationArtifactManifest
                {
                    ArtifactName = artifactName,
                    Kind = spec.Kind,
                    Mode = spec.Mode,
                    SourcePath = spec.SourcePath,
                    SourceFiles = compilationSourcePaths,
                    TargetFramework = spec.TargetFramework,
                    RuntimeIdentifier = runtimeIdentifier,
                    TargetContract = targetContract,
                    Toolchain = toolchain,
                    BuildCache = buildCache,
                    IrOptimization = optimizationEvidence,
                    DecisionTrace = decisionTrace,
                    Reproduction = reproduction,
                    Boundaries = boundaryEvidence,
                    RequiresPowerShellRuntime = requiresPowerShellRuntime,
                    UsesPowerShellRuntimeFallback = usesPowerShellRuntimeFallback,
                    SemanticProfile = runtimeFreeContract?.SemanticProfile,
                    PublicAbi = runtimeFreeContract?.PublicAbi,
                    GeneratedSourceSha256 = runtimeFreeContract?.GeneratedSourceSha256 ?? string.Empty,
                    ContainsEmbeddedPowerShellSource = spec.Kind == PowerShellCompilationArtifactKind.Executable && requiresPowerShellRuntime ||
                        stagedArtifact.Files.Any(static file => PowerShellStrictDependencyClosureVerifier.IsPowerShellSource(file.Path)),
                    AllowsPowerShellRuntimeEvaluation = requiresPowerShellRuntime || usesPowerShellRuntimeFallback,
                    DependencyClosureVerified = dependencyClosure?.Verified == true,
                    DependencyClosure = dependencyClosure,
                    SelfContained = spec.Kind == PowerShellCompilationArtifactKind.Executable && spec.SelfContained,
                    SingleFile = spec.Kind == PowerShellCompilationArtifactKind.Executable && spec.SingleFile,
                    Optimization = spec.Optimization,
                    CompiledMethods = compiledMethods,
                    AnalyzedUnits = analyzedUnits,
                    EmittedUnits = emittedUnits,
                    RuntimeRoutedUnits = runtimeRoutedFallbackUnits,
                    FallbackUnits = fallbackUnits,
                    ShapedFallbackUnits = shapedFallbackUnits,
                    RuntimeFallbackUnits = runtimeRoutedFallbackUnits,
                    OmittedUnits = omittedUnits,
                    CompilationCoveragePercentage = analyzedUnits == 0 ? 0 : emittedUnits * 100d / analyzedUnits,
                    ArtifactPath = artifactPath,
                    GeneratedSourcePath = generatedSourcePath,
                    ArtifactSha256 = ComputeSha256(stagedArtifact.PrimaryPath),
                    ArtifactSizeBytes = new FileInfo(stagedArtifact.PrimaryPath).Length,
                    AuthenticodeSigned = signing is not null,
                    SigningCertificateThumbprint = signing?.CertificateThumbprint,
                    AuthenticodeSignedFiles = signing?.SignedFiles ?? 0,
                    Files = PowerShellArtifactSetPublisher.RebaseFiles(stagedArtifact.Files, artifactStagingDirectory, spec.OutputDirectory),
                    Dependencies = dependencyPlan,
                    DependencyGraph = dependencyGraph,
                    DependencyLockReviewed = spec.ExpectedDependencyLock is not null,
                    CommandProviders = commandProviders,
                    Lifecycles = compiledMethodDetails.Where(static method => method.Lifecycle is not null)
                        .Select(static method => method.Lifecycle!)
                        .OrderBy(static lifecycle => lifecycle.SourceSha256, StringComparer.Ordinal)
                        .ToArray(),
                    ResourceSummary = PowerShellCompilationResourceSummary.Create(dependencyPlan),
                    Diagnostics = diagnostics
                };
                var manifestPath = Path.Combine(spec.OutputDirectory, artifactName + ".powerforge-compilation.json");
                WriteManifest(Path.Combine(artifactStagingDirectory, Path.GetFileName(manifestPath)), manifest);
            PowerShellArtifactSetPublisher.Commit(
                    artifactStagingDirectory,
                    spec.OutputDirectory,
                    artifactName,
                    PowerShellCompiledModuleManifest.GetProtectedSourceFiles(
                            spec.SourcePath,
                            spec.Kind == PowerShellCompilationArtifactKind.BinaryModule && spec.Mode == PowerShellCompilationMode.Hybrid,
                            spec.ModuleManifestPath)
                        .Concat(compilationSourcePaths)
                        .Concat(dependencyPlan
                            .Where(static dependency => dependency.SourcePath is not null && dependency.Exists)
                            .Select(static dependency => dependency.SourcePath!))
                        .Distinct(PowerShellCompilationPathSafety.PathComparer));

                result.Succeeded = true;
                result.ArtifactPath = artifactPath;
                result.ManifestPath = manifestPath;
                result.GeneratedSourcePath = generatedSourcePath;
                result.Manifest = manifest;
                return result;
            }
            finally
            {
                PowerShellArtifactSetPublisher.TryDeleteDirectory(artifactStagingDirectory);
            }
        }
        catch (Exception ex)
        {
            result.Succeeded = false;
            result.Error = ex.Message;
            return result;
        }
    }

    private static void EnsureStrictDependencyClosureCertified(PowerShellCompilationDependencyClosure? closure)
    {
        if (closure?.Verified == true && closure.Limitations.Count == 0)
            return;

        var limitations = closure?.Limitations
            .Where(static limitation => !string.IsNullOrWhiteSpace(limitation))
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? Array.Empty<string>();
        var detail = limitations.Length == 0
            ? "The delivered dependency closure did not produce positive certification evidence."
            : string.Join(" ", limitations);
        throw new InvalidOperationException(
            "Strict runtime-free artifact publication requires a fully certified delivered dependency closure. " + detail);
    }

    internal static bool ShouldEnablePublishSingleFile(PowerShellCompilationBuildSpec spec)
        => spec.SingleFile && spec.Optimization != PowerShellCompilationExecutableOptimization.NativeAot;

    private static GeneratedBuildProcessResult RunDotNetBuild(
        PowerShellCompilationBuildSpec spec,
        string projectPath,
        string publishDirectory,
        string? runtimeIdentifier,
        bool restoreCompleted)
    {
        var arguments = new List<string>
        {
            spec.Kind == PowerShellCompilationArtifactKind.Executable ? "publish" : "build",
            projectPath,
            "--configuration", "Release",
            "--output", publishDirectory,
            "--nologo",
            "--verbosity", "minimal"
        };
        if (restoreCompleted) arguments.Add("--no-restore");
        if (spec.Kind == PowerShellCompilationArtifactKind.Executable && !string.IsNullOrWhiteSpace(runtimeIdentifier))
        {
            arguments.Add("--runtime");
            arguments.Add(runtimeIdentifier!);
        }

        var run = new ProcessRunner().RunAsync(new ProcessRunRequest(
                "dotnet",
                Path.GetDirectoryName(projectPath) ?? Directory.GetCurrentDirectory(),
                arguments,
                TimeSpan.FromSeconds(spec.TimeoutSeconds)))
            .GetAwaiter()
            .GetResult();
        var output = string.IsNullOrWhiteSpace(run.StdErr)
            ? run.StdOut
            : run.StdOut + Environment.NewLine + run.StdErr;
        return new GeneratedBuildProcessResult(run.ExitCode, output, run.TimedOut);
    }

    private static GeneratedBuildProcessResult RunDotNetRestore(
        PowerShellCompilationBuildSpec spec,
        string projectPath,
        string? runtimeIdentifier)
    {
        var arguments = new List<string>
        {
            "restore", projectPath, "--nologo", "--verbosity", "minimal"
        };
        if (spec.Kind == PowerShellCompilationArtifactKind.Executable && !string.IsNullOrWhiteSpace(runtimeIdentifier))
        {
            arguments.Add("--runtime");
            arguments.Add(runtimeIdentifier!);
        }
        var run = new ProcessRunner().RunAsync(new ProcessRunRequest(
            "dotnet",
            Path.GetDirectoryName(projectPath) ?? Directory.GetCurrentDirectory(),
            arguments,
            TimeSpan.FromSeconds(spec.TimeoutSeconds))).GetAwaiter().GetResult();
        var output = string.IsNullOrWhiteSpace(run.StdErr) ? run.StdOut : run.StdOut + Environment.NewLine + run.StdErr;
        return new GeneratedBuildProcessResult(run.ExitCode, output, run.TimedOut);
    }

    private static string GetPowerShellSdkVersion(string targetFramework)
        => targetFramework.Equals("net10.0", StringComparison.OrdinalIgnoreCase) ? "7.6.5" : "7.4.18";

    private static string GetSecurityXmlVersion(string targetFramework)
        => "10.0.11";

    private static string GetPowerShellReference(string targetFramework)
        => targetFramework.Equals("net472", StringComparison.OrdinalIgnoreCase)
            ? "<PackageReference Include=\"Microsoft.PowerShell.5.ReferenceAssemblies\" Version=\"1.1.0\" PrivateAssets=\"all\" />"
            : $"<PackageReference Include=\"Microsoft.PowerShell.SDK\" Version=\"{GetPowerShellSdkVersion(targetFramework)}\" PrivateAssets=\"all\" ExcludeAssets=\"runtime\" />{Environment.NewLine}    " +
              $"<PackageReference Include=\"System.Security.Cryptography.Xml\" Version=\"{GetSecurityXmlVersion(targetFramework)}\" PrivateAssets=\"all\" ExcludeAssets=\"runtime\" />";

    private static string ReadTemplate(string resourceName)
    {
        using var stream = typeof(PowerShellCompilationArtifactBuilder).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded compilation template '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void WriteManifest(string path, PowerShellCompilationArtifactManifest manifest)
    {
        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new JsonStringEnumConverter());
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, options), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var algorithm = SHA256.Create();
        return string.Concat(algorithm.ComputeHash(stream).Select(static value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static string SanitizeArtifactName(string value)
    {
        var sanitized = new string(value.Trim().Select(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_' ? character : '_').ToArray());
        if (string.IsNullOrWhiteSpace(sanitized) || sanitized is "." or "..")
            throw new ArgumentException("Artifact name does not contain a usable file name.", nameof(value));
        if (new[] { ".exe", ".dll", ".pdb", ".generated", ".powerforge-compilation.json" }
            .Any(suffix => sanitized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Artifact name must not end with a generated artifact suffix because it can overlap another artifact set.", nameof(value));
        PowerShellArtifactSetPublisher.EnsureArtifactNameIsNotReserved(sanitized, nameof(value));
        return sanitized;
    }

    private static string EscapeXml(string value)
        => System.Security.SecurityElement.Escape(value) ?? string.Empty;

    private static string GetBinaryModuleAssemblyVersion(PowerShellCompilationBuildSpec spec)
    {
        if (spec.Kind != PowerShellCompilationArtifactKind.BinaryModule)
            return "1.0.0.0";
        var manifestPath = PowerShellCompiledModuleManifest.ResolveSourceManifest(spec.SourcePath, spec.ModuleManifestPath);
        if (!File.Exists(manifestPath)) return "1.0.0.0";
        var value = ModuleManifestValueReader.ReadTopLevelString(manifestPath, "ModuleVersion");
        if (string.IsNullOrWhiteSpace(value)) return "1.0.0.0";
        if (!Version.TryParse(value, out var version))
            throw new InvalidOperationException($"Module manifest '{manifestPath}' declares invalid ModuleVersion '{value}'.");
        return new Version(
            version.Major,
            version.Minor,
            Math.Max(0, version.Build),
            Math.Max(0, version.Revision)).ToString(4);
    }

    private static string BoundOutput(string output)
        => output.Length <= MaximumBuildOutputLength ? output : output.Substring(output.Length - MaximumBuildOutputLength);

    private static string DescribeBlockers(IEnumerable<PowerShellCompilationDiagnostic> diagnostics)
        => string.Join(" ", diagnostics.Select(static diagnostic => diagnostic.Message)
            .Where(static message => !string.IsNullOrWhiteSpace(message))
            .Distinct(StringComparer.Ordinal));

    private sealed class CopiedArtifact
    {
        internal CopiedArtifact(string primaryPath, PowerShellCompilationArtifactFile[] files)
        {
            PrimaryPath = primaryPath;
            Files = files;
        }

        internal string PrimaryPath { get; }
        internal PowerShellCompilationArtifactFile[] Files { get; }

        internal CopiedArtifact WithAdditionalFiles(IEnumerable<PowerShellCompilationArtifactFile> files)
            => new(PrimaryPath, Files.Concat(files).ToArray());
    }

    private sealed class GeneratedBuildProcessResult
    {
        internal GeneratedBuildProcessResult(int exitCode, string output, bool timedOut)
        {
            ExitCode = exitCode;
            Output = output;
            TimedOut = timedOut;
        }

        internal int ExitCode { get; }
        internal string Output { get; }
        internal bool TimedOut { get; }
    }

    private sealed class PackagedSourceSet
    {
        internal PackagedSourceSet(
            string entryRelativePath,
            string projectResources,
            string dependencySpecs,
            bool hasDependencies,
            string[] embeddedScriptPaths,
            string[] embeddedResourceRelativePaths,
            bool usesExtractedRoot)
        {
            EntryRelativePath = entryRelativePath;
            ProjectResources = projectResources;
            DependencySpecs = dependencySpecs;
            HasDependencies = hasDependencies;
            EmbeddedScriptPaths = embeddedScriptPaths;
            EmbeddedResourceRelativePaths = embeddedResourceRelativePaths;
            UsesExtractedRoot = usesExtractedRoot;
        }

        internal string EntryRelativePath { get; }
        internal string ProjectResources { get; }
        internal string DependencySpecs { get; }
        internal bool HasDependencies { get; }
        internal string[] EmbeddedScriptPaths { get; }
        internal string[] EmbeddedResourceRelativePaths { get; }
        internal bool UsesExtractedRoot { get; }
    }

}
