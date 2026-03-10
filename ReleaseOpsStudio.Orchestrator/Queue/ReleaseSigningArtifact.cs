namespace ReleaseOpsStudio.Orchestrator.Queue;

public sealed record ReleaseSigningArtifact(
    string RepositoryName,
    ReleaseBuildAdapterKind AdapterKind,
    string ArtifactPath,
    string ArtifactKind)
{
    public string DisplayName => Path.GetFileName(ArtifactPath);

    public string AdapterDisplay => AdapterKind.ToString();
}
