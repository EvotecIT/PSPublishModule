namespace PowerForge;

public sealed partial class PowerShellCompiler
{
    /// <summary>Resolves source, target, and resource settings without producing an artifact.</summary>
    /// <remarks>The returned specification can be configured with build controls such as caching and signing.</remarks>
    public PowerShellCompilationBuildSpec Prepare(PowerShellCompilationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        cancellationToken.ThrowIfCancellationRequested();
        var (_, spec, _) = Resolve(request);
        cancellationToken.ThrowIfCancellationRequested();
        return spec;
    }

    /// <summary>Builds an artifact using the specification's dependency approval policy.</summary>
    public PowerShellCompilationBuildResult Build(PowerShellCompilationBuildSpec spec,
        ILogger? logger = null, CancellationToken cancellationToken = default,
        IProgress<PowerShellCompilationProgress>? progress = null)
        => new PowerShellCompilationArtifactBuilder().Build(spec, logger, cancellationToken, progress);

    private static (PowerShellCompilationResolvedInput Input, PowerShellCompilationBuildSpec Spec,
        PowerShellCompilationTargetContract Target) Resolve(PowerShellCompilationRequest request)
    {
        var paths = PowerShellCompilationInputResolver.NormalizeRequestedPaths(request.Paths);
        var selected = request.Target is null ? null : PowerShellCompilationTargetContractService.Normalize(request.Target.Copy());
        var kind = selected?.ArtifactKind ?? request.Kind ??
            (!string.IsNullOrWhiteSpace(request.EntryPoint) ? PowerShellCompilationArtifactKind.Executable :
                paths.Length > 1 ? PowerShellCompilationArtifactKind.Library :
                PowerShellCompilationInputResolver.InferDefaultArtifactKind(paths[0]));
        var mode = selected?.Mode ?? request.Mode ?? PowerShellCompilationInputResolver.GetDefaultMode(kind, paths.Length > 1);
        var profileId = string.IsNullOrWhiteSpace(request.SemanticProfileId)
            ? null : PowerShellCompilationSemanticOracleCatalog.Get(request.SemanticProfileId!.Trim()).ProfileId;
        if (selected is not null)
            ValidateSelection(request, selected, profileId);
        profileId = selected?.SemanticProfileId ?? profileId ??
            PowerShellCompilationArtifactBuilder.GetDefaultBuildSemanticProfileId(mode, request.TargetFramework);

        var input = new PowerShellCompilationInputResolver(profileId).ResolveNormalizedPaths(paths, kind, mode,
            request.EntryPoint, request.ResourceMode == PowerShellCompilationResourceMode.CompleteModule &&
                                mode != PowerShellCompilationMode.Strict);
        var spec = new PowerShellCompilationBuildSpec(input.SourcePath,
            request.OutputDirectory ?? PowerShellCompilationOutputPolicy.GetDefaultOutputDirectory(input),
            PowerShellCompilationArtifactName.Normalize(request.ArtifactName ?? input.ArtifactName,
                nameof(request.ArtifactName)), input.Kind, input.Mode)
        {
            ResolvedSourceRoot = input.ModuleRoot,
            ModuleManifestPath = input.ModuleManifestPath,
            TargetFramework = request.TargetFramework ?? PowerShellCompilationTargetFrameworkPolicy.Default,
            RuntimeIdentifier = request.RuntimeIdentifier,
            SelfContained = request.SelfContained ?? false,
            SingleFile = request.SingleFile ?? true,
            Optimization = request.Optimization ?? PowerShellCompilationExecutableOptimization.None,
            SemanticProfileId = profileId,
            TargetContract = selected,
            ResourceMode = request.ResourceMode,
            IncludeResource = request.IncludeResource?.ToArray() ?? Array.Empty<string>(),
            ExcludeResource = request.ExcludeResource?.ToArray() ?? Array.Empty<string>(),
            CompilationSourcePaths = input.CompilationSourceFiles,
            RuntimeSourcePaths = input.SourceFiles
        };
        var target = ResolveTarget(spec);
        PowerShellCompilationArtifactBuilder.ValidateSpec(spec);
        return (input, spec, target);
    }

    private static void ValidateSelection(PowerShellCompilationRequest request, PowerShellCompilationTargetContract target,
        string? profileId)
    {
        var selfContained = target.Deployment is PowerShellCompilationDeploymentModel.SelfContained or
            PowerShellCompilationDeploymentModel.Trimmed or PowerShellCompilationDeploymentModel.NativeAot;
        var optimization = target.Deployment switch
        {
            PowerShellCompilationDeploymentModel.Trimmed => PowerShellCompilationExecutableOptimization.Trimmed,
            PowerShellCompilationDeploymentModel.NativeAot => PowerShellCompilationExecutableOptimization.NativeAot,
            _ => PowerShellCompilationExecutableOptimization.None
        };
        if ((request.Kind is { } kind && kind != target.ArtifactKind) ||
            (request.Mode is { } mode && mode != target.Mode) ||
            (profileId is not null && !string.Equals(profileId, target.SemanticProfileId, StringComparison.Ordinal)) ||
            (request.TargetFramework is { } framework && !string.Equals(framework.Trim(), target.TargetFramework, StringComparison.OrdinalIgnoreCase)) ||
            (request.RuntimeIdentifier is { } rid && !string.Equals(rid.Trim(), target.RuntimeIdentifier, StringComparison.OrdinalIgnoreCase)) ||
            (request.SelfContained is { } contained && contained != selfContained) ||
            (request.SingleFile is { } singleFile && singleFile != target.SingleFile) ||
            (request.Optimization is { } requestedOptimization && requestedOptimization != optimization))
            throw new ArgumentException("The explicit artifact settings conflict with the selected target.", nameof(request));
    }
}
