namespace ReleaseOpsStudio.Domain.Verification;

public sealed record ReleaseVerificationReceipt(
    string RootPath,
    string RepositoryName,
    string AdapterKind,
    string TargetName,
    string TargetKind,
    string? Destination,
    ReleaseVerificationReceiptStatus Status,
    string Summary,
    DateTimeOffset VerifiedAtUtc)
{
    public string DestinationDisplay => string.IsNullOrWhiteSpace(Destination) ? "Destination not resolved" : Destination!;
}
