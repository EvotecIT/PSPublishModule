namespace PowerForgeStudio.Domain.Signing;

public sealed record ReleaseSigningReceipt(
    string RootPath,
    string RepositoryName,
    string AdapterKind,
    string ArtifactPath,
    string ArtifactKind,
    ReleaseSigningReceiptStatus Status,
    string Summary,
    DateTimeOffset SignedAtUtc)
{
    public string ArtifactName => Path.GetFileName(ArtifactPath);

    /// <summary>SHA-256 digest of the signed file or directory contents captured after signing completed.</summary>
    public string? ContentSha256 { get; init; }
}

