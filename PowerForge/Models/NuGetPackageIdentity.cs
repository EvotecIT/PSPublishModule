namespace PowerForge;

/// <summary>Identity recorded inside a NuGet package archive.</summary>
public sealed class NuGetPackageIdentity
{
    /// <summary>Creates a package identity.</summary>
    public NuGetPackageIdentity(string id, string version)
    {
        Id = id;
        Version = version;
    }

    /// <summary>Package identifier from the archive.</summary>
    public string Id { get; }

    /// <summary>Normalized package version from the archive.</summary>
    public string Version { get; }
}
