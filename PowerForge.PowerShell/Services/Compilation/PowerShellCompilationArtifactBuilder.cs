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
    /// <summary>Builds the requested PowerShell artifact.</summary>
    public PowerShellCompilationBuildResult Build(PowerShellCompilationBuildSpec spec)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        ValidateSpec(spec);
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
            var plan = AnalyzeCompilationSources(compilationSourcePaths, spec.Mode, spec.TargetFramework, capabilities);
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
                runtimeRoutedUnits = plan.TotalUnits;
            }
            else if (spec.Kind is PowerShellCompilationArtifactKind.Library or PowerShellCompilationArtifactKind.BinaryModule)
            {
                if (spec.Mode == PowerShellCompilationMode.Package)
                    throw new InvalidOperationException("DLL artifacts require Hybrid or Strict mode because they contain genuinely typed methods.");
                var transpiler = new PowerShellTypedCompilationTranspiler();
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
                    exportedFunctions = exportContract?.SelectFunctions(typed.Methods.Select(static method => method.SourceName));
                    if (spec.Mode == PowerShellCompilationMode.Hybrid)
                        typed = PowerShellHybridFunctionCollisionResolver.RouteNameCollisionsToFallback(typed, spec.TargetFramework);
                    typed = PowerShellBinaryCmdletSourceGenerator.PrepareForBinaryModule(typed, exportedFunctions, spec.TargetFramework);
                }
                if (typed.Methods.Length == 0 &&
                    !(spec.Kind == PowerShellCompilationArtifactKind.BinaryModule && spec.Mode == PowerShellCompilationMode.Hybrid))
                {
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
                        ? typed.Methods.Length
                        : exportedFunctions.Count(name => typed.Methods.Any(method => method.SourceName.Equals(name, StringComparison.OrdinalIgnoreCase)));
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
                compiledUnits = typed.Methods.Length;
                compiledMethods = typed.Methods.Length;
                compiledMethodDetails = typed.Methods;
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

            var runtimeIdentifier = ResolveRuntimeIdentifier(spec);
            var process = RunDotNetBuild(spec, projectPath, publishDirectory, runtimeIdentifier);
            result.BuildOutput = BoundOutput(process.Output);
            if (process.TimedOut)
                throw new TimeoutException($"Generated .NET build exceeded {spec.TimeoutSeconds} seconds.");
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Generated .NET build failed with exit code {process.ExitCode}.");

            var artifactStagingDirectory = PowerShellArtifactSetPublisher.CreateStagingDirectory(spec.OutputDirectory, artifactName);
            try
            {
                var stagedArtifact = CopyArtifact(spec, artifactName, publishDirectory, typed, usesPowerShellRuntimeFallback, artifactStagingDirectory);
                stagedArtifact = stagedArtifact.WithAdditionalFiles(CopyPlannedPayload(
                    stagedArtifact.PrimaryPath,
                    artifactName,
                    dependencyPlan,
                    stagedArtifact.Files));
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
                    dependencyClosure = PowerShellStrictDependencyClosureVerifier.Verify(stagedArtifact.Files);
                var artifactPath = PowerShellArtifactSetPublisher.RebasePath(stagedArtifact.PrimaryPath, artifactStagingDirectory, spec.OutputDirectory);
                var generatedSourcePath = stagedGeneratedSourcePath is null
                    ? null
                    : PowerShellArtifactSetPublisher.RebasePath(stagedGeneratedSourcePath, artifactStagingDirectory, spec.OutputDirectory);
                var nonCompiledUnits = Math.Max(0, plan.TotalUnits - compiledUnits);
                var fallbackUnits = usesPowerShellRuntimeFallback
                    ? Math.Max(0, plan.TotalUnits - runtimeRoutedUnits) + runtimeManifestHooks.Length
                    : 0;
                var omittedUnits = spec.Kind == PowerShellCompilationArtifactKind.Library ? nonCompiledUnits : 0;
                var diagnostics = typed?.Diagnostics ?? plan.Files
                    .SelectMany(static file => file.Diagnostics.Concat(file.Units.SelectMany(static unit => unit.Diagnostics)))
                    .ToArray();
                var manifest = new PowerShellCompilationArtifactManifest
                {
                    ArtifactName = artifactName,
                    Kind = spec.Kind,
                    Mode = spec.Mode,
                    SourcePath = spec.SourcePath,
                    SourceFiles = compilationSourcePaths,
                    TargetFramework = spec.TargetFramework,
                    RuntimeIdentifier = runtimeIdentifier,
                    RequiresPowerShellRuntime = requiresPowerShellRuntime,
                    UsesPowerShellRuntimeFallback = usesPowerShellRuntimeFallback,
                    SemanticProfile = runtimeFreeContract?.SemanticProfile,
                    PublicAbi = runtimeFreeContract?.PublicAbi,
                    GeneratedSourceSha256 = runtimeFreeContract?.GeneratedSourceSha256 ?? string.Empty,
                    ContainsEmbeddedPowerShellSource = stagedArtifact.Files.Any(static file =>
                        PowerShellStrictDependencyClosureVerifier.IsPowerShellSource(file.Path)),
                    AllowsPowerShellRuntimeEvaluation = requiresPowerShellRuntime || usesPowerShellRuntimeFallback,
                    DependencyClosureVerified = dependencyClosure?.Verified == true,
                    DependencyClosure = dependencyClosure,
                    SelfContained = spec.Kind == PowerShellCompilationArtifactKind.Executable && spec.SelfContained,
                    SingleFile = spec.Kind == PowerShellCompilationArtifactKind.Executable && spec.SingleFile,
                    Optimization = spec.Optimization,
                    CompiledMethods = compiledMethods,
                    RuntimeFallbackUnits = fallbackUnits,
                    OmittedUnits = omittedUnits,
                    CompilationCoveragePercentage = plan.TotalUnits == 0 ? 0 : compiledUnits * 100d / plan.TotalUnits,
                    ArtifactPath = artifactPath,
                    GeneratedSourcePath = generatedSourcePath,
                    ArtifactSha256 = ComputeSha256(stagedArtifact.PrimaryPath),
                    ArtifactSizeBytes = new FileInfo(stagedArtifact.PrimaryPath).Length,
                    AuthenticodeSigned = signing is not null,
                    SigningCertificateThumbprint = signing?.CertificateThumbprint,
                    AuthenticodeSignedFiles = signing?.SignedFiles ?? 0,
                    Files = PowerShellArtifactSetPublisher.RebaseFiles(stagedArtifact.Files, artifactStagingDirectory, spec.OutputDirectory),
                    Dependencies = dependencyPlan,
                    CommandProviders = compiledMethodDetails.SelectMany(static method => method.CommandProviders)
                        .GroupBy(static provider => provider.ProviderId + "\0" + provider.ProviderVersion, StringComparer.Ordinal)
                        .Select(static group => group.First())
                        .OrderBy(static provider => provider.ProviderId, StringComparer.Ordinal)
                        .ThenBy(static provider => provider.ProviderVersion, StringComparer.Ordinal)
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

    private static void ValidateSpec(PowerShellCompilationBuildSpec spec)
    {
        if (!Enum.IsDefined(typeof(PowerShellCompilationArtifactKind), spec.Kind))
            throw new ArgumentOutOfRangeException(nameof(spec), "Artifact kind is not defined.");
        if (!Enum.IsDefined(typeof(PowerShellCompilationMode), spec.Mode))
            throw new ArgumentOutOfRangeException(nameof(spec), "Compilation mode is not defined.");
        if (!Enum.IsDefined(typeof(PowerShellCompilationExecutableOptimization), spec.Optimization))
            throw new ArgumentOutOfRangeException(nameof(spec), "Executable optimization is not defined.");
        if (!Enum.IsDefined(typeof(PowerShellCompilationResourceMode), spec.ResourceMode))
            throw new ArgumentOutOfRangeException(nameof(spec), "Resource mode is not defined.");
        if (!Enum.IsDefined(typeof(CertificateStoreLocation), spec.CertificateStoreLocation))
            throw new ArgumentOutOfRangeException(nameof(spec), "Certificate store location is not defined.");
        if (spec.Mode == PowerShellCompilationMode.Analyze)
            throw new ArgumentException("Analyze mode reports eligibility and does not produce artifacts. Use the analyzer API or CLI analyze command.", nameof(spec));
        if (!File.Exists(spec.SourcePath))
            throw new FileNotFoundException("PowerShell source file was not found.", spec.SourcePath);
        var extension = Path.GetExtension(spec.SourcePath);
        if (!extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".psm1", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("PowerShell artifacts accept .ps1 and .psm1 source files.", nameof(spec));
        if (spec.Kind == PowerShellCompilationArtifactKind.Executable &&
            !extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Executable compilation requires a standalone .ps1 entrypoint; a .psm1 module has no unambiguous application entrypoint.", nameof(spec));
        var effectiveManifestPath = spec.Kind == PowerShellCompilationArtifactKind.BinaryModule
            ? PowerShellCompiledModuleManifest.ResolveSourceManifest(spec.SourcePath, spec.ModuleManifestPath)
            : spec.ModuleManifestPath;
        if (!string.IsNullOrWhiteSpace(effectiveManifestPath) &&
            (!string.IsNullOrWhiteSpace(spec.ModuleManifestPath) || File.Exists(effectiveManifestPath)))
        {
            var moduleManifestPath = effectiveManifestPath!;
            if (!File.Exists(moduleManifestPath))
                throw new FileNotFoundException("PowerShell module manifest was not found.", moduleManifestPath);
            if (!Path.GetExtension(moduleManifestPath).Equals(".psd1", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("ModuleManifestPath must reference a .psd1 file.", nameof(spec));
            var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(spec.SourcePath));
            var manifestDirectory = Path.GetDirectoryName(Path.GetFullPath(moduleManifestPath));
            if (!PowerShellCompilationPathSafety.PathEquals(sourceDirectory, manifestDirectory))
                throw new ArgumentException("The source .psm1 and module manifest must reside in the same module directory.", nameof(spec));
            PowerShellCompiledModuleManifest.EnsureManifestOwnsSource(spec.SourcePath, moduleManifestPath);
            PowerShellCompilationPathSafety.EnsureNoLinks(
                sourceDirectory!,
                Path.GetFullPath(moduleManifestPath),
                $"PowerShell module manifest '{moduleManifestPath}' traverses a symbolic link or junction.");
        }
        if (spec.TimeoutSeconds < 1)
            throw new ArgumentOutOfRangeException(nameof(spec), "Build timeout must be positive.");
        if (spec.SignArtifact && string.IsNullOrWhiteSpace(spec.TimeStampServer))
            throw new ArgumentException("Signing requires an RFC3161 timestamp server URL.", nameof(spec));
        if (spec.SigningTimeoutSeconds < 1)
            throw new ArgumentOutOfRangeException(nameof(spec), "Signing timeout must be positive.");
        if (spec.Kind == PowerShellCompilationArtifactKind.Executable && !spec.TargetFramework.Equals("net8.0", StringComparison.OrdinalIgnoreCase) && !spec.TargetFramework.Equals("net10.0", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Executables currently target net8.0 or net10.0.", nameof(spec));
        PowerShellCompilationBuildSpec.EnsureModeSupported(spec.Kind, spec.Mode);
        if (spec.Kind != PowerShellCompilationArtifactKind.Executable &&
            (spec.SelfContained || !string.IsNullOrWhiteSpace(spec.RuntimeIdentifier)))
            throw new ArgumentException("SelfContained and RuntimeIdentifier are executable-only publication options.", nameof(spec));
        if (spec.Optimization != PowerShellCompilationExecutableOptimization.None)
        {
            if (spec.Kind != PowerShellCompilationArtifactKind.Executable || spec.Mode != PowerShellCompilationMode.Strict)
                throw new ArgumentException("Executable optimization is supported only for Strict genuinely typed executables.", nameof(spec));
            if (!spec.SelfContained || string.IsNullOrWhiteSpace(spec.RuntimeIdentifier) || !spec.SingleFile)
                throw new ArgumentException("Trimmed and NativeAot executables require SelfContained, RuntimeIdentifier, and SingleFile.", nameof(spec));
        }
        if (spec.Kind != PowerShellCompilationArtifactKind.Executable &&
            !spec.TargetFramework.Equals("net472", StringComparison.OrdinalIgnoreCase) &&
            !spec.TargetFramework.Equals("net8.0", StringComparison.OrdinalIgnoreCase) &&
            !spec.TargetFramework.Equals("net10.0", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Typed libraries and binary modules currently target net472, net8.0, or net10.0.", nameof(spec));
    }

    private static string[] ResolveCompilationSourcePaths(PowerShellCompilationBuildSpec spec)
    {
        var sourcePath = Path.GetFullPath(spec.SourcePath);
        var sourceRoot = Path.GetDirectoryName(sourcePath) ?? Directory.GetCurrentDirectory();
        var paths = new[] { sourcePath }
            .Concat(spec.CompilationSourcePaths ?? Array.Empty<string>())
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path.Trim().Trim('"')))
            .Distinct(PowerShellCompilationPathSafety.PathComparer)
            .ToArray();
        foreach (var path in paths)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("PowerShell compilation source file was not found.", path);
            var extension = Path.GetExtension(path);
            if (!extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".psm1", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"PowerShell compilation source '{path}' must be a .ps1 or .psm1 file.", nameof(spec));
            if (!PowerShellCompilationPathSafety.PathEquals(path, sourcePath))
                PowerShellCompilationPathSafety.EnsureContained(sourceRoot, path, $"Additional compilation source '{path}' escapes the root module directory.");
            PowerShellCompilationPathSafety.EnsureNoLinks(sourceRoot, path, $"Compilation source '{path}' traverses a symbolic link or junction.");
        }
        return paths;
    }

    private static PowerShellCompilationPlan AnalyzeCompilationSources(
        IEnumerable<string> sourcePaths,
        PowerShellCompilationMode mode,
        string targetFramework,
        PowerShellCompilationCapability capabilities)
    {
        var analyzer = new PowerShellCompilationAnalyzer();
        var paths = sourcePaths.Select(Path.GetFullPath).Distinct(PowerShellCompilationPathSafety.PathComparer).ToArray();
        var basePath = paths.Length == 0
            ? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(paths[0]) ?? Directory.GetCurrentDirectory();
        return analyzer.AnalyzeFiles(mode, paths, basePath, targetFramework, capabilities);
    }

    private static void ValidateRuntimeHookSourceOwnership(
        PowerShellCompilationBuildSpec spec,
        IEnumerable<string> compilationSourcePaths)
    {
        if (spec.Kind != PowerShellCompilationArtifactKind.BinaryModule)
            return;
        var sourceRoot = Path.GetDirectoryName(Path.GetFullPath(spec.SourcePath)) ?? Directory.GetCurrentDirectory();
        var compilationSources = compilationSourcePaths.Select(Path.GetFullPath).ToHashSet(PowerShellCompilationPathSafety.PathComparer);
        var overlap = PowerShellCompiledModuleManifest.GetContainedRuntimeScriptFiles(spec.SourcePath, spec.ModuleManifestPath)
            .Select(reference => Path.GetFullPath(Path.Combine(
                sourceRoot,
                PowerShellCompiledModuleManifest.NormalizeManifestRelativePath(reference))))
            .FirstOrDefault(compilationSources.Contains);
        if (overlap is not null)
        {
            throw new InvalidOperationException(
                $"PowerShell source '{overlap}' cannot be both an explicit compilation source and a manifest runtime hook. Remove it from CompilationSourcePaths so its runtime scope and loading semantics are preserved.");
        }
    }

    private static void ValidateRuntimeSourcePaths(
        PowerShellCompilationBuildSpec spec,
        IEnumerable<string> compilationSourcePaths)
    {
        if (spec.RuntimeSourcePaths is not { Length: > 0 }) return;
        var sourceRoot = Path.GetDirectoryName(Path.GetFullPath(spec.SourcePath)) ?? Directory.GetCurrentDirectory();
        var compiled = compilationSourcePaths.Select(Path.GetFullPath).ToHashSet(PowerShellCompilationPathSafety.PathComparer);
        foreach (var runtimeSource in spec.RuntimeSourcePaths
                     .Where(static path => !string.IsNullOrWhiteSpace(path))
                     .Select(path => Path.GetFullPath(path.Trim().Trim('"')))
                     .Distinct(PowerShellCompilationPathSafety.PathComparer))
        {
            if (!File.Exists(runtimeSource))
                throw new FileNotFoundException("PowerShell runtime source file was not found.", runtimeSource);
            var extension = Path.GetExtension(runtimeSource);
            if (!extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".psm1", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"PowerShell runtime source '{runtimeSource}' must be a .ps1 or .psm1 file.", nameof(spec));
            if (!compiled.Contains(runtimeSource))
                PowerShellCompilationPathSafety.EnsureContained(sourceRoot, runtimeSource, $"Runtime source '{runtimeSource}' escapes the root module directory.");
            PowerShellCompilationPathSafety.EnsureNoLinks(sourceRoot, runtimeSource, $"Runtime source '{runtimeSource}' traverses a symbolic link or junction.");
        }
    }

    internal static bool ShouldEnablePublishSingleFile(PowerShellCompilationBuildSpec spec)
        => spec.SingleFile && spec.Optimization != PowerShellCompilationExecutableOptimization.NativeAot;

    private static GeneratedBuildProcessResult RunDotNetBuild(
        PowerShellCompilationBuildSpec spec,
        string projectPath,
        string publishDirectory,
        string? runtimeIdentifier)
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

    private static string GetPowerShellSdkVersion(string targetFramework)
        => targetFramework.Equals("net10.0", StringComparison.OrdinalIgnoreCase) ? "7.6.4" : "7.4.18";

    private static string GetSecurityXmlVersion(string targetFramework)
        => targetFramework.Equals("net10.0", StringComparison.OrdinalIgnoreCase) ? "10.0.11" : "8.0.4";

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
