using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Verification;

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
        string? sourcePath = null)
        => new(
            RootPath: rootPath,
            RepositoryName: repositoryName,
            AdapterKind: adapterKind,
            TargetName: targetName,
            TargetKind: targetKind,
            Destination: destination,
            SourcePath: sourcePath,
            Status: status,
            Summary: summary,
            PublishedAtUtc: DateTimeOffset.UtcNow);

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
            Destination: publishReceipt.Destination,
            Status: status,
            Summary: summary,
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
            Destination: destination,
            Status: ReleaseVerificationReceiptStatus.Failed,
            Summary: summary,
            VerifiedAtUtc: DateTimeOffset.UtcNow);
}
