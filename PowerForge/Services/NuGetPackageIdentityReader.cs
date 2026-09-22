using NuGet.Packaging;
using NuGet.Versioning;

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
            return identity is null || identity.Version is null
                ? null : TryCreate(identity.Id, identity.Version.ToNormalizedString());
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal static NuGetPackageIdentity? TryCreate(string? id, string? version)
    {
        if (id is null || id.Length == 0 || id.Length > 180 ||
            !id.Any(static character => char.IsLetterOrDigit(character)) ||
            !id.All(static character => char.IsLetterOrDigit(character) || character is '.' or '_' or '-') ||
            version is null || version.Length == 0 || version.Length > 128 ||
            !NuGetVersion.TryParse(version, out var parsed))
            return null;
        return new NuGetPackageIdentity(id, parsed.ToNormalizedString());
    }
}
