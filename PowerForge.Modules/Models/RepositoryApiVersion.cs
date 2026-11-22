namespace PowerForge;

/// <summary>
/// API version for PSResourceGet repository endpoints.
/// </summary>
public enum RepositoryApiVersion
{
    /// <summary>Automatic/default behavior (omit version unless required).</summary>
    Auto = 0,
    /// <summary>NuGet v2 API.</summary>
    V2 = 1,
    /// <summary>NuGet v3 API.</summary>
    V3 = 2,
    /// <summary>OCI/container registry API (Azure Container Registry, Microsoft Artifact Registry).</summary>
    ContainerRegistry = 3,
    /// <summary>Local filesystem repository API.</summary>
    Local = 4,
    /// <summary>NuGet.Server repository API.</summary>
    NugetServer = 5
}
