namespace PowerForgeStudio.Domain.Publish;

public sealed record ReleasePublishTarget(
    string RootPath,
    string RepositoryName,
    string AdapterKind,
    string TargetName,
    string TargetKind,
    string? SourcePath,
    string Destination);
