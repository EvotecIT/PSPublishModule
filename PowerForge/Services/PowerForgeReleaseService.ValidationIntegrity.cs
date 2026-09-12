namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    // Observe the release inputs, not the entire staging root: probes may create unrelated reports.
    // The checkpoint is also revalidated immediately before remote publication so completed
    // delayed validator writes are detected after the validator process exits.
    private static string[] GetValidationIntegrityPaths(PowerForgeReleaseResult result)
        => result.ReleaseAssetEntries.SelectMany(asset => new[] { asset.Path, asset.StagedPath })
            .Concat(result.ReleaseAssets)
            // Per-tool GitHub publication may regenerate configuration aliases and select
            // native installer or direct-output companions from the original publish result.
            .Concat(result.DotNetToolPlan?.ConfigurationInputPaths ?? Array.Empty<string>())
            .Concat(result.DotNetToolPlan?.GeneratedConfigurationInputPaths ?? Array.Empty<string>())
            .Concat((result.DotNetTools?.Artefacts ?? Array.Empty<DotNetPublishArtefactResult>())
                .SelectMany(artifact => new[] { artifact.ZipPath, artifact.ExePath, artifact.OutputDir }
                    .Concat(artifact.OutputFiles ?? Array.Empty<string>())
                    .Concat(artifact.EvidencePaths ?? Array.Empty<string>())))
            .Concat(GetDeferredModulePublicationIntegrityPaths(result.ModulePlan))
            .Concat(new[] { result.ReleaseManifestPath, result.ReleaseChecksumsPath })
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .Distinct(PathComparer)
            .ToArray();

    private static IEnumerable<string?> GetDeferredModulePublicationIntegrityPaths(
        PowerForgeModuleReleasePlanSummary? plan)
    {
        if (plan is null)
            yield break;

        yield return plan.StagingPath;
        yield return plan.ConfigPath;
        yield return plan.ScriptPath;
        yield return plan.ModulePath;

        if (string.IsNullOrWhiteSpace(plan.ConfigPath) || !File.Exists(plan.ConfigPath))
            yield break;

        var context = new ModulePipelineConfigurationService().Load(plan.ConfigPath!);
        foreach (var configPath in context.PackageConfigurationPaths)
            yield return configPath;

        foreach (var action in (context.Spec.Segments ?? Array.Empty<IConfigurationSegment>())
                     .OfType<ConfigurationActionSegment>()
                     .Where(static action => action.Configuration?.Enabled == true &&
                                             !string.IsNullOrWhiteSpace(action.Configuration.FilePath)))
        {
            yield return PathValueResolver.Resolve(context.ProjectRoot, action.Configuration.FilePath!);
        }

        foreach (var publish in (context.Spec.Segments ?? Array.Empty<IConfigurationSegment>())
                     .OfType<ConfigurationPublishSegment>()
                     .Where(static publish => publish.Configuration?.Enabled == true &&
                                              !string.IsNullOrWhiteSpace(publish.Configuration.ApiKeyFilePath)))
        {
            yield return publish.Configuration.ApiKeyFilePath;
        }
    }

    private static Dictionary<string, string> CaptureValidationIntegrity(string[] roots, CancellationToken token)
    {
        var hashes = new Dictionary<string, string>(PathComparer);
        foreach (var root in roots)
        {
            token.ThrowIfCancellationRequested();
            FileSystemPathSafety.RejectReparsePoints(root, root, "Release validation input");
            if (Directory.Exists(root))
            {
                // Include the root itself so an empty directory cannot silently disappear.
                hashes[root] = "directory";
                foreach (var file in ReleaseValidationService.EnumerateValidationFiles(root, token))
                {
                    hashes[file] = DotNetPublishReleaseArtifactVerifier.ComputeSha256Async(file, token).GetAwaiter().GetResult();
                }
            }
            else
            {
                // A missing alternative (for example ZipPath when only an executable was
                // produced) must not appear during a probe and change publication selection.
                if (!File.Exists(root))
                {
                    hashes[root] = "missing";
                }
                else
                {
                    FileSystemPathSafety.RequireRegularFile(root);
                    hashes[root] = DotNetPublishReleaseArtifactVerifier.ComputeSha256Async(root, token).GetAwaiter().GetResult();
                }
            }
        }
        return hashes;
    }

    private static void ValidateIntegrityUnchanged(string[] roots, Dictionary<string, string> expected, CancellationToken token)
    {
        var actual = CaptureValidationIntegrity(roots, token);
        var changed = expected.Keys.Concat(actual.Keys).Distinct(PathComparer)
            .FirstOrDefault(path => !expected.TryGetValue(path, out var before) ||
                !actual.TryGetValue(path, out var after) || !string.Equals(before, after, StringComparison.Ordinal));
        if (changed is not null)
        {
            throw new InvalidOperationException($"Release validation changed a release input: '{changed}'. Probes must leave release assets unchanged.");
        }
    }

    private static ReleaseValidationIntegrityCheckpoint CaptureValidationIntegrityCheckpoint(
        PowerForgeReleaseResult result,
        CancellationToken token)
    {
        var paths = GetValidationIntegrityPaths(result);
        return new ReleaseValidationIntegrityCheckpoint(paths, CaptureValidationIntegrity(paths, token));
    }

    internal static void ValidateReleaseValidationIntegrity(
        PowerForgeReleaseResult result,
        CancellationToken token)
    {
        var checkpoint = result.ReleaseValidationIntegrity;
        if (checkpoint is null)
            return;

        ValidateIntegrityUnchanged(checkpoint.Paths, checkpoint.Hashes, token);
    }

    private static bool ValidateReleaseValidationIntegrityBeforePublication(
        PowerForgeReleaseRequest request,
        PowerForgeReleaseResult result,
        PowerForgeReleaseProgressPhase phase)
    {
        try
        {
            ValidateReleaseValidationIntegrity(result, request.CancellationToken);
            return true;
        }
        catch (Exception exception) when (!request.CancellationToken.IsCancellationRequested)
        {
            request.Progress?.PhaseFailed(phase, exception.Message);
            result.Success = false;
            result.ErrorMessage = exception.Message;
            return false;
        }
    }
}

internal sealed class ReleaseValidationIntegrityCheckpoint
{
    internal ReleaseValidationIntegrityCheckpoint(string[] paths, Dictionary<string, string> hashes)
    {
        Paths = paths;
        Hashes = hashes;
    }

    internal string[] Paths { get; }

    internal Dictionary<string, string> Hashes { get; }
}
