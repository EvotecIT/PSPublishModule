namespace PowerForge;

internal sealed class DotNetRepositoryReleasePreparedContext
{
    public string RootPath { get; set; } = string.Empty;
    public DotNetRepositoryReleaseSpec Spec { get; set; } = new();
}
