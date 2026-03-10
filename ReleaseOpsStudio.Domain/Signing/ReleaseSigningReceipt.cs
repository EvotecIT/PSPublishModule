namespace ReleaseOpsStudio.Domain.Signing;

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
}
