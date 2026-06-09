namespace PowerForgeStudio.Domain.Portfolio;

public sealed record RepositoryAdapterEvidence(
    string AdapterDisplay,
    string StatusDisplay,
    string Summary,
    string Detail,
    string ArtifactPath,
    string OutputTail,
    string ErrorTail);

