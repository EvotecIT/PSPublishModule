using NuGet.Packaging;

namespace PowerForge;

/// <summary>Reads the authoritative id and version from a built NuGet package.</summary>
public static class NuGetPackageIdentityReader
{
    /// <summary>Returns the archive identity, or null when the package cannot be read.</summary>
    public static NuGetPackageIdentity? TryRead(string packagePath)
    {
        if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath)) return null;
        try
        {
            using var reader = new PackageArchiveReader(packagePath);
            var identity = reader.GetIdentity();
            return identity is null || string.IsNullOrWhiteSpace(identity.Id) || identity.Version is null
                ? null : new NuGetPackageIdentity(identity.Id, identity.Version.ToNormalizedString());
        }
        catch (Exception)
        {
            return null;
        }
    }
}
