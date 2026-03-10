namespace ReleaseOpsStudio.Domain.Portfolio;

public sealed record RepositoryReleaseDrift(
    RepositoryReleaseDriftStatus Status,
    string Summary,
    string Detail)
{
    public string StatusDisplay => Status switch
    {
        RepositoryReleaseDriftStatus.Unknown => "Unknown",
        RepositoryReleaseDriftStatus.Aligned => "Aligned",
        RepositoryReleaseDriftStatus.Attention => "Attention",
        _ => Status.ToString()
    };
}
