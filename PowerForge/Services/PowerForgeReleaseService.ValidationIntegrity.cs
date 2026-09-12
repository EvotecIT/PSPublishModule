namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    // Observe the release inputs, not the entire staging root: probes may create unrelated reports.
    // This detects completed probe mutations; it is not a concurrent-mutation sandbox.
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
            .Concat(new[] { result.ReleaseManifestPath, result.ReleaseChecksumsPath, result.ModulePlan?.StagingPath })
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .Distinct(PathComparer)
            .ToArray();

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
}
