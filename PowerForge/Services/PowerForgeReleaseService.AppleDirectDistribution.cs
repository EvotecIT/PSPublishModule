namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private AppleNotarizationResult NotarizeDirectAppleExport(
        PowerForgeAppleReleasePlan plan,
        PowerForgeAppleAppReleaseTargetPlan app,
        string? artifactPath = null,
        string? acceptedSubmissionId = null,
        string? expectedArtifactSha256 = null,
        bool staplingCompleted = false)
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
            StaplingCompleted = staplingCompleted,
            AcceptedCheckpoint = checkpoint => WriteAppleNotarizationAcceptance(plan, app, checkpoint),
            StapledCheckpoint = checkpoint => WriteAppleNotarizationStapled(plan, app, checkpoint),
            Timeout = TimeSpan.FromSeconds(plan.DirectDistribution.TimeoutSeconds),
            Staple = plan.DirectDistribution.Staple,
            Assess = plan.DirectDistribution.Assess
        });
        return result;
    }

    internal static string ResolveDirectAppleArtifactPath(string exportPath)
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

    private static string? MapDirectExportOutputPath(
        string privateExportPath,
        string publishedExportPath,
        string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            return outputPath;

        var fullPrivateRoot = Path.GetFullPath(privateExportPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullOutputPath = Path.GetFullPath(outputPath!);
        var comparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!fullOutputPath.Equals(fullPrivateRoot, comparison) &&
            !fullOutputPath.StartsWith(fullPrivateRoot + Path.DirectorySeparatorChar, comparison))
        {
            return outputPath;
        }

        var relative = FrameworkCompatibility.GetRelativePath(fullPrivateRoot, fullOutputPath);
        return Path.GetFullPath(Path.Combine(publishedExportPath, relative));
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
        var failedResult = failedStep switch
        {
            "submission" => result.Submission,
            "ticket stapling" => result.Staple,
            "ticket validation" => result.StapleValidation,
            _ => result.Assessment
        };
        var detail = !string.IsNullOrWhiteSpace(failedResult?.StdErr)
            ? failedResult!.StdErr
            : failedResult?.StdOut;
        return new InvalidOperationException(
            $"Apple notarization {failedStep} failed for '{app.Name}' with notary status " +
            $"'{result.Status ?? "unknown"}' and submission '{result.SubmissionId ?? "unknown"}'. {detail}".Trim());
    }
}
