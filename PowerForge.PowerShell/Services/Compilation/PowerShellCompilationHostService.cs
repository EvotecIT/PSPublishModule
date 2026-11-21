namespace PowerForge;

/// <summary>Uses the compiler's existing analysis and build owners for an interactive or automated reviewed build.</summary>
public sealed class PowerShellCompilationHostService
{
    /// <summary>Checks source and dependency eligibility for one exact selection without executing the source.</summary>
    public PowerShellCompilationCheckResult Check(PowerShellCompilationCheckRequest request,
        CancellationToken cancellationToken = default, IProgress<PowerShellCompilationProgress>? progress = null)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            PowerShellCompilationProgress.Report(progress, PowerShellCompilationHostPhase.Resolving);
            cancellationToken.ThrowIfCancellationRequested();
            var selected = request.Target is null ? null : PowerShellCompilationTargetContractService.Normalize(request.Target.Copy());
            if (selected is not null && (request.Kind is { } kind && kind != selected.ArtifactKind ||
                                        request.Mode is { } mode && mode != selected.Mode))
                throw new ArgumentException("The explicit kind or mode conflicts with the selected target.");
            var profileId = selected?.SemanticProfileId ?? request.SemanticProfileId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(profileId))
                profileId = PowerShellCompilationTargetContractService.GetDefaultSemanticProfileId(request.TargetFramework);
            var input = new PowerShellCompilationInputResolver(profileId).Resolve(
                request.Path, selected?.ArtifactKind ?? request.Kind, selected?.Mode ?? request.Mode,
                allowDynamicModuleRuntimeSources: request.ResourceMode == PowerShellCompilationResourceMode.CompleteModule &&
                                                  (selected?.Mode ?? request.Mode) != PowerShellCompilationMode.Strict);
            var spec = new PowerShellCompilationBuildSpec(input.SourcePath,
                request.OutputDirectory ?? PowerShellCompilationOutputPolicy.GetDefaultOutputDirectory(input),
                input.ArtifactName, input.Kind, input.Mode)
            {
                ModuleManifestPath = input.ModuleManifestPath,
                TargetFramework = request.TargetFramework,
                SemanticProfileId = request.SemanticProfileId ?? string.Empty,
                TargetContract = selected,
                ResourceMode = request.ResourceMode,
                IncludeResource = request.IncludeResource,
                ExcludeResource = request.ExcludeResource,
                CompilationSourcePaths = input.CompilationSourceFiles,
                RuntimeSourcePaths = input.SourceFiles
            };
            var target = ResolveTarget(spec);
            PowerShellCompilationArtifactBuilder.ValidateSpec(spec);
            PowerShellCompilationArtifactBuilder.GetValidatedRuntimeManifestHooks(spec);
            PowerShellCompilationOutputPolicy.EnsureDoesNotOverlapRecursiveLoaderRoot(spec.SourcePath, spec.OutputDirectory);
            cancellationToken.ThrowIfCancellationRequested();
            var analyzer = new PowerShellCompilationAnalyzer(Array.Empty<PowerShellCompilationCommandProviderContract>(),
                target.SemanticProfileId);
            PowerShellCompilationProgress.Report(progress, PowerShellCompilationHostPhase.Analyzing);
            var plan = analyzer.Analyze(input, input.Mode, target.TargetFramework,
                spec.ResourceMode, spec.IncludeResource, spec.ExcludeResource, spec.OutputDirectory, target, null, null,
                cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            PowerShellCompilationProgress.Report(progress, PowerShellCompilationHostPhase.Shaping);
            cancellationToken.ThrowIfCancellationRequested();
            var explanation = PowerShellCompilationExplainShaper.CreateFinalExplanation(input, plan, target.TargetFramework);
            cancellationToken.ThrowIfCancellationRequested();
            var lockGraph = plan.DependencyGraph!;
            var diagnostics = GetDiagnostics(input.ModuleRoot, explanation).ToList();
            foreach (var conflict in lockGraph.Conflicts)
                diagnostics.Add(Error("powershell.dependency.conflict", conflict, input.SourcePath, "Resolve the conflicting dependency before checking again."));
            foreach (var cycle in lockGraph.Cycles)
                diagnostics.Add(Error("powershell.dependency.cycle", string.Join(" -> ", cycle), input.SourcePath, "Remove the static dependency cycle before checking again."));
            return new PowerShellCompilationCheckResult
            {
                ModuleSourceDirectory = input.Kind == PowerShellCompilationArtifactKind.BinaryModule ||
                    PowerShellCompilationDependencyPlanner.IsModuleInput(input.SourcePath, input.ModuleManifestPath)
                    ? input.ModuleRoot : null,
                Explanation = explanation,
                Recommendation = new PowerShellCompilationProfileRecommendationService().Create(plan, target),
                Dependencies = plan.Dependencies,
                Diagnostics = diagnostics.OrderBy(static diagnostic => diagnostic.FilePath, StringComparer.Ordinal)
                    .ThenBy(static diagnostic => diagnostic.Line).ThenBy(static diagnostic => diagnostic.Column).ToArray(),
                ProposedReview = explanation.CanProceed && diagnostics.All(static item => item.Severity != PowerShellCompilationHostSeverity.Error)
                    ? new PowerShellCompilationReview { Target = target, DependencyLock = lockGraph }
                    : null
            };
        }
        catch (Exception error) when (IsInputFailure(error))
        {
            return new PowerShellCompilationCheckResult
            {
                Diagnostics = new[] { Error("powershell.check.input", error.Message, request.Path,
                    "Check the source path, mode, and target options.") }
            };
        }
    }

    /// <summary>Builds with the selected target and explicitly confirmed dependency graph.</summary>
    public PowerShellCompilationBuildResult Compile(PowerShellCompilationBuildSpec spec,
        PowerShellCompilationReview review, ILogger? logger = null, CancellationToken cancellationToken = default,
        IProgress<PowerShellCompilationProgress>? progress = null)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        if (review is null) throw new ArgumentNullException(nameof(review));
        cancellationToken.ThrowIfCancellationRequested();
        if (review.SchemaVersion != 1 || review.Target is null || review.DependencyLock is null ||
            spec.AllowUnreviewedDependencyResolution || spec.ExpectedDependencyLock is not null)
            return Refuse(PowerShellCompilationFailureStage.Dependency, "Use one confirmed dependency review from Check.");
        if (spec.ProviderPackages.Length > 0 || spec.CommandProviders.Length > 0 ||
            spec.ExpectedProviderLock is not null || spec.AllowUnreviewedProviderResolution)
            return Refuse(PowerShellCompilationFailureStage.Dependency, "Check does not review custom command providers. Use the provider and dependency lock build options.");
        try
        {
            spec = spec.Copy();
            var target = ResolveTarget(spec);
            var approved = PowerShellCompilationTargetContractService.Normalize(review.Target.Copy());
            if (!string.Equals(target.ContractSha256, approved.ContractSha256, StringComparison.OrdinalIgnoreCase))
                return Refuse(PowerShellCompilationFailureStage.Dependency, "The selected target differs from the reviewed target. Run Check again for this selection.");

            if (spec.TargetContract is not null)
                spec.TargetContract = target;
            spec.ExpectedDependencyLock = review.DependencyLock;
            return new PowerShellCompilationArtifactBuilder().Build(spec, logger, cancellationToken, progress,
                approvedDependencyHash: review.DependencyLock.LockSha256);
        }
        catch (Exception error) when (IsInputFailure(error))
        {
            return Refuse(PowerShellCompilationFailureStage.Input, error.Message);
        }
    }

    /// <summary>Checks the selected SDK and the framework metadata required by a requested target.</summary>
    public PowerShellCompilationDoctorResult Doctor(PowerShellCompilationTargetContract? target = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (target is not null)
            {
                var spec = new PowerShellCompilationBuildSpec("check.ps1", ".", "Check",
                    target.ArtifactKind, target.Mode) { TargetContract = target };
                target = ResolveTarget(spec);
                PowerShellCompilationArtifactBuilder.ValidateTargetOptions(spec);
            }
            var sdk = PowerShellCompilationToolchainFingerprint.DiscoverSelectedSdk(cancellationToken);
            if (target is not null && PowerShellCompilationTargetFrameworkPolicy.IsModern(target.TargetFramework))
                _ = PowerShellCompilationToolchainFingerprint.ResolveRuntimePackVersion(target.TargetFramework, sdk);
            cancellationToken.ThrowIfCancellationRequested();
            return new PowerShellCompilationDoctorResult { DotNetSdkVersion = sdk.Version };
        }
        catch (Exception error) when (IsInputFailure(error))
        {
            return new PowerShellCompilationDoctorResult
            {
                Diagnostics = new[] { Error("powershell.doctor.sdk", error.Message, string.Empty,
                    "Install a .NET SDK that supports the selected target and is allowed by global.json.") }
            };
        }
    }

    private static PowerShellCompilationTargetContract ResolveTarget(PowerShellCompilationBuildSpec spec)
    {
        spec.TargetContract = spec.TargetContract?.Copy();
        PowerShellCompilationArtifactBuilder.ApplyExplicitTargetContract(spec);
        var rid = PowerShellCompilationArtifactBuilder.ResolveRuntimeIdentifier(spec);
        if (!string.IsNullOrWhiteSpace(rid))
            spec.RuntimeIdentifier = rid;
        return PowerShellCompilationTargetContractService.Normalize(
            PowerShellCompilationArtifactBuilder.ResolveTargetContract(spec, rid));
    }

    private static IEnumerable<PowerShellCompilationHostDiagnostic> GetDiagnostics(
        string root, PowerShellCompilationExplanation explanation)
    {
        foreach (var file in explanation.Files)
        {
            var path = Path.Combine(root, file.RelativePath);
            foreach (var cause in file.Causes)
                yield return Describe(cause, path, PowerShellCompilationHostSeverity.Error);
            foreach (var unit in file.Units)
            foreach (var cause in unit.Causes)
                yield return Describe(cause, path, unit.Rejected
                    ? PowerShellCompilationHostSeverity.Error
                    : PowerShellCompilationHostSeverity.Warning);
        }

        foreach (var dependency in explanation.DependencyCauses)
            yield return Error("powershell.dependency.missing", dependency.Message, dependency.RelativePath,
                "Provide the required input and check the build again.");
    }

    private static PowerShellCompilationHostDiagnostic Describe(PowerShellCompilationExplanationDiagnostic cause, string path,
        PowerShellCompilationHostSeverity severity)
        => new()
        {
            Severity = severity, Code = cause.Code, FeatureId = cause.FeatureId, Message = cause.Message,
            FilePath = path, Line = cause.Line, Column = cause.Column,
            Fix = PowerShellCompilationHostDiagnosticCatalog.GetFix(cause.Code)
        };

    private static PowerShellCompilationHostDiagnostic Error(string featureId, string message, string path, string fix)
        => new()
        {
            Severity = PowerShellCompilationHostSeverity.Error, Code = PowerShellCompilationDiagnosticCode.InputError,
            FeatureId = featureId, Message = message, FilePath = path, Fix = fix
        };

    private static bool IsInputFailure(Exception error)
        => error is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException or KeyNotFoundException;

    private static PowerShellCompilationBuildResult Refuse(PowerShellCompilationFailureStage stage, string message)
        => new()
        {
            Error = message,
            Failure = new PowerShellCompilationFailure { Stage = stage, Summary = message, Reason = "ReviewedBuildRejected" }
        };
}
