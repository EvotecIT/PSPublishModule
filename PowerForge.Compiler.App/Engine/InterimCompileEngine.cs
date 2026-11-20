using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PowerForge.Compiler.App.Engine;

/// <summary>
/// Maps today's compiler services onto <see cref="ICompileEngine"/>. When the engine gains the host service
/// described in Docs/PowerForge.PowerShellCompilation.HostApi.md, this class shrinks to a direct call.
/// </summary>
internal sealed partial class InterimCompileEngine : ICompileEngine
{
    // The engine's default (PowerShellCompilationTargetFrameworkPolicy.Default) is internal; executables accept only net10.0.
    private const string TargetFramework = "net10.0";

    private static readonly PowerShellCompilationMode[] CheckedModes =
    {
        PowerShellCompilationMode.Package,
        PowerShellCompilationMode.Hybrid,
        PowerShellCompilationMode.Strict
    };

    public Task<CheckResult> CheckAsync(string inputPath, FileRules files, CancellationToken cancellationToken) =>
        Task.Run(() => Check(inputPath, files, cancellationToken), cancellationToken);

    public Task<CompileResult> CompileAsync(CompileRequest request, IProgress<CompileProgress>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => Compile(request, progress, cancellationToken), cancellationToken);

    public IReadOnlyList<CompileTarget> GetTargets(PowerShellCompilationMode mode, PowerShellCompilationArtifactKind kind)
    {
        var hostRuntime = RuntimeInformation.RuntimeIdentifier;
        var targets = PowerShellCompilationSupportMatrixService.Create().Profiles
            .Where(profile => profile.Mode == mode && profile.ArtifactKind == kind)
            .Select(static profile => new CompileTarget(
                profile.Id,
                profile.Mode,
                profile.RuntimeIdentifier,
                profile.Deployment,
                profile.SupportLevel,
                profile.Advertised))
            .OrderByDescending(static target => target.Advertised)
            .ThenByDescending(target => target.RuntimeIdentifier.Equals(hostRuntime, StringComparison.OrdinalIgnoreCase))
            .ThenBy(static target => target.RuntimeIdentifier, StringComparer.Ordinal)
            .ThenBy(static target => target.Deployment)
            .ToList();
        if (targets.Count == 0)
        {
            // Modes without qualified profiles still build for the current machine, framework-dependent.
            targets.Add(new CompileTarget($"host-{mode}", mode, hostRuntime, PowerShellCompilationDeploymentModel.FrameworkDependent, "this machine", Advertised: true));
        }
        return targets;
    }

    public IReadOnlyList<SigningCertificate> GetSigningCertificates()
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<SigningCertificate>();
        const string CodeSigningOid = "1.3.6.1.5.5.7.3.3";
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        var now = DateTime.Now;
        return store.Certificates
            .Where(certificate => certificate.NotAfter > now && certificate.NotBefore <= now && certificate.HasPrivateKey)
            .Where(static certificate => certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>()
                .Any(static usage => usage.EnhancedKeyUsages.Cast<Oid>().Any(static oid => oid.Value == CodeSigningOid)))
            .OrderByDescending(static certificate => certificate.NotAfter)
            .Select(static certificate => new SigningCertificate(
                certificate.Thumbprint,
                certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false),
                certificate.NotAfter))
            .ToArray();
    }

    private static CheckResult Check(string inputPath, FileRules files, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        if (!TryGetExistingPath(inputPath, out var fullPath, out var pathProblem))
            return InputProblem(inputPath, pathProblem, stopwatch.Elapsed);

        PowerShellCompilationResolvedInput resolved;
        try
        {
            resolved = new PowerShellCompilationInputResolver().Resolve(fullPath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return InputProblem(fullPath, ex.Message, stopwatch.Elapsed);
        }

        var modes = new List<ModeCheck>(CheckedModes.Length);
        foreach (var mode in CheckedModes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            modes.Add(CheckMode(fullPath, resolved.Kind, mode, files));
        }

        return new CheckResult(
            fullPath,
            resolved.Kind,
            resolved.SourceFiles,
            modes,
            Recommend(modes),
            Array.Empty<CompileDiagnostic>(),
            stopwatch.Elapsed);
    }

    private static ModeCheck CheckMode(string fullPath, PowerShellCompilationArtifactKind kind, PowerShellCompilationMode mode, FileRules files)
    {
        try
        {
            var resolved = new PowerShellCompilationInputResolver().Resolve(fullPath, kind, mode, AllowsRuntimeSources(files, mode));
            var plan = new PowerShellCompilationAnalyzer(Array.Empty<PowerShellCompilationCommandProviderContract>(), GetSemanticProfileId()).Analyze(
                resolved,
                mode,
                TargetFramework,
                files.Mode,
                files.Include,
                files.Exclude,
                PowerShellCompilationOutputPolicy.GetDefaultOutputDirectory(resolved),
                null);
            var explanation = PowerShellCompilationExplainShaper.CreateFinalExplanation(resolved, plan, TargetFramework);
            return MapExplanation(mode, explanation, resolved);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The engine refuses some inputs by throwing (for example a Strict result type it cannot represent).
            return new ModeCheck(
                mode,
                CompileOutcome.Blocked,
                0,
                0,
                0,
                new[] { new CompileDiagnostic(CompileSeverity.Error, "Rejected", string.Empty, ex.Message, fullPath, 0, 0) },
                Array.Empty<CompileDependency>());
        }
    }

    internal static ModeCheck MapExplanation(
        PowerShellCompilationMode mode,
        PowerShellCompilationExplanation explanation,
        PowerShellCompilationResolvedInput resolved)
    {
        var root = resolved.ModuleRoot;
        var diagnostics = new List<CompileDiagnostic>();
        foreach (var file in explanation.Files)
        {
            var path = Path.GetFullPath(Path.Combine(root, file.RelativePath));
            foreach (var cause in file.Causes)
                diagnostics.Add(ToDiagnostic(cause, path, explanation.CanProceed ? CompileSeverity.Warning : CompileSeverity.Error));
            foreach (var unit in file.Units)
            {
                var severity = unit.Decision == PowerShellCompilationDecisionKind.Rejected ? CompileSeverity.Error : CompileSeverity.Info;
                foreach (var cause in unit.Causes)
                    diagnostics.Add(ToDiagnostic(cause, path, severity));
            }
        }

        foreach (var cause in explanation.DependencyCauses)
        {
            diagnostics.Add(new CompileDiagnostic(
                explanation.CanProceed ? CompileSeverity.Warning : CompileSeverity.Error,
                cause.Kind.ToString(),
                cause.Discovery.ToString(),
                cause.Message,
                string.IsNullOrEmpty(cause.RelativePath) ? string.Empty : Path.GetFullPath(Path.Combine(root, cause.RelativePath)),
                0,
                0));
        }

        var entryName = Path.GetFileName(resolved.SourcePath);
        var dependencies = explanation.Dependencies
            .Where(dependency => !(dependency.Kind == PowerShellCompilationDependencyKind.PowerShellSource &&
                                   Path.GetFileName(dependency.RelativePath).Equals(entryName, StringComparison.OrdinalIgnoreCase)))
            .Select(static dependency => new CompileDependency(
                string.IsNullOrEmpty(dependency.Name) ? dependency.RelativePath : dependency.Name,
                dependency.Kind.ToString(),
                ToPlacement(dependency.Disposition),
                dependency.RelativePath))
            .ToArray();

        return new ModeCheck(
            mode,
            GetOutcome(mode, explanation.CanProceed, explanation.TypedUnits),
            explanation.TypedUnits,
            explanation.RuntimeFallbackUnits,
            explanation.RejectedUnits,
            diagnostics
                .OrderByDescending(static diagnostic => diagnostic.Severity)
                .ThenBy(static diagnostic => diagnostic.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static diagnostic => diagnostic.Line)
                .ThenBy(static diagnostic => diagnostic.Column)
                .ToArray(),
            dependencies);
    }

    internal static CompileOutcome GetOutcome(PowerShellCompilationMode mode, bool canProceed, int typedUnits)
    {
        if (!canProceed) return CompileOutcome.Blocked;
        return mode switch
        {
            PowerShellCompilationMode.Strict => CompileOutcome.Native,
            PowerShellCompilationMode.Hybrid when typedUnits > 0 => CompileOutcome.Mixed,
            _ => CompileOutcome.Packaged
        };
    }

    internal static PowerShellCompilationMode? Recommend(IReadOnlyList<ModeCheck> modes)
    {
        ModeCheck? Find(PowerShellCompilationMode mode, CompileOutcome outcome) =>
            modes.FirstOrDefault(check => check.Mode == mode && check.Outcome == outcome);

        return (Find(PowerShellCompilationMode.Strict, CompileOutcome.Native)
                ?? Find(PowerShellCompilationMode.Hybrid, CompileOutcome.Mixed)
                ?? Find(PowerShellCompilationMode.Package, CompileOutcome.Packaged)
                ?? Find(PowerShellCompilationMode.Hybrid, CompileOutcome.Packaged))?.Mode;
    }

    private static CompileResult Compile(CompileRequest request, IProgress<CompileProgress>? progress, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        if (!TryGetExistingPath(request.InputPath, out var fullPath, out var pathProblem))
            return Failed(new CompileDiagnostic(CompileSeverity.Error, "InputError", string.Empty, pathProblem, request.InputPath, 0, 0), request.Mode, stopwatch.Elapsed);

        var logger = new ProgressLogger(progress);
        try
        {
            var files = request.Files ?? FileRules.Default;
            var resolved = new PowerShellCompilationInputResolver().Resolve(fullPath, mode: request.Mode, allowDynamicModuleRuntimeSources: AllowsRuntimeSources(files, request.Mode));
            var deployment = request.Target?.Deployment ?? PowerShellCompilationDeploymentModel.FrameworkDependent;
            var spec = new PowerShellCompilationBuildSpec(
                resolved.SourcePath,
                request.OutputDirectory ?? PowerShellCompilationOutputPolicy.GetDefaultOutputDirectory(resolved),
                resolved.ArtifactName,
                resolved.Kind,
                resolved.Mode)
            {
                ModuleManifestPath = resolved.ModuleManifestPath,
                CompilationSourcePaths = resolved.CompilationSourceFiles,
                RuntimeSourcePaths = resolved.SourceFiles,
                ResourceMode = files.Mode,
                IncludeResource = files.Include.ToArray(),
                ExcludeResource = files.Exclude.ToArray(),
                TargetFramework = TargetFramework,
                SemanticProfileId = GetSemanticProfileId(),
                RuntimeIdentifier = request.Target?.RuntimeIdentifier,
                SelfContained = deployment is PowerShellCompilationDeploymentModel.SelfContained or PowerShellCompilationDeploymentModel.Trimmed,
                Optimization = deployment switch
                {
                    PowerShellCompilationDeploymentModel.Trimmed => PowerShellCompilationExecutableOptimization.Trimmed,
                    PowerShellCompilationDeploymentModel.NativeAot => PowerShellCompilationExecutableOptimization.NativeAot,
                    _ => PowerShellCompilationExecutableOptimization.None
                },
                UseBuildCache = true,
                SignArtifact = !string.IsNullOrWhiteSpace(request.SigningThumbprint),
                CertificateThumbprint = request.SigningThumbprint,
                CertificateStoreLocation = CertificateStoreLocation.CurrentUser,
                TimeStampServer = "http://timestamp.digicert.com",
                // A development build until the engine produces a reviewed lock from Check (HostApi.md, slice 3).
                AllowUnreviewedDependencyResolution = true
            };

            var result = new PowerShellCompilationArtifactBuilder().Build(spec, logger, cancellationToken);
            stopwatch.Stop();
            if (result.Succeeded)
                PowerShellCompilationBuildReport.WriteSuccess(logger, result, stopwatch.Elapsed);
            return MapBuildResult(result, request.Mode, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failed(new CompileDiagnostic(CompileSeverity.Error, "BuildError", string.Empty, ex.Message, fullPath, 0, 0), request.Mode, stopwatch.Elapsed);
        }
    }

    internal static CompileResult MapBuildResult(PowerShellCompilationBuildResult result, PowerShellCompilationMode mode, TimeSpan elapsed)
    {
        var manifest = result.Manifest;
        var typed = manifest?.EmittedUnits ?? 0;
        var runtime = manifest is null ? 0 : Math.Max(0, manifest.AnalyzedUnits - manifest.EmittedUnits - manifest.OmittedUnits);
        var size = manifest?.ArtifactSizeBytes ?? 0;
        if (size == 0 && result.ArtifactPath is not null && File.Exists(result.ArtifactPath))
            size = new FileInfo(result.ArtifactPath).Length;

        var diagnostics = new List<CompileDiagnostic>();
        if (!result.Succeeded)
        {
            var failure = result.Failure;
            if (failure is not null && failure.Locations.Length > 0)
            {
                foreach (var location in failure.Locations)
                {
                    diagnostics.Add(new CompileDiagnostic(
                        CompileSeverity.Error,
                        string.IsNullOrEmpty(location.Code) ? failure.Stage.ToString() : location.Code,
                        failure.Reason,
                        string.IsNullOrEmpty(location.Message) ? failure.Summary : location.Message,
                        location.RelativePath,
                        location.Line,
                        location.Column));
                }
            }
            else
            {
                diagnostics.Add(new CompileDiagnostic(
                    CompileSeverity.Error,
                    failure?.Stage.ToString() ?? "BuildError",
                    failure?.Reason ?? string.Empty,
                    failure?.Summary ?? result.Error ?? "The build failed without an error message.",
                    string.Empty,
                    0,
                    0));
            }
        }

        var summary = result.Succeeded ? GetOutcome(mode, canProceed: true, typed) : CompileOutcome.Blocked;
        return new CompileResult(
            result.Succeeded,
            result.ArtifactPath,
            size,
            summary,
            typed,
            runtime,
            diagnostics,
            elapsed,
            manifest?.ContainsEmbeddedPowerShellSource ?? false,
            manifest?.AuthenticodeSigned ?? false);
    }

    private static CompileResult Failed(CompileDiagnostic diagnostic, PowerShellCompilationMode mode, TimeSpan elapsed) =>
        new(false, null, 0, CompileOutcome.Blocked, 0, 0, new[] { diagnostic }, elapsed);

    private static CheckResult InputProblem(string path, string message, TimeSpan elapsed) =>
        new(path, null, Array.Empty<string>(), Array.Empty<ModeCheck>(), null,
            new[] { new CompileDiagnostic(CompileSeverity.Error, "InputError", string.Empty, message, path, 0, 0) },
            elapsed);

    private static bool TryGetExistingPath(string inputPath, out string fullPath, out string problem)
    {
        fullPath = string.Empty;
        problem = string.Empty;
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            problem = "Choose a PowerShell script, module file or module folder.";
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(inputPath.Trim().Trim('"'));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            problem = $"'{inputPath}' is not a valid path: {ex.Message}";
            return false;
        }

        if (File.Exists(fullPath) || Directory.Exists(fullPath)) return true;
        problem = $"'{fullPath}' does not exist.";
        return false;
    }

    // Matches the CLI: a whole-module build carries files PowerShell loads at run time, which Strict cannot represent.
    private static bool AllowsRuntimeSources(FileRules files, PowerShellCompilationMode mode) =>
        files.Mode == PowerShellCompilationResourceMode.CompleteModule && mode != PowerShellCompilationMode.Strict;

    private static string GetSemanticProfileId() =>
        PowerShellCompilationSemanticOracleCatalog.Get(
            PowerShellCompilationTargetContractService.GetDefaultSemanticProfileId(TargetFramework)).ProfileId;

    private static CompileDiagnostic ToDiagnostic(PowerShellCompilationExplanationDiagnostic cause, string path, CompileSeverity severity) =>
        new(severity, cause.Code.ToString(), cause.FeatureId, cause.Message, path, cause.Line, cause.Column);

    private static DependencyPlacement ToPlacement(PowerShellCompilationDependencyDisposition disposition) => disposition switch
    {
        PowerShellCompilationDependencyDisposition.ExternalRequirement => DependencyPlacement.RequiredOnTarget,
        PowerShellCompilationDependencyDisposition.NotIncluded or PowerShellCompilationDependencyDisposition.Missing => DependencyPlacement.Blocked,
        _ => DependencyPlacement.Bundled
    };

    private sealed class ProgressLogger : ILogger
    {
        private readonly IProgress<CompileProgress>? _progress;

        public ProgressLogger(IProgress<CompileProgress>? progress) => _progress = progress;

        public bool IsVerbose => false;

        public void Info(string message) => _progress?.Report(new CompileProgress(message));

        public void Success(string message) => _progress?.Report(new CompileProgress(message));

        public void Warn(string message) => _progress?.Report(new CompileProgress(message, CompileSeverity.Warning));

        public void Error(string message) => _progress?.Report(new CompileProgress(message, CompileSeverity.Error));

        public void Verbose(string message)
        {
        }
    }
}
