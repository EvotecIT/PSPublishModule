using NuGet.Packaging;
using NuGet.Versioning;

namespace PowerForge;

internal static class ManagedModuleStandaloneInput
{
    internal static string ValidateName(string? name)
    {
        if (name is null || name.Length > PackageIdValidator.MaxPackageIdLength || !PackageIdValidator.IsValidPackageId(name))
            throw new ArgumentException($"'{name}' is not a valid module name.");
        return ManagedModulePackageIdentity.RequireSafeId(name, nameof(name));
    }

    internal static string ValidateVersion(string name, string? version)
    {
        if (version is null || version != version.Trim() || !NuGetVersion.TryParse(version, out var exact) || exact.HasMetadata)
            throw new ArgumentException($"'{name}' requires an exact package version such as '1.2.3'.");
        return ManagedModulePackageIdentity.RequireSafeVersion(exact.ToNormalizedString(), nameof(version));
    }

    internal static ManagedModuleRepository ResolveRepository(string source, string repositoryName)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            if (uri.Scheme is not ("https" or "http") || uri.UserInfo.Length != 0 ||
                uri.Query.Length != 0 || uri.Fragment.Length != 0)
                throw new ArgumentException("The source must be an HTTP(S) feed URL without credentials, a query, or a fragment, or a local folder.");
            return new ManagedModuleRepository(repositoryName, uri.AbsoluteUri.TrimEnd('/'));
        }

        return new ManagedModuleRepository(repositoryName, Path.GetFullPath(ManagedModuleRepositoryPathResolver.ResolveLocalFolder(source)),
            ManagedModuleRepositoryKind.LocalFolder);
    }
}
