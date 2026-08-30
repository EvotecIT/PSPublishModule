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
        using var workspaceLease = PowerShellCompilationWorkspace.Create(spec.KeepBuildWorkspace, spec.OfflineRestore);
        var workspace = workspaceLease.Path;
        var result = new PowerShellCompilationBuildResult { BuildWorkspace = spec.KeepBuildWorkspace ? workspace : null };
        var failureStage = PowerShellCompilationFailureStage.Input;
        PowerShellCompilationPlan? failurePlan = null;
        PowerShellCompilationFailureMap? failureMap = null;
        int? failureExitCode = null;

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
            failureStage = PowerShellCompilationFailureStage.Dependency;
            var providerResolution = ResolveProviderPackages(spec);
            var providerRuntimeAssemblies = PrepareProviderRuntimeAssemblies(workspace, providerResolution);
            var providerProjectReferences = CreateProviderProjectReferences(providerRuntimeAssemblies);
            var commandProviderInputs = spec.CommandProviders
                .Concat(providerResolution.Providers)
                .OrderBy(static provider => provider.ProviderId, StringComparer.Ordinal)
                .ThenBy(static provider => provider.ProviderVersion, StringComparer.Ordinal)
                .ThenBy(static provider => provider.CommandName, StringComparer.Ordinal)
                .ToArray();
            var dependencyPlan = PowerShellCompilationDependencyPlanner.Analyze(spec, compilationSourcePaths);
            var dependencyGraph = PowerShellCompilationDependencyPlanner.AnalyzeGraph(spec, compilationSourcePaths, dependencyPlan);
            var generatedAssemblyName = ResolveGeneratedAssemblyName(spec, artifactName, dependencyGraph);
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
            failureStage = PowerShellCompilationFailureStage.Analysis;
            var capabilities = PowerShellCompilationBuildSpec.GetCapabilities(spec.Kind, spec.Mode);
            var plan = AnalyzeCompilationSources(compilationSourcePaths, spec.Mode, spec.TargetFramework, spec.SemanticProfileId, capabilities, commandProviderInputs);
            failurePlan = plan;
            if (plan.ParseErrorFiles > 0)
                throw new InvalidOperationException("PowerShell source contains parser errors; no artifact was produced.");

            var publishDirectory = Path.Combine(workspace, "publish");
            Directory.CreateDirectory(publishDirectory);
            PowerShellTypedCompilationResult? typed = null;
            string projectPath;
            bool requiresPowerShellRuntime;
            bool usesPowerShellRuntimeFallback;
            int compiledMethods;
            IReadOnlyCollection<PowerShellCompiledMethod> compiledMethodDetails = Array.Empty<PowerShellCompiledMethod>();
            var optimizationEvidence = new PowerShellCompilationOptimizationEvidence();
            PowerShellCompilationIrSnapshotBundle? irSnapshots = null;
            PowerShellRuntimeFreeArtifactContract? runtimeFreeContract = null;
            PowerShellCompilationAbiManifest? publicAbi = null;
            PowerShellCompilationDependencyClosure? dependencyClosure = null;
            var runtimeManifestHooks = spec.Kind == PowerShellCompilationArtifactKind.BinaryModule
                ? PowerShellCompiledModuleManifest.GetRuntimeScriptHooks(spec.SourcePath, spec.ModuleManifestPath)
                : Array.Empty<string>();
            if (spec.Mode == PowerShellCompilationMode.Strict && runtimeManifestHooks.Length > 0)
                throw new InvalidOperationException(
                    $"Strict binary-module compilation rejected manifest runtime script hook(s): {string.Join(", ", runtimeManifestHooks)}.");
            if (spec.Kind == PowerShellCompilationArtifactKind.Executable && spec.Mode == PowerShellCompilationMode.Strict)
            {
                var executable = PowerShellTypedExecutableEmitter.Emit(spec.SourcePath, compilationSourcePaths, plan, spec.TargetFramework, spec.SemanticProfileId);
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
                        .Replace("{{PUBLISH_AOT}}", spec.Optimization == PowerShellCompilationExecutableOptimization.NativeAot ? "true" : "false")
                        .Replace("{{PROVIDER_REFERENCES}}", providerProjectReferences),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                requiresPowerShellRuntime = false;
                usesPowerShellRuntimeFallback = false;
                compiledMethods = executable.Methods.Length;
                compiledMethodDetails = executable.Methods;
                optimizationEvidence = executable.Optimization;
                irSnapshots = executable.IrSnapshots;
            }
            else if (spec.Kind == PowerShellCompilationArtifactKind.Executable && spec.Mode == PowerShellCompilationMode.Hybrid)
            {
                var hybrid = PrepareHybridExecutable(
                    workspace,
                    artifactName,
                    spec,
                    compilationSourcePaths,
                    plan,
                    dependencyPlan,
                    commandProviderInputs,
                    providerProjectReferences);
                typed = hybrid.Typed;
                projectPath = hybrid.ProjectPath;
                requiresPowerShellRuntime = true;
                compiledMethodDetails = hybrid.CompiledMethods;
                compiledMethods = hybrid.CompiledMethods.Length;
                usesPowerShellRuntimeFallback = plan.TotalUnits > compiledMethods;
                optimizationEvidence = typed.Optimization;
                irSnapshots = typed.IrSnapshots;
            }
            else if (spec.Kind is PowerShellCompilationArtifactKind.Library or PowerShellCompilationArtifactKind.BinaryModule)
            {
                if (spec.Mode == PowerShellCompilationMode.Package)
                    throw new InvalidOperationException("DLL artifacts require Hybrid or Strict mode because they contain genuinely typed methods.");
                var transpiler = new PowerShellTypedCompilationTranspiler(commandProviderInputs, spec.SemanticProfileId);
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
                        typed = PowerShellHybridFunctionCollisionResolver.RouteNameCollisionsToFallback(typed, spec.TargetFramework, spec.SemanticProfileId);
                        typed = PowerShellAdvancedFunctionLifecyclePlanner.AddHostedLifecycleMethods(typed, spec.TargetFramework);
                    }
                    exportedFunctions = exportContract?.SelectFunctions(typed.Methods.Select(static method => method.SourceName));
                    typed = PowerShellBinaryCmdletSourceGenerator.PrepareForBinaryModule(typed, exportedFunctions, spec.TargetFramework, spec.SemanticProfileId);
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
                    File.WriteAllText(
                        Path.Combine(workspace, "CompiledCmdlets.cs"),
                        PowerShellBinaryCmdletSourceGenerator.Generate(typed, exportedFunctions, spec.TargetFramework),
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
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
                        .Replace("{{PROVIDER_REFERENCES}}", providerProjectReferences)
                        .Replace("{{ARTIFACT_NAME}}", EscapeXml(generatedAssemblyName))
                        .Replace("{{ASSEMBLY_VERSION}}", EscapeXml(GetBinaryModuleAssemblyVersion(spec))),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                requiresPowerShellRuntime = spec.Kind == PowerShellCompilationArtifactKind.BinaryModule;
                usesPowerShellRuntimeFallback = spec.Kind == PowerShellCompilationArtifactKind.BinaryModule &&
                    spec.Mode == PowerShellCompilationMode.Hybrid &&
                    (typed.Methods.Count(static method => method.Lifecycle is null) != plan.TotalUnits || runtimeManifestHooks.Length > 0);
                compiledMethods = typed.Methods.Count(static method => method.Lifecycle is null);
                compiledMethodDetails = spec.Kind == PowerShellCompilationArtifactKind.BinaryModule && exportedFunctions is not null
                    ? typed.Methods.Where(method => method.Lifecycle is null || exportedFunctions.Contains(method.SourceName, StringComparer.OrdinalIgnoreCase)).ToArray()
                    : typed.Methods;
                optimizationEvidence = typed.Optimization;
                irSnapshots = typed.IrSnapshots;
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
                        .Replace("{{PROVIDER_REFERENCES}}", providerProjectReferences)
                        .Replace("{{DEPENDENCY_RESOURCES}}", packagedSources.ProjectResources),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                requiresPowerShellRuntime = true;
                usesPowerShellRuntimeFallback = true;
                compiledMethods = 0;
            }

            var unitDispositionLedger = PowerShellCompilationUnitDispositionLedgerBuilder.Create(
                plan,
                spec.Kind,
                typed,
                spec.SourcePath,
                runtimeManifestHooks.Select(static hook => "Manifest runtime script hook: " + hook),
                compiledMethodDetails);
            usesPowerShellRuntimeFallback = unitDispositionLedger.UsesPowerShellRuntimeFallback;
            failureMap = PowerShellCompilationDiagnosticsEvidenceBuilder.CreateFailureMap(plan, compiledMethodDetails, unitDispositionLedger);

            if (spec.Mode == PowerShellCompilationMode.Strict && compiledMethodDetails.Count > 0)
            {
                failureStage = PowerShellCompilationFailureStage.Abi;
                if (!requiresPowerShellRuntime)
                {
                    runtimeFreeContract = PowerShellRuntimeFreeArtifactContract.Create(
                        workspace,
                        "PowerForge.Compiled",
                        typed?.TypeName ?? "CompiledPowerShellScript",
                        compiledMethodDetails);
                    publicAbi = runtimeFreeContract.PublicAbi;
                }
                else if (spec.Kind == PowerShellCompilationArtifactKind.BinaryModule)
                {
                    publicAbi = PowerShellCompilationAbiBuilder.Create(
                        "PowerForge.Compiled",
                        typed?.TypeName ?? PowerShellCSharpSymbolRenderer.Identifier(artifactName) + "Methods",
                        compiledMethodDetails);
                    File.WriteAllText(
                        Path.Combine(workspace, "PowerForgePublicAbiContract.g.cs"),
                        PowerShellRuntimeFreeContractSource.GeneratePublicAbiMetadata(publicAbi),
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                }
            }
            if (!string.IsNullOrWhiteSpace(spec.ExpectedPublicAbiSha256) &&
                !string.Equals(publicAbi?.Sha256, spec.ExpectedPublicAbiSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The generated public ABI does not match the expected ABI SHA-256.");

            var resolvedPackageLockSha256 = PrepareExactNuGetClosureLock(spec, projectPath);
            GeneratedBuildProcessResult? restore = null;
            if (spec.UseBuildCache || !string.IsNullOrWhiteSpace(spec.NuGetLockFilePath))
            {
                failureStage = PowerShellCompilationFailureStage.Restore;
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
            failureStage = spec.Optimization == PowerShellCompilationExecutableOptimization.None
                ? PowerShellCompilationFailureStage.Build
                : PowerShellCompilationFailureStage.Optimization;
            var process = PowerShellCompilationArtifactBuildCache.TryRestore(spec, buildCache, publishDirectory)
                ? new GeneratedBuildProcessResult(0, "PowerForge compilation build cache: verified content-addressed hit.", timedOut: false)
                : RunDotNetBuild(spec, projectPath, publishDirectory, runtimeIdentifier, restoreCompleted: restore is not null);
            result.BuildOutput = BoundOutput(string.Join(Environment.NewLine,
                new[] { restore?.Output, process.Output }.Where(static output => !string.IsNullOrWhiteSpace(output))));
            if (process.TimedOut)
                throw new TimeoutException($"Generated .NET build exceeded {spec.TimeoutSeconds} seconds.");
            if (process.ExitCode != 0)
            {
                failureExitCode = process.ExitCode;
                throw new InvalidOperationException($"Generated .NET build failed with exit code {process.ExitCode}.");
            }
            PowerShellCompilationArtifactBuildCache.Store(spec, buildCache, publishDirectory);
            VerifyDependencyInputsHaveNotDrifted(spec, dependencyGraph);

            failureStage = PowerShellCompilationFailureStage.Publication;
            var artifactStagingDirectory = PowerShellArtifactSetPublisher.CreateStagingDirectory(spec.OutputDirectory, artifactName);
            try
            {
                var stagedArtifact = CopyArtifact(spec, artifactName, generatedAssemblyName, publishDirectory, typed, usesPowerShellRuntimeFallback, artifactStagingDirectory);
                stagedArtifact = stagedArtifact.WithAdditionalFiles(CopyProviderRuntimeAssemblies(
                    spec,
                    stagedArtifact.PrimaryPath,
                    providerRuntimeAssemblies,
                    stagedArtifact.Files));
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
                    providerResolution.Lock,
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
                var irSnapshotEvidence = new PowerShellCompilationIrSnapshotEvidence { Emitted = false };
                if (spec.EmitIrSnapshots)
                {
                    irSnapshotEvidence = PowerShellCompilationDiagnosticsEvidenceBuilder.PublishIrSnapshots(
                        Path.GetDirectoryName(stagedArtifact.PrimaryPath) ?? artifactStagingDirectory,
                        artifactName,
                        irSnapshots,
                        out var irSnapshotFile);
                    if (irSnapshotFile is not null)
                        stagedArtifact = stagedArtifact.WithAdditionalFiles(new[] { irSnapshotFile });
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
                        spec.Optimization,
                        providerResolution.Lock.Packages.Length == 0 ? null : providerResolution.Lock));
                    EnsureStrictDependencyClosureCertified(dependencyClosure);
                }
                var artifactPath = PowerShellArtifactSetPublisher.RebasePath(stagedArtifact.PrimaryPath, artifactStagingDirectory, spec.OutputDirectory);
                var generatedSourcePath = stagedGeneratedSourcePath is null
                    ? null
                    : PowerShellArtifactSetPublisher.RebasePath(stagedGeneratedSourcePath, artifactStagingDirectory, spec.OutputDirectory);
                var boundaryEvidence = CreateBoundaryEvidence(unitDispositionLedger, spec.BoundaryRuntimeProfile);
                var diagnostics = typed?.Diagnostics ?? plan.Files
                    .SelectMany(static file => file.Diagnostics.Concat(file.Units.SelectMany(static unit => unit.Diagnostics)))
                    .ToArray();
                diagnostics = PowerShellCompilationReproductionEvidenceBuilder.MakeDiagnosticsPortable(plan, diagnostics);
                var commandProviders = compiledMethodDetails.SelectMany(static method => method.CommandProviders)
                    .GroupBy(static provider => provider.ProviderId + "\0" + provider.ProviderVersion + "\0" + provider.CommandName, StringComparer.Ordinal)
                    .Select(static group => group.First())
                    .OrderBy(static provider => provider.ProviderId, StringComparer.Ordinal)
                    .ThenBy(static provider => provider.ProviderVersion, StringComparer.Ordinal)
                    .ThenBy(static provider => provider.CommandName, StringComparer.Ordinal)
                    .ToArray();
                var decisionTrace = PowerShellCompilationExplanationService.CreateFinal(plan, unitDispositionLedger);
                var diagnosticAudit = PowerShellCompilationDiagnosticsEvidenceBuilder.CreateAuditTrail(
                    spec,
                    buildCache,
                    dependencyGraph,
                    publicAbi,
                    unitDispositionLedger,
                    commandProviders);
                var diagnosticsPolicy = spec.DiagnosticsPolicy ?? PowerShellCompilationDiagnosticsEvidenceBuilder.CreatePolicy();
                var reproduction = PowerShellCompilationReproductionEvidenceBuilder.Create(
                    plan,
                    spec.Kind,
                    unitDispositionLedger,
                    decisionTrace,
                    toolchain,
                    runtimeFreeContract?.SemanticProfile,
                    publicAbi,
                    providerResolution.Lock,
                    runtimeFreeContract?.GeneratedSourceSha256 ?? string.Empty,
                    stagedArtifact.Files,
                    diagnostics,
                    commandProviders,
                    irSnapshotEvidence,
                    failureMap,
                    diagnosticAudit,
                    diagnosticsPolicy);
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
                    ResolvedPackageLockSha256 = resolvedPackageLockSha256,
                    IrOptimization = optimizationEvidence,
                    IrSnapshots = irSnapshotEvidence,
                    DecisionTrace = decisionTrace,
                    UnitDispositionLedger = unitDispositionLedger,
                    Reproduction = reproduction,
                    Boundaries = boundaryEvidence,
                    FailureMap = failureMap,
                    DiagnosticAudit = diagnosticAudit,
                    DiagnosticsPolicy = diagnosticsPolicy,
                    RequiresPowerShellRuntime = requiresPowerShellRuntime,
                    UsesPowerShellRuntimeFallback = usesPowerShellRuntimeFallback,
                    SemanticProfile = runtimeFreeContract?.SemanticProfile,
                    PublicAbi = publicAbi,
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
                    AnalyzedUnits = unitDispositionLedger.AnalyzedUnits,
                    EmittedUnits = unitDispositionLedger.EmittedUnits,
                    RuntimeRoutedUnits = unitDispositionLedger.RuntimeRoutedUnits,
                    FallbackUnits = unitDispositionLedger.FallbackUnits,
                    ShapedFallbackUnits = unitDispositionLedger.ShapedFallbackUnits,
                    RuntimeFallbackUnits = unitDispositionLedger.RuntimeRoutedUnits,
                    OmittedUnits = unitDispositionLedger.OmittedUnits,
                    CompilationCoveragePercentage = unitDispositionLedger.CompilationCoveragePercentage,
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
                    ProviderLock = providerResolution.Lock.Packages.Length == 0 ? null : providerResolution.Lock,
                    ProviderLockReviewed = providerResolution.Lock.Packages.Length > 0 && spec.ExpectedProviderLock is not null,
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
            result.Failure = PowerShellCompilationDiagnosticsEvidenceBuilder.MapFailure(
                failureStage,
                failureStage + "Failure",
                ex.Message,
                result.BuildOutput,
                failureExitCode,
                failurePlan,
                failureMap);
            result.Error = result.Failure.Summary;
            return result;
        }
    }

}
