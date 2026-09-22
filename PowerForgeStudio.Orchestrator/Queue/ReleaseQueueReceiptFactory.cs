using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Verification;
using PowerForgeStudio.Orchestrator.Host;

namespace PowerForgeStudio.Orchestrator.Queue;

public static class ReleaseQueueReceiptFactory
{
    public static ReleasePublishReceipt CreatePublishReceipt(
        string rootPath,
        string repositoryName,
        string adapterKind,
        string targetName,
        string targetKind,
        string? destination,
        ReleasePublishReceiptStatus status,
        string summary,
        string? sourcePath = null,
        string? packageId = null,
        string? packageVersion = null)
        => new(
            RootPath: rootPath,
            RepositoryName: repositoryName,
            AdapterKind: adapterKind,
            TargetName: targetName,
            TargetKind: targetKind,
            Destination: StudioOutputSanitizer.SanitizeDestination(destination),
            SourcePath: sourcePath,
            Status: status,
            Summary: StudioOutputSanitizer.Sanitize(summary),
            PublishedAtUtc: DateTimeOffset.UtcNow) {
            PackageId = packageId,
            PackageVersion = packageVersion,
            DestinationCredentialsOmitted = StudioOutputSanitizer.DestinationCredentialsOmitted(destination)
        };

    public static ReleasePublishReceipt FailedPublishReceipt(
        string rootPath,
        string repositoryName,
        string adapterKind,
        string targetName,
        string? destination,
        string summary,
        string? targetKind = null,
        string? sourcePath = null)
        => CreatePublishReceipt(
            rootPath,
            repositoryName,
            adapterKind,
            targetName,
            string.IsNullOrWhiteSpace(targetKind) ? targetName : targetKind!,
            destination,
            ReleasePublishReceiptStatus.Failed,
            summary,
            sourcePath);

    public static ReleasePublishReceipt SkippedPublishReceipt(
        string rootPath,
        string repositoryName,
        string adapterKind,
        string targetName,
        string? destination,
        string summary,
        string? targetKind = null,
        string? sourcePath = null)
        => CreatePublishReceipt(
            rootPath,
            repositoryName,
            adapterKind,
            targetName,
            string.IsNullOrWhiteSpace(targetKind) ? targetName : targetKind!,
            destination,
            ReleasePublishReceiptStatus.Skipped,
            summary,
            sourcePath);

    public static ReleaseVerificationReceipt CreateVerificationReceipt(
        ReleasePublishReceipt publishReceipt,
        ReleaseVerificationReceiptStatus status,
        string summary)
        => new(
            RootPath: publishReceipt.RootPath,
            RepositoryName: publishReceipt.RepositoryName,
            AdapterKind: publishReceipt.AdapterKind,
            TargetName: publishReceipt.TargetName,
            TargetKind: publishReceipt.TargetKind,
            Destination: StudioOutputSanitizer.SanitizeDestination(publishReceipt.Destination),
            Status: status,
            Summary: StudioOutputSanitizer.Sanitize(summary),
            VerifiedAtUtc: DateTimeOffset.UtcNow);

    public static ReleaseVerificationReceipt FailedVerificationReceipt(
        string rootPath,
        string repositoryName,
        string adapterKind,
        string targetName,
        string? destination,
        string summary,
        string? targetKind = null)
        => new(
            RootPath: rootPath,
            RepositoryName: repositoryName,
            AdapterKind: adapterKind,
            TargetName: targetName,
            TargetKind: string.IsNullOrWhiteSpace(targetKind) ? targetName : targetKind!,
            Destination: StudioOutputSanitizer.SanitizeDestination(destination),
            Status: ReleaseVerificationReceiptStatus.Failed,
            Summary: StudioOutputSanitizer.Sanitize(summary),
            VerifiedAtUtc: DateTimeOffset.UtcNow);
}
