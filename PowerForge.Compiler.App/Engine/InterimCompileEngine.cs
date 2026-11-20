using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PowerForge.Compiler.App.Engine;

/// <summary>
/// Maps the shared compiler host service onto the window's models.
/// </summary>
internal sealed partial class InterimCompileEngine : ICompileEngine
{
    // The engine's default (PowerShellCompilationTargetFrameworkPolicy.Default) is internal; executables accept only net10.0.
    private const string TargetFramework = "net10.0";
    private static readonly PowerShellCompilationHostService Host = new();

    private static readonly PowerShellCompilationMode[] CheckedModes =
    {
        PowerShellCompilationMode.Package,
        PowerShellCompilationMode.Hybrid,
        PowerShellCompilationMode.Strict
    };

    public Task<CheckResult> CheckAsync(string inputPath, FileRules files, CancellationToken cancellationToken) =>
        Task.Run(() => Check(inputPath, files, cancellationToken), cancellationToken);

    public Task<CompileReviewResult> ReviewAsync(CompileRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => Review(request, cancellationToken), cancellationToken);

    public Task<CompileResult> CompileAsync(CompileRequest request, PowerShellCompilationReview review,
        IProgress<CompileProgress>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => Compile(request, review, progress, cancellationToken), cancellationToken);

    public IReadOnlyList<CompileTarget> GetTargets(PowerShellCompilationMode mode, PowerShellCompilationArtifactKind kind)
    {
        var hostRuntime = RuntimeInformation.RuntimeIdentifier;
        var targets = PowerShellCompilationSupportMatrixService.Create().Profiles
            .Where(profile => profile.Mode == mode && profile.ArtifactKind == kind)
            .Select(static profile => new CompileTarget(
                profile.Id,
                profile.Mode,
                profile.TargetFramework,
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
            targets.Add(new CompileTarget($"host-{mode}", mode, TargetFramework, hostRuntime, PowerShellCompilationDeploymentModel.FrameworkDependent, "this machine", Advertised: true));
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
            resolved = new PowerShellCompilationInputResolver().Resolve(fullPath,
                allowDynamicModuleRuntimeSources: files.Mode == PowerShellCompilationResourceMode.CompleteModule);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return InputProblem(fullPath, ex.Message, stopwatch.Elapsed);
        }

        var modes = new List<ModeCheck>(CheckedModes.Length);
        foreach (var mode in CheckedModes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            modes.Add(CheckMode(fullPath, resolved, mode, files, cancellationToken));
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

    private static ModeCheck CheckMode(string fullPath, PowerShellCompilationResolvedInput resolved,
        PowerShellCompilationMode mode, FileRules files, CancellationToken cancellationToken)
    {
        var check = Host.Check(new PowerShellCompilationCheckRequest(fullPath)
        {
            Kind = resolved.Kind,
            Mode = mode,
            ResourceMode = files.Mode,
            IncludeResource = files.Include.ToArray(),
            ExcludeResource = files.Exclude.ToArray()
        }, cancellationToken);
        return new ModeCheck(mode,
            GetOutcome(mode, check.CanProceed, check.Explanation?.TypedUnits ?? 0),
            check.Explanation?.TypedUnits ?? 0,
            check.Explanation?.RuntimeFallbackUnits ?? 0,
            check.Explanation?.RejectedUnits ?? 0,
            check.Diagnostics.Select(MapDiagnostic).ToArray(),
            MapDependencies(check.Dependencies, resolved.SourcePath));
    }

    private static CompileReviewResult Review(CompileRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var (resolved, spec) = CreateBuildSpec(request);
            var check = Host.Check(new PowerShellCompilationCheckRequest(request.InputPath)
            {
                Target = spec.TargetContract,
                OutputDirectory = spec.OutputDirectory,
                ResourceMode = spec.ResourceMode,
                IncludeResource = spec.IncludeResource,
                ExcludeResource = spec.ExcludeResource
            }, cancellationToken);
            var doctor = check.CanProceed ? Host.Doctor(check.ProposedReview!.Target, cancellationToken) : null;
            var dependencies = check.ProposedReview?.DependencyLock is { } graph
                ? MapReviewedDependencies(graph)
                : MapDependencies(check.Dependencies, resolved.SourcePath);
            var diagnostics = check.Diagnostics.Concat(doctor?.Diagnostics ?? Array.Empty<PowerShellCompilationHostDiagnostic>())
                .Select(MapDiagnostic).ToArray();
            return new CompileReviewResult(doctor is { Succeeded: true } ? check.ProposedReview : null, dependencies, diagnostics);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new CompileReviewResult(null, Array.Empty<CompileDependency>(),
                new[] { new CompileDiagnostic(CompileSeverity.Error, "InputError", string.Empty, ex.Message, request.InputPath, 0, 0) });
        }
    }

    private static CompileDependency[] MapReviewedDependencies(PowerShellCompilationDependencyGraph graph) =>
        graph.Nodes.Where(node => node.Id != graph.RootNodeId)
            .Select(static node =>
            {
                var identity = node.Identity;
                var version = new List<string>();
                if (!string.IsNullOrEmpty(identity.RequiredVersion)) version.Add($"Required version {identity.RequiredVersion}");
                if (!string.IsNullOrEmpty(identity.MinimumVersion)) version.Add($"Minimum version {identity.MinimumVersion}");
                if (!string.IsNullOrEmpty(identity.MaximumVersion)) version.Add($"Maximum version {identity.MaximumVersion}");
                if (version.Count == 0 && !string.IsNullOrEmpty(identity.Version)) version.Add($"Version {identity.Version}");
                var details = version.Concat(new[] { identity.RuntimeIdentifier, identity.TargetFramework, identity.Guid, identity.Source })
                    .Where(static item => !string.IsNullOrWhiteSpace(item));
                var placement = node.Disposition switch
                {
                    PowerShellCompilationDependencyGraphDisposition.External => DependencyPlacement.RequiredOnTarget,
                    PowerShellCompilationDependencyGraphDisposition.PrivateRestored => DependencyPlacement.Restored,
                    PowerShellCompilationDependencyGraphDisposition.Referenced => DependencyPlacement.Referenced,
                    PowerShellCompilationDependencyGraphDisposition.Hosted => DependencyPlacement.Hosted,
                    PowerShellCompilationDependencyGraphDisposition.Rejected => DependencyPlacement.Blocked,
                    _ => DependencyPlacement.Bundled
                };
                return new CompileDependency(string.IsNullOrEmpty(identity.Name) ? node.Id : identity.Name,
                    node.Kind.ToString(), placement, string.Join(" · ", details));
            }).ToArray();

    private static CompileDependency[] MapDependencies(IEnumerable<PowerShellCompilationDependency> dependencies, string sourcePath)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return dependencies
            .Where(dependency => !(dependency.Kind == PowerShellCompilationDependencyKind.PowerShellSource &&
                                   string.Equals(dependency.SourcePath, sourcePath, comparison)))
            .Select(static dependency => new CompileDependency(
                string.IsNullOrEmpty(dependency.Name) ? dependency.RelativePath : dependency.Name,
                dependency.Kind.ToString(),
                ToPlacement(dependency.Disposition),
                dependency.RelativePath))
            .ToArray();

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

    private static CompileResult Compile(CompileRequest request, PowerShellCompilationReview review,
        IProgress<CompileProgress>? progress, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        if (!TryGetExistingPath(request.InputPath, out var fullPath, out var pathProblem))
            return Failed(new CompileDiagnostic(CompileSeverity.Error, "InputError", string.Empty, pathProblem, request.InputPath, 0, 0), request.Mode, stopwatch.Elapsed);

        var logger = new ProgressLogger(progress);
        try
        {
            var (_, spec) = CreateBuildSpec(request);
            var result = Host.Compile(spec, review, logger, cancellationToken);
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

    private static (PowerShellCompilationResolvedInput Input, PowerShellCompilationBuildSpec Spec) CreateBuildSpec(CompileRequest request)
    {
        var files = request.Files ?? FileRules.Default;
        var resolved = new PowerShellCompilationInputResolver().Resolve(request.InputPath, mode: request.Mode,
            allowDynamicModuleRuntimeSources: AllowsRuntimeSources(files, request.Mode));
        return (resolved, new PowerShellCompilationBuildSpec(resolved.SourcePath,
            request.OutputDirectory ?? PowerShellCompilationOutputPolicy.GetDefaultOutputDirectory(resolved),
            resolved.ArtifactName, resolved.Kind, resolved.Mode)
        {
            ModuleManifestPath = resolved.ModuleManifestPath,
            CompilationSourcePaths = resolved.CompilationSourceFiles,
            RuntimeSourcePaths = resolved.SourceFiles,
            ResourceMode = files.Mode,
            IncludeResource = files.Include.ToArray(),
            ExcludeResource = files.Exclude.ToArray(),
            TargetContract = CreateTarget(resolved.Kind, resolved.Mode, request.Target),
            UseBuildCache = true,
            SignArtifact = !string.IsNullOrWhiteSpace(request.SigningThumbprint),
            CertificateThumbprint = request.SigningThumbprint,
            CertificateStoreLocation = CertificateStoreLocation.CurrentUser,
            TimeStampServer = "http://timestamp.digicert.com"
        });
    }

    private static PowerShellCompilationTargetContract CreateTarget(PowerShellCompilationArtifactKind kind,
        PowerShellCompilationMode mode, CompileTarget? target)
    {
        if (target?.ExactContract is { } saved) return saved;
        var deployment = target?.Deployment ?? PowerShellCompilationDeploymentModel.FrameworkDependent;
        var framework = string.IsNullOrWhiteSpace(target?.TargetFramework) ? TargetFramework : target.TargetFramework;
        var rid = target?.RuntimeIdentifier;
        var optimization = deployment switch
        {
            PowerShellCompilationDeploymentModel.Trimmed => PowerShellCompilationExecutableOptimization.Trimmed,
            PowerShellCompilationDeploymentModel.NativeAot => PowerShellCompilationExecutableOptimization.NativeAot,
            _ => PowerShellCompilationExecutableOptimization.None
        };
        return PowerShellCompilationTargetContractService.Create(kind, mode, framework,
            rid, RequiresSelfContained(deployment),
            singleFile: kind == PowerShellCompilationArtifactKind.Executable && !string.IsNullOrWhiteSpace(rid),
            optimization, explicitContract: true);
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

    internal static bool RequiresSelfContained(PowerShellCompilationDeploymentModel deployment) =>
        deployment is PowerShellCompilationDeploymentModel.SelfContained
            or PowerShellCompilationDeploymentModel.Trimmed
            or PowerShellCompilationDeploymentModel.NativeAot;

    private static CompileDiagnostic MapDiagnostic(PowerShellCompilationHostDiagnostic diagnostic) =>
        new(diagnostic.Severity == PowerShellCompilationHostSeverity.Error ? CompileSeverity.Error : CompileSeverity.Warning,
            diagnostic.Code.ToString(), diagnostic.FeatureId, diagnostic.Message, diagnostic.FilePath,
            diagnostic.Line, diagnostic.Column, diagnostic.Fix);

    private static DependencyPlacement ToPlacement(PowerShellCompilationDependencyDisposition disposition) => disposition switch
    {
        PowerShellCompilationDependencyDisposition.ExternalRequirement => DependencyPlacement.RequiredOnTarget,
        PowerShellCompilationDependencyDisposition.NotIncluded => DependencyPlacement.Excluded,
        PowerShellCompilationDependencyDisposition.Missing => DependencyPlacement.Blocked,
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
