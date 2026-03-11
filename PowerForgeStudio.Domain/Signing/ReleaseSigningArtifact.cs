namespace PowerForgeStudio.Domain.Signing;

public sealed record ReleaseSigningArtifact(
    string RepositoryName,
    string AdapterKind,
    string ArtifactPath,
    string ArtifactKind)
{
    public string DisplayName => Path.GetFileName(ArtifactPath);

    public string AdapterDisplay => AdapterKind;
}
