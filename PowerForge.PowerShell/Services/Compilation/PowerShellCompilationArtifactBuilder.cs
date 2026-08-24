using System.Management.Automation.Language;
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

        Directory.CreateDirectory(spec.OutputDirectory);
        PowerShellCompilationPathSafety.EnsureNoLinksFromFileSystemRoot(
            spec.OutputDirectory,
            $"PowerShell compilation output directory '{spec.OutputDirectory}' must not be a symbolic link or junction.");
        var artifactName = SanitizeArtifactName(spec.ArtifactName);
        // Microsoft.PowerShell.SDK carries deeply nested content files. Keeping the disposable
        // generated project below the durable output directory can exceed MAX_PATH on Windows
        // even when the user's final artifact path is otherwise reasonable.
        var workspace = Path.Combine(Path.GetTempPath(), "PowerForge", "ps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
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
            var capabilities = spec.Kind == PowerShellCompilationArtifactKind.BinaryModule
                ? PowerShellCompilationCapability.PowerShellStreams |
                  PowerShellCompilationCapability.LocalFunctionCalls |
                  PowerShellCompilationCapability.BoundParameters |
                  PowerShellCompilationCapability.PowerShellObjects
                : spec.Kind == PowerShellCompilationArtifactKind.Executable && spec.Mode == PowerShellCompilationMode.Strict
                    ? PowerShellCompilationCapability.LocalFunctionCalls |
                      PowerShellCompilationCapability.BoundParameters
                    : PowerShellCompilationCapability.None;
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
                        PowerShellCSharpMethodEmitter.SanitizeIdentifier(artifactName) + "Methods",
                        spec.TargetFramework)
                    : transpiler.Transpile(
                        compilationSourcePaths,
                        "PowerForge.Compiled",
                        PowerShellCSharpMethodEmitter.SanitizeIdentifier(artifactName) + "Methods",
                        spec.TargetFramework);
                string[]? exportedFunctions = null;
                if (spec.Kind == PowerShellCompilationArtifactKind.BinaryModule)
                {
                    var exportContract = PowerShellModuleExportContract.TryRead(spec.SourcePath);
                    exportedFunctions = exportContract?.SelectFunctions(typed.Methods.Select(static method => method.SourceName));
                    if (spec.Mode == PowerShellCompilationMode.Hybrid)
                        typed = PowerShellHybridFunctionCollisionResolver.RouteNameCollisionsToFallback(typed, spec.TargetFramework);
                    typed = PowerShellBinaryCmdletSourceGenerator.PrepareForBinaryModule(typed, exportedFunctions, spec.TargetFramework);
                }
                if (typed.Methods.Length == 0 &&
                    !(spec.Kind == PowerShellCompilationArtifactKind.BinaryModule && spec.Mode == PowerShellCompilationMode.Hybrid))
                {
                    var firstBlocker = typed.Diagnostics.FirstOrDefault();
                    throw new InvalidOperationException(firstBlocker is null
                        ? "No PowerShell functions were eligible for typed CLR compilation."
                        : $"No PowerShell functions were eligible for typed CLR compilation. First blocker: {firstBlocker.Message}");
                }
                if (spec.Mode == PowerShellCompilationMode.Strict && typed.Diagnostics.Length > 0)
                    throw new InvalidOperationException($"Strict mode rejected {typed.Diagnostics.Length} compilation blocker(s).");
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
                        PowerShellBinaryCmdletSourceGenerator.Generate(typed, exportedFunctions),
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
                        .Replace("{{ARTIFACT_NAME}}", EscapeXml(artifactName)),
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
                var packagedSources = PreparePackagedSources(workspace, spec.SourcePath, compilationSourcePaths);
                var parameterInitializers = GeneratePackagedParameterInitializers(spec.SourcePath);
                File.WriteAllText(
                    Path.Combine(workspace, "Source.ps1"),
                    GeneratePackagedScript(spec.SourcePath, packagedSources.HasDependencies),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                File.WriteAllText(
                    Path.Combine(workspace, "Program.cs"),
                    ReadTemplate(PackagedProgramTemplate)
                        .Replace("{{PARAMETERS}}", parameterInitializers.Parameters)
                        .Replace("{{SWITCH_PARAMETERS}}", parameterInitializers.SwitchParameters)
                        .Replace("{{PARAMETER_ALIASES}}", parameterInitializers.ParameterAliases)
                        .Replace("{{ENTRY_RELATIVE_PATH}}", PowerShellCSharpLiteral.QuoteString(packagedSources.EntryRelativePath))
                        .Replace("{{DEPENDENCY_SPECS}}", packagedSources.DependencySpecs),
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
        finally
        {
            if (!spec.KeepBuildWorkspace)
            {
                try { Directory.Delete(workspace, recursive: true); } catch { }
            }
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
        if (!Enum.IsDefined(typeof(CertificateStoreLocation), spec.CertificateStoreLocation))
            throw new ArgumentOutOfRangeException(nameof(spec), "Certificate store location is not defined.");
        if (spec.Mode == PowerShellCompilationMode.Analyze)
            throw new ArgumentException("Analyze mode reports eligibility and does not produce artifacts. Use the analyzer API or CLI analyze command.", nameof(spec));
        if (!File.Exists(spec.SourcePath))
            throw new FileNotFoundException("PowerShell source file was not found.", spec.SourcePath);
        var extension = Path.GetExtension(spec.SourcePath);
        if (!extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".psm1", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("PowerShell artifacts accept .ps1 and .psm1 source files.", nameof(spec));
        if (!string.IsNullOrWhiteSpace(spec.ModuleManifestPath))
        {
            var moduleManifestPath = spec.ModuleManifestPath!;
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
        if (spec.Kind == PowerShellCompilationArtifactKind.Executable && spec.Mode == PowerShellCompilationMode.Hybrid)
            throw new ArgumentException("Hybrid executable compilation is not supported. Use Package for broad PowerShell compatibility or Strict for a genuinely typed executable.", nameof(spec));
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
        if (spec.Kind != PowerShellCompilationArtifactKind.BinaryModule || string.IsNullOrWhiteSpace(spec.ModuleManifestPath))
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

    private static CopiedArtifact CopyArtifact(
        PowerShellCompilationBuildSpec spec,
        string artifactName,
        string publishDirectory,
        PowerShellTypedCompilationResult? typed,
        bool usesPowerShellRuntimeFallback,
        string outputDirectory)
    {
        if (spec.Kind is PowerShellCompilationArtifactKind.Library or PowerShellCompilationArtifactKind.BinaryModule)
        {
            var source = Path.Combine(publishDirectory, artifactName + ".dll");
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

    private static CopiedArtifact CopyHybridModule(
        PowerShellCompilationBuildSpec spec,
        string artifactName,
        string compiledAssembly,
        PowerShellTypedCompilationResult typed,
        string outputDirectory)
    {
        var moduleDirectory = Path.Combine(outputDirectory, artifactName);
        Directory.CreateDirectory(moduleDirectory);
        var assemblyPath = Path.Combine(moduleDirectory, artifactName + ".dll");
        var modulePath = Path.Combine(moduleDirectory, artifactName + ".psm1");
        File.Copy(compiledAssembly, assemblyPath, overwrite: true);
        var files = new List<PowerShellCompilationArtifactFile>();
        File.WriteAllText(
            modulePath,
            PowerShellHybridModuleComposer.ComposeRoot(
                spec.SourcePath,
                Path.GetFileName(assemblyPath),
                typed,
                manifestControlsExports: !string.IsNullOrWhiteSpace(spec.ModuleManifestPath) || HasSiblingModuleManifest(spec.SourcePath)),
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
            typed,
            spec.TargetFramework);
        if (manifestFiles is not null)
        {
            var primaryManifest = manifestFiles.First(path => path.EndsWith(artifactName + ".psd1", StringComparison.OrdinalIgnoreCase));
            foreach (var manifestFile in manifestFiles)
                files.Add(CreateArtifactFile(manifestFile, PowerShellCompilationPathSafety.PathEquals(manifestFile, primaryManifest) ? "PrimaryModuleManifest" : "ModuleDependency"));
        }
        var sourceRoot = Path.GetDirectoryName(Path.GetFullPath(spec.SourcePath)) ?? Directory.GetCurrentDirectory();
        var conventionalDiscovery = PowerShellConventionalModuleSourceDiscovery.Analyze(spec.SourcePath);
        var runtimeHooks = PowerShellCompiledModuleManifest.GetContainedRuntimeScriptFiles(spec.SourcePath, spec.ModuleManifestPath)
            .Select(reference => Path.GetFullPath(Path.Combine(
                sourceRoot,
                PowerShellCompiledModuleManifest.NormalizeManifestRelativePath(reference))))
            .ToArray();
        var wrappedCompiledMethods = PowerShellHybridModuleComposer.GetWrappedCompiledMethodKeys(spec.SourcePath, typed);
        foreach (var dependency in PowerShellHybridDependencyResolver.CopyDependencies(
                     spec.SourcePath,
                     moduleDirectory,
                     runtimeHooks,
                     path => PowerShellHybridModuleComposer.ComposeDependency(path, typed, wrappedCompiledMethods),
                     typed.SourcePaths.Where(path => !PowerShellCompilationPathSafety.PathEquals(path, spec.SourcePath)),
                     conventionalLoaders: conventionalDiscovery.Loaders))
            files.Add(CreateArtifactFile(dependency, "ModuleDependency"));
        var primaryPath = manifestFiles?.First(path => path.EndsWith(".psd1", StringComparison.OrdinalIgnoreCase)) ?? modulePath;
        return new CopiedArtifact(primaryPath, files.ToArray());
    }

    private static CopiedArtifact CopyStrictModuleWithManifest(
        PowerShellCompilationBuildSpec spec,
        string artifactName,
        string compiledAssembly,
        PowerShellTypedCompilationResult typed,
        string outputDirectory)
    {
        var moduleDirectory = Path.Combine(outputDirectory, artifactName);
        Directory.CreateDirectory(moduleDirectory);
        var assemblyPath = Path.Combine(moduleDirectory, artifactName + ".dll");
        File.Copy(compiledAssembly, assemblyPath, overwrite: true);
        var files = new List<PowerShellCompilationArtifactFile> { CreateArtifactFile(assemblyPath, "TypedAssembly") };
        CopyDebugSymbolsIfPresent(compiledAssembly, assemblyPath, files);
        var manifestFiles = PowerShellCompiledModuleManifest.Create(
            spec.SourcePath,
            spec.ModuleManifestPath,
            moduleDirectory,
            artifactName,
            Path.GetFileName(assemblyPath),
            typed,
            spec.TargetFramework) ?? throw new InvalidOperationException("The sibling module manifest was not available during artifact publication.");
        var primaryManifest = manifestFiles.First(path => path.EndsWith(artifactName + ".psd1", StringComparison.OrdinalIgnoreCase));
        foreach (var manifestFile in manifestFiles)
            files.Add(CreateArtifactFile(manifestFile, PowerShellCompilationPathSafety.PathEquals(manifestFile, primaryManifest) ? "PrimaryModuleManifest" : "ModuleDependency"));
        var manifestPath = primaryManifest;
        return new CopiedArtifact(manifestPath, files.ToArray());
    }

    private static string GeneratePackagedScript(string sourcePath, bool hasDependencies)
        => PowerShellPackagedScriptRewriter.Rewrite(
            sourcePath,
            allowDotSource: hasDependencies,
            dependencyCommandPathExpression: hasDependencies
                ? "[PowerForge.Compiled.PowerForgePackagedEntryPoint]::Path"
                : null);

    private static PackagedSourceSet PreparePackagedSources(
        string workspace,
        string sourcePath,
        IEnumerable<string> compilationSourcePaths)
    {
        var fullSourcePath = Path.GetFullPath(sourcePath);
        var sourceRoot = Path.GetDirectoryName(fullSourcePath) ?? Directory.GetCurrentDirectory();
        var dependencies = compilationSourcePaths
            .Select(Path.GetFullPath)
            .Where(path => !PowerShellCompilationPathSafety.PathEquals(path, fullSourcePath))
            .Distinct(PowerShellCompilationPathSafety.PathComparer)
            .OrderBy(path => FrameworkCompatibility.GetRelativePath(sourceRoot, path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (dependencies.Length == 0)
            return new PackagedSourceSet(Path.GetFileName(fullSourcePath), string.Empty, string.Empty, hasDependencies: false);

        var dependencyDirectory = Path.Combine(workspace, "EmbeddedDependencies");
        Directory.CreateDirectory(dependencyDirectory);
        var projectResources = new List<string>();
        var dependencySpecs = new List<string>();
        for (var index = 0; index < dependencies.Length; index++)
        {
            var dependency = dependencies[index];
            PowerShellCompilationPathSafety.EnsureContained(sourceRoot, dependency, $"Packaged dependency '{dependency}' escapes the executable entrypoint root.");
            var fileName = $"Dependency{index:D4}.ps1";
            File.Copy(dependency, Path.Combine(dependencyDirectory, fileName), overwrite: false);
            var logicalName = $"PowerForge.Compiled.{Path.GetFileNameWithoutExtension(fileName)}.ps1";
            var relativePath = FrameworkCompatibility.GetRelativePath(sourceRoot, dependency).Replace('\\', '/');
            projectResources.Add($"    <EmbeddedResource Include=\"EmbeddedDependencies/{fileName}\" LogicalName=\"{EscapeXml(logicalName)}\" />");
            dependencySpecs.Add($"        new EmbeddedDependency({PowerShellCSharpLiteral.QuoteString(logicalName)}, {PowerShellCSharpLiteral.QuoteString(relativePath)}),");
        }
        return new PackagedSourceSet(
            Path.GetFileName(fullSourcePath),
            string.Join(Environment.NewLine, projectResources),
            string.Join(Environment.NewLine, dependencySpecs),
            hasDependencies: true);
    }

    private static (string Parameters, string SwitchParameters, string ParameterAliases) GeneratePackagedParameterInitializers(string sourcePath)
    {
        Token[] tokens;
        ParseError[] errors;
        var ast = Parser.ParseFile(sourcePath, out tokens, out errors);
        if (errors.Length > 0)
            throw new InvalidOperationException("Packaged script parameters could not be parsed for native argument binding.");
        var parameters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var switchParameters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parameterAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in ast.ParamBlock?.Parameters.AsEnumerable() ?? Enumerable.Empty<ParameterAst>())
        {
            var name = parameter.Name.VariablePath.UserPath;
            parameters.Add(name);
            foreach (var alias in parameter.Attributes.OfType<AttributeAst>().Where(static attribute => IsAttributeNamed(attribute, "Alias")))
            foreach (var value in alias.PositionalArguments.OfType<StringConstantExpressionAst>())
                parameterAliases[value.Value] = name;
            if (parameter.StaticType == typeof(System.Management.Automation.SwitchParameter))
                switchParameters.Add(name);
        }
        if (ast.ParamBlock?.Attributes.OfType<AttributeAst>().Any(static attribute => IsAttributeNamed(attribute, "CmdletBinding")) == true)
        {
            var commonSwitches = new[] { "Verbose", "Debug", "WhatIf", "Confirm" };
            parameters.UnionWith(commonSwitches);
            switchParameters.UnionWith(commonSwitches);
            parameterAliases["vb"] = "Verbose";
            parameterAliases["db"] = "Debug";
            parameterAliases["wi"] = "WhatIf";
            parameterAliases["cf"] = "Confirm";
        }
        return (GenerateInitializer(parameters), GenerateInitializer(switchParameters), GenerateAliasInitializer(parameterAliases));
    }

    private static string GenerateInitializer(IEnumerable<string> values)
        => string.Join(", ", values
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .Select(PowerShellCSharpLiteral.QuoteString)
            .ToArray());

    private static string GenerateAliasInitializer(IEnumerable<KeyValuePair<string, string>> aliases)
        => string.Join(", ", aliases
            .OrderBy(static alias => alias.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static alias => $"[{PowerShellCSharpLiteral.QuoteString(alias.Key)}] = {PowerShellCSharpLiteral.QuoteString(alias.Value)}")
            .ToArray());

    private static bool IsAttributeNamed(AttributeAst attribute, string name)
    {
        var fullName = attribute.TypeName.FullName;
        return fullName.Equals(name, StringComparison.OrdinalIgnoreCase) ||
               fullName.Equals(name + "Attribute", StringComparison.OrdinalIgnoreCase) ||
               fullName.EndsWith("." + name + "Attribute", StringComparison.OrdinalIgnoreCase);
    }

    private static string GenerateHybridModuleScript(
        string sourcePath,
        string assemblyFileName,
        PowerShellTypedCompilationResult typed)
    {
        Token[] tokens;
        ParseError[] errors;
        var ast = Parser.ParseFile(sourcePath, out tokens, out errors);
        if (errors.Length > 0)
            throw new InvalidOperationException("Hybrid module source could not be parsed while composing fallback code.");

        var compiled = new HashSet<string>(
            typed.Methods.Select(static method => method.SourceName + "\0" + method.SourceLine),
            StringComparer.OrdinalIgnoreCase);
        var prologueEndOffset = ast.ParamBlock?.Extent.EndOffset ?? 0;
        foreach (var usingStatement in ast.FindAll(static node => node is UsingStatementAst, searchNestedScriptBlocks: false).Cast<UsingStatementAst>())
            prologueEndOffset = Math.Max(prologueEndOffset, usingStatement.Extent.EndOffset);
        var functions = ast.FindAll(static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false)
            .Cast<FunctionDefinitionAst>()
            .OrderByDescending(static function => function.Extent.StartOffset)
            .ToArray();
        var source = new StringBuilder(File.ReadAllText(sourcePath));
        var exportContract = PowerShellModuleExportContract.TryRead(ast);
        var wrappedFunctionNames = exportContract?.SelectFunctions(typed.Methods.Select(static method => method.SourceName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var wrapped = wrappedFunctionNames is null
            ? compiled
            : new HashSet<string>(
                typed.Methods
                    .Where(method => wrappedFunctionNames.Contains(method.SourceName))
                    .Select(static method => method.SourceName + "\0" + method.SourceLine),
                StringComparer.OrdinalIgnoreCase);
        var removals = new List<(int Start, int Length)>();
        foreach (var function in functions)
        {
            if (!wrapped.Contains(function.Name + "\0" + function.Extent.StartLineNumber))
                continue;
            if (function.Extent.StartOffset < prologueEndOffset)
                throw new InvalidOperationException($"Compiled function '{function.Name}' overlaps the module prologue and cannot be composed safely.");
            removals.Add((function.Extent.StartOffset, function.Extent.EndOffset - function.Extent.StartOffset));
        }
        if (exportContract is not null)
            removals.AddRange(exportContract.Commands.Select(static command =>
                (command.Extent.StartOffset, command.Extent.EndOffset - command.Extent.StartOffset)));
        foreach (var removal in removals.OrderByDescending(static removal => removal.Start))
            source.Remove(removal.Start, removal.Length);

        var fallbackFunctions = functions
            .Where(function => !wrapped.Contains(function.Name + "\0" + function.Extent.StartLineNumber))
            .OrderBy(static function => function.Extent.StartOffset)
            .Select(static function => function.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var compiledCmdlets = typed.Methods
            .Select(static method => method.SourceName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var exportedFallbackFunctions = (exportContract?.SelectFunctions(fallbackFunctions) ?? fallbackFunctions)
            .Concat(PowerShellCompiledModuleManifest.GetNestedModuleFunctionExportPatterns(sourcePath, functions.Select(static function => function.Name)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var exportedCompiledCmdlets = exportContract?.SelectFunctions(compiledCmdlets) ?? compiledCmdlets;
        var additionalCmdlets = exportContract?.Cmdlets ?? Array.Empty<string>();
        var aliases = exportContract?.Aliases ?? new[] { "*" };
        var variables = exportContract?.Variables ?? Array.Empty<string>();
        var import = new StringBuilder();
        if (prologueEndOffset > 0 && source[prologueEndOffset - 1] is not '\r' and not '\n') import.AppendLine();
        import.AppendLine("# Generated by PowerForge hybrid PowerShell compilation.");
        import.AppendLine("Import-Module -Name (Join-Path -Path $PSScriptRoot -ChildPath '" + EscapePowerShellSingleQuotedString(assemblyFileName) + "') -Force -ErrorAction Stop");
        import.AppendLine();
        source.Insert(prologueEndOffset, import.ToString());
        var builder = new StringBuilder(source.ToString());
        if (source.Length > 0 && source[source.Length - 1] != '\n') builder.AppendLine();
        if (exportContract is not null && exportContract.Commands.Length == 0)
            return builder.ToString();
        builder.AppendLine();
        builder.Append("Export-ModuleMember -Function @(").Append(JoinPowerShellNames(exportedFallbackFunctions))
            .Append(") -Cmdlet @(").Append(JoinPowerShellNames(exportedCompiledCmdlets.Concat(additionalCmdlets).Distinct(StringComparer.OrdinalIgnoreCase)))
            .Append(") -Alias @(").Append(JoinPowerShellNames(aliases)).Append(')');
        if (variables.Length > 0)
            builder.Append(" -Variable @(").Append(JoinPowerShellNames(variables)).Append(')');
        builder.AppendLine();
        return builder.ToString();
    }

    private static bool HasSiblingModuleManifest(string sourcePath)
        => Path.GetExtension(sourcePath).Equals(".psm1", StringComparison.OrdinalIgnoreCase) &&
           File.Exists(Path.ChangeExtension(sourcePath, ".psd1"));

    private static string JoinPowerShellNames(IEnumerable<string> names)
        => string.Join(", ", names.Select(name => "'" + EscapePowerShellSingleQuotedString(name) + "'"));

    private static string EscapePowerShellSingleQuotedString(string value)
        => value.Replace("'", "''");

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

    private static string BoundOutput(string output)
        => output.Length <= MaximumBuildOutputLength ? output : output.Substring(output.Length - MaximumBuildOutputLength);

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
        internal PackagedSourceSet(string entryRelativePath, string projectResources, string dependencySpecs, bool hasDependencies)
        {
            EntryRelativePath = entryRelativePath;
            ProjectResources = projectResources;
            DependencySpecs = dependencySpecs;
            HasDependencies = hasDependencies;
        }

        internal string EntryRelativePath { get; }
        internal string ProjectResources { get; }
        internal string DependencySpecs { get; }
        internal bool HasDependencies { get; }
    }

}
