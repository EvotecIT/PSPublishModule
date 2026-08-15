namespace PowerForge.Web;

public static partial class WebLlmsGenerator
{
    private static int ApplyInstallCommandPolicy(
        WebLlmsOptions options,
        IReadOnlyList<PackageInfo> packages,
        string? packageId,
        string? version,
        bool projectIsPowerShellModule,
        bool projectIsDotNetTool,
        ref string? legacyInstallCommand)
    {
        if (options.ContentKind != WebLlmsContentKind.Package)
            return 0;

        if (options.InstallCommandPolicy == WebLlmsInstallCommandPolicy.None)
        {
            legacyInstallCommand = null;
            foreach (var package in packages)
                package.InstallCommand = null;
            return 0;
        }

        if (options.InstallCommandPolicy == WebLlmsInstallCommandPolicy.Declared)
            return packages.Count == 0
                ? string.IsNullOrWhiteSpace(legacyInstallCommand) ? 0 : 1
                : packages.Count(HasInstallCommand);

        if (string.IsNullOrWhiteSpace(options.PublicationCatalogPath))
            throw new InvalidOperationException(
                "VerifiedCatalog install policy requires publicationCatalogPath.");
        var catalog = WebPublicationCatalog.Load(
            options.PublicationCatalogPath,
            options.PublicationCatalogMaxAgeHours,
            "LLMS");

        if (packages.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(packageId) ||
                !Contains(catalog, packageId, version, projectIsPowerShellModule, options))
            {
                legacyInstallCommand = null;
            }
            else
            {
                legacyInstallCommand = CreateInstallCommand(
                    packageId,
                    projectIsPowerShellModule,
                    projectIsDotNetTool,
                    version);
            }
            return string.IsNullOrWhiteSpace(legacyInstallCommand) ? 0 : 1;
        }

        foreach (var package in packages)
        {
            if (!Contains(catalog, package.Id, package.Version, package.IsPowerShellModule, options))
                package.InstallCommand = null;
            else
                package.InstallCommand = CreateInstallCommand(
                    package.Id,
                    package.IsPowerShellModule,
                    package.IsDotNetTool,
                    package.Version);
        }

        return packages.Count(HasInstallCommand);
    }

    private static bool Contains(
        WebPublicationCatalog catalog,
        string packageId,
        string? expectedVersion,
        bool isPowerShellModule,
        WebLlmsOptions options)
        => catalog.ContainsExactOwnedPackage(
            isPowerShellModule ? "powershellgallery" : "nuget",
            packageId,
            expectedVersion,
            isPowerShellModule ? options.PowerShellGalleryOwner : options.NuGetOwner);
}
