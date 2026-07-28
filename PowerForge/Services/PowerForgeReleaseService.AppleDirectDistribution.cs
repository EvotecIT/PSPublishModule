namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private AppleNotarizationResult NotarizeDirectAppleExport(
        PowerForgeAppleReleasePlan plan,
        PowerForgeAppleAppReleaseTargetPlan app,
        string? artifactPath = null,
        string? acceptedSubmissionId = null,
        string? expectedArtifactSha256 = null)
    {
        var result = _notarizeAppleArtifact(new AppleNotarizationRequest
        {
            ArtifactPath = artifactPath ?? ResolveDirectAppleArtifactPath(app.ExportPath),
            XcrunExecutable = plan.DirectDistribution.XcrunExecutable,
            DittoExecutable = plan.DirectDistribution.DittoExecutable,
            SpctlExecutable = plan.DirectDistribution.SpctlExecutable,
            KeychainProfile = plan.DirectDistribution.KeychainProfile,
            ApiKeyPath = plan.AppStoreConnectApiKeyPath,
            ApiKeyId = plan.AppStoreConnectApiKeyId,
            ApiIssuerId = plan.AppStoreConnectApiIssuerId,
            AcceptedSubmissionId = acceptedSubmissionId,
            ExpectedArtifactSha256 = expectedArtifactSha256,
            Timeout = TimeSpan.FromSeconds(plan.DirectDistribution.TimeoutSeconds),
            Staple = plan.DirectDistribution.Staple,
            Assess = plan.DirectDistribution.Assess
        });
        return result;
    }

    private static string ResolveDirectAppleArtifactPath(string exportPath)
    {
        if (!Directory.Exists(exportPath))
            throw new DirectoryNotFoundException($"Developer ID export path was not found: {exportPath}");

        var artifacts = Directory.EnumerateFileSystemEntries(exportPath)
            .Where(path =>
                (Directory.Exists(path) && Path.GetExtension(path).Equals(".app", StringComparison.OrdinalIgnoreCase)) ||
                (File.Exists(path) &&
                 (Path.GetExtension(path).Equals(".dmg", StringComparison.OrdinalIgnoreCase) ||
                  Path.GetExtension(path).Equals(".pkg", StringComparison.OrdinalIgnoreCase))))
            .ToArray();
        if (artifacts.Length != 1)
        {
            throw new InvalidOperationException(
                $"Developer ID export '{exportPath}' must contain exactly one .app, .dmg, or signed flat .pkg artifact; found {artifacts.Length}.");
        }

        return Path.GetFullPath(artifacts[0]);
    }

    private static InvalidOperationException CreateAppleNotarizationFailure(
        PowerForgeAppleAppReleaseTargetPlan app,
        AppleNotarizationResult result)
    {
        var failedStep = !result.Submission.Succeeded ||
                         !string.Equals(result.Status, "Accepted", StringComparison.OrdinalIgnoreCase)
            ? "submission"
            : result.Staple?.Succeeded == false
                ? "ticket stapling"
                : result.StapleValidation?.Succeeded == false
                    ? "ticket validation"
                    : "Gatekeeper assessment";
        var detail = result.Assessment?.StdErr ??
                     result.StapleValidation?.StdErr ??
                     result.Staple?.StdErr ??
                     result.Submission.StdErr;
        return new InvalidOperationException(
            $"Apple notarization {failedStep} failed for '{app.Name}' with notary status " +
            $"'{result.Status ?? "unknown"}' and submission '{result.SubmissionId ?? "unknown"}'. {detail}".Trim());
    }
}
