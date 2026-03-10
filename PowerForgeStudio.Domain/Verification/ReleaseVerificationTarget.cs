namespace PowerForgeStudio.Domain.Verification;

public sealed record ReleaseVerificationTarget(
    string RootPath,
    string RepositoryName,
    string AdapterKind,
    string TargetName,
    string TargetKind,
    string? Destination,
    string? SourcePath)
{
    public string DisplayName => string.IsNullOrWhiteSpace(SourcePath)
        ? TargetName
        : $"{TargetName} ({Path.GetFileName(SourcePath)})";
}

