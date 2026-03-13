namespace PowerForgeStudio.Domain.Publish;

public sealed record ReleasePublishReceipt(
    string RootPath,
    string RepositoryName,
    string AdapterKind,
    string TargetName,
    string TargetKind,
    string? Destination,
    string? SourcePath,
    ReleasePublishReceiptStatus Status,
    string Summary,
    DateTimeOffset PublishedAtUtc)
{
    public string StatusDisplay => Status.ToString();
}
