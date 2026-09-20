using PowerForge;
using PowerForgeStudio.Domain.Signing;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed partial class ReleasePublishExecutionService
{
    private static string? ValidateProjectGitHubAssets(DotNetRepositoryReleaseResult plan, ReleaseSigningExecutionResult signing)
    {
        var paths = plan.Projects.Where(project => project.IsPackable && !string.IsNullOrWhiteSpace(project.ReleaseZipPath))
            .Select(project => project.ReleaseZipPath!).ToArray();
        if (paths.Length == 0) return "No checkpointed release archives were selected for GitHub publishing.";
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var evidence = signing.Receipts.Where(receipt =>
            string.Equals(receipt.AdapterKind, "ProjectBuild", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(receipt.ArtifactKind, "File", StringComparison.OrdinalIgnoreCase) &&
            receipt.Status is ReleaseSigningReceiptStatus.Signed or ReleaseSigningReceiptStatus.Skipped).ToArray();
        var selected = new List<ReleaseSigningReceipt>();
        foreach (var path in paths)
        {
            if (!Path.IsPathFullyQualified(path)) return "GitHub upload plan contains a non-absolute artifact path. Rebuild and sign before publishing.";
            var receipt = evidence.FirstOrDefault(candidate => comparer.Equals(Path.GetFullPath(candidate.ArtifactPath), Path.GetFullPath(path)));
            if (receipt is null) return "GitHub upload plan contains an archive without signing checkpoint evidence. Rebuild and sign before publishing.";
            selected.Add(receipt);
        }
        // Plan generation can touch the filesystem. Check the exact upload set after it completes.
        return ReleaseSigningArtifactIntegrity.Validate(selected);
    }
}
